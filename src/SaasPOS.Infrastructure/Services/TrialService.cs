using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Implementación del servicio de prueba gratuita (trial).
/// Gestiona la creación, conversión y extensión de trials desde el panel de SuperAdmin.
/// </summary>
public partial class TrialService : ITrialService
{
    private readonly AppDbContext _context;
    private readonly ILogger<TrialService> _logger;

    /// <summary>Regex para validar RUC ecuatoriano: exactamente 13 dígitos numéricos.</summary>
    [GeneratedRegex(@"^\d{13}$")]
    private static partial Regex RucRegex();

    /// <summary>Duración estándar del período de prueba en días.</summary>
    private const int DuracionTrialDias = 15;

    public TrialService(AppDbContext context, ILogger<TrialService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TrialResultDto> CrearTrialAsync(CreateTrialRequest request, int superAdminId)
    {
        // Validar formato de RUC (exactamente 13 dígitos numéricos)
        if (string.IsNullOrWhiteSpace(request.Ruc) || !RucRegex().IsMatch(request.Ruc))
        {
            _logger.LogWarning("Intento de creación de trial con RUC inválido: {Ruc}", request.Ruc);
            throw new InvalidOperationException("RUC_INVALIDO");
        }

        // Validar RazonSocial (no vacía, entre 1 y 200 caracteres)
        if (string.IsNullOrWhiteSpace(request.RazonSocial) || request.RazonSocial.Length > 200)
        {
            _logger.LogWarning("Intento de creación de trial con RazonSocial inválida");
            throw new InvalidOperationException("RAZON_SOCIAL_INVALIDA");
        }

        try
        {
            // Verificar duplicidad de RUC en Comercios (cualquier estado)
            var comercioExistente = await _context.Comercios
                .IgnoreQueryFilters()
                .AnyAsync(c => c.Ruc == request.Ruc);

            if (comercioExistente)
            {
                _logger.LogWarning("Intento de creación de trial con RUC duplicado: {Ruc}", request.Ruc);

                // Registrar intento rechazado en audit log
                await RegistrarIntentoRechazadoAsync(request.Ruc, superAdminId, "RUC_DUPLICATE");

                throw new InvalidOperationException("RUC_DUPLICATE");
            }

            // Verificar historial de trials: buscar suscripciones Trial o Trial_Expirado asociadas al RUC
            var tieneHistorialTrial = await _context.Suscripciones
                .IgnoreQueryFilters()
                .Include(s => s.Comercio)
                .AnyAsync(s => s.Comercio.Ruc == request.Ruc
                    && (s.Estado == "Trial" || s.Estado == "Trial_Expirado"));

            if (tieneHistorialTrial)
            {
                _logger.LogWarning("Intento de creación de trial para RUC con historial previo: {Ruc}", request.Ruc);

                // Registrar intento rechazado en audit log
                await RegistrarIntentoRechazadoAsync(request.Ruc, superAdminId, "TRIAL_YA_UTILIZADO");

                throw new InvalidOperationException("TRIAL_YA_UTILIZADO");
            }
        }
        catch (InvalidOperationException)
        {
            // Re-lanzar excepciones de negocio sin envolver
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar elegibilidad de trial para RUC: {Ruc}", request.Ruc);
            throw new InvalidOperationException("VERIFICACION_NO_DISPONIBLE");
        }

        // Obtener el Plan Básico
        var planBasico = await _context.Planes
            .FirstOrDefaultAsync(p => p.Nombre == "Básico")
            ?? throw new InvalidOperationException("VERIFICACION_NO_DISPONIBLE");

        // Crear Comercio + Suscripcion de forma atómica
        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var fechaInicio = DateOnly.FromDateTime(DateTime.UtcNow);
            var fechaProximoCorte = fechaInicio.AddDays(DuracionTrialDias);

            // Crear el Comercio
            var comercio = new Comercio
            {
                Ruc = request.Ruc,
                RazonSocial = request.RazonSocial,
                PlanId = planBasico.Id,
                UsaFacturacionSRI = false,
                Estado = "Activo",
                FechaRegistro = DateTime.UtcNow
            };

            _context.Comercios.Add(comercio);
            await _context.SaveChangesAsync();

            // Crear la Suscripcion en estado Trial
            var suscripcion = new Suscripcion
            {
                ComercioId = comercio.Id,
                PlanId = planBasico.Id,
                FechaInicio = fechaInicio,
                FechaProximoCorte = fechaProximoCorte,
                MontoCuota = 0,
                EsProporcional = false,
                Estado = "Trial",
                DiasExtendidos = 0
            };

            _context.Suscripciones.Add(suscripcion);
            await _context.SaveChangesAsync();

            // Registrar audit log de creación
            var logAuditoria = new LogAuditoria
            {
                ComercioId = comercio.Id,
                UsuarioId = superAdminId,
                Accion = "Trial_Creado",
                FechaHora = DateTime.UtcNow,
                TablaAfectada = "Suscripciones",
                RegistroId = suscripcion.Id.ToString(),
                ValoresAnteriores = null,
                ValoresNuevos = JsonSerializer.Serialize(new
                {
                    comercio.Ruc,
                    comercio.RazonSocial,
                    suscripcion.Estado,
                    suscripcion.FechaInicio,
                    suscripcion.FechaProximoCorte,
                    PlanNombre = planBasico.Nombre
                })
            };

            _context.LogsAuditoria.Add(logAuditoria);
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Trial creado exitosamente: ComercioId={ComercioId}, SuscripcionId={SuscripcionId}, RUC={Ruc}",
                comercio.Id, suscripcion.Id, request.Ruc);

            return new TrialResultDto(
                comercio.Id,
                suscripcion.Id,
                comercio.RazonSocial,
                fechaInicio,
                fechaProximoCorte
            );
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error al crear trial para RUC: {Ruc}. Se realizó rollback de la transacción.", request.Ruc);
            throw new InvalidOperationException("VERIFICACION_NO_DISPONIBLE");
        }
    }

    /// <inheritdoc />
    public async Task<ConversionResultDto> ConvertirTrialAsync(int suscripcionId, ConvertTrialRequest request, int superAdminId)
    {
        // Buscar la suscripción incluyendo el Comercio (sin filtros de tenant)
        var suscripcion = await _context.Suscripciones
            .IgnoreQueryFilters()
            .Include(s => s.Comercio)
            .FirstOrDefaultAsync(s => s.Id == suscripcionId);

        // Validar que exista y esté en estado convertible
        if (suscripcion is null || (suscripcion.Estado != "Trial" && suscripcion.Estado != "Trial_Expirado"))
        {
            _logger.LogWarning("Intento de conversión no aplicable para SuscripcionId={SuscripcionId}", suscripcionId);
            throw new InvalidOperationException("CONVERSION_NO_APLICA");
        }

        // Validar que el plan seleccionado exista
        var plan = await _context.Planes.FirstOrDefaultAsync(p => p.Id == request.PlanId);
        if (plan is null)
        {
            _logger.LogWarning("Intento de conversión con PlanId inválido: {PlanId}", request.PlanId);
            throw new InvalidOperationException("PLAN_INVALIDO");
        }

        // Guardar estado anterior para auditoría
        var estadoAnterior = suscripcion.Estado;
        var eraTrialExpirado = estadoAnterior == "Trial_Expirado";
        var fechaHoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var fechaProximoCorte = fechaHoy.AddDays(30);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Actualizar suscripción
            suscripcion.Estado = "Activa";
            suscripcion.PlanId = plan.Id;
            suscripcion.MontoCuota = plan.Precio;
            suscripcion.FechaProximoCorte = fechaProximoCorte;
            suscripcion.FechaInicio = fechaHoy;

            // Actualizar el plan del comercio
            suscripcion.Comercio.PlanId = plan.Id;

            // Si era Trial_Expirado, reactivar el comercio
            if (eraTrialExpirado)
            {
                suscripcion.Comercio.Estado = "Activo";
            }

            // Registrar audit log
            var logAuditoria = new LogAuditoria
            {
                ComercioId = suscripcion.ComercioId,
                UsuarioId = superAdminId,
                Accion = "Trial_Convertido",
                FechaHora = DateTime.UtcNow,
                TablaAfectada = "Suscripciones",
                RegistroId = suscripcion.Id.ToString(),
                ValoresAnteriores = JsonSerializer.Serialize(new
                {
                    EstadoAnterior = estadoAnterior
                }),
                ValoresNuevos = JsonSerializer.Serialize(new
                {
                    Estado = "Activa",
                    PlanId = plan.Id,
                    PlanNombre = plan.Nombre,
                    MontoCuota = plan.Precio,
                    FechaProximoCorte = fechaProximoCorte,
                    FechaInicio = fechaHoy,
                    Reactivacion = eraTrialExpirado
                })
            };

            _context.LogsAuditoria.Add(logAuditoria);
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Trial convertido exitosamente: SuscripcionId={SuscripcionId}, PlanId={PlanId}, Reactivacion={Reactivacion}",
                suscripcionId, plan.Id, eraTrialExpirado);

            return new ConversionResultDto(plan.Nombre, plan.Precio, fechaProximoCorte);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error al convertir trial SuscripcionId={SuscripcionId}. Se realizó rollback.", suscripcionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ExtensionResultDto> ExtenderTrialAsync(int suscripcionId, ExtendTrialRequest request, int superAdminId)
    {
        // Buscar la suscripción ignorando filtros globales de tenant
        var suscripcion = await _context.Suscripciones
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == suscripcionId);

        // Validar que exista y esté en estado Trial
        if (suscripcion == null || suscripcion.Estado != "Trial")
        {
            _logger.LogWarning("Intento de extensión para suscripción {SuscripcionId} con estado inválido", suscripcionId);
            throw new InvalidOperationException("EXTENSION_NO_APLICA");
        }

        // Validar que DiasAdicionales esté en el rango [1, 15]
        if (request.DiasAdicionales < 1 || request.DiasAdicionales > 15)
        {
            _logger.LogWarning("Intento de extensión con días fuera de rango: {Dias}", request.DiasAdicionales);
            throw new InvalidOperationException("DIAS_FUERA_DE_RANGO");
        }

        // Validar que el acumulado de extensiones no supere 30 días
        if (suscripcion.DiasExtendidos + request.DiasAdicionales > 30)
        {
            _logger.LogWarning(
                "Extensión superaría el límite: DiasExtendidos={DiasExtendidos} + DiasAdicionales={DiasAdicionales} > 30",
                suscripcion.DiasExtendidos, request.DiasAdicionales);
            throw new InvalidOperationException("LIMITE_EXTENSION_SUPERADO");
        }

        // Aplicar la extensión
        suscripcion.FechaProximoCorte = suscripcion.FechaProximoCorte.AddDays(request.DiasAdicionales);
        suscripcion.DiasExtendidos += request.DiasAdicionales;

        // Registrar audit log de extensión
        var logAuditoria = new LogAuditoria
        {
            ComercioId = suscripcion.ComercioId,
            UsuarioId = superAdminId,
            Accion = "Trial_Extendido",
            FechaHora = DateTime.UtcNow,
            TablaAfectada = "Suscripciones",
            RegistroId = suscripcion.Id.ToString(),
            ValoresAnteriores = null,
            ValoresNuevos = JsonSerializer.Serialize(new
            {
                DiasAdicionados = request.DiasAdicionales,
                NuevaFechaProximoCorte = suscripcion.FechaProximoCorte,
                TotalDiasExtendidos = suscripcion.DiasExtendidos
            })
        };

        _context.LogsAuditoria.Add(logAuditoria);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Trial extendido: SuscripcionId={SuscripcionId}, DiasAdicionados={Dias}, NuevaFecha={Fecha}, TotalExtendidos={Total}",
            suscripcionId, request.DiasAdicionales, suscripcion.FechaProximoCorte, suscripcion.DiasExtendidos);

        return new ExtensionResultDto(suscripcion.FechaProximoCorte);
    }

    /// <inheritdoc />
    public async Task<List<TrialDto>> ObtenerTrialsAsync()
    {
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        // Consultar suscripciones en estado Trial o Trial_Expirado con datos del comercio
        var suscripciones = await _context.Suscripciones
            .IgnoreQueryFilters()
            .Include(s => s.Comercio)
            .Where(s => s.Estado == "Trial" || s.Estado == "Trial_Expirado")
            .ToListAsync();

        // Mapear a DTO calculando días restantes y ordenar por días restantes ascendente
        var trials = suscripciones
            .Select(s => new TrialDto(
                s.Id,
                s.ComercioId,
                s.Comercio.RazonSocial,
                s.Comercio.Ruc,
                s.FechaInicio,
                s.FechaProximoCorte,
                Math.Max(0, s.FechaProximoCorte.DayNumber - hoy.DayNumber),
                s.Estado
            ))
            .OrderBy(t => t.DiasRestantes)
            .ToList();

        return trials;
    }

    /// <inheritdoc />
    public async Task<TrialResumenDto> ObtenerResumenAsync()
    {
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        // Consultar suscripciones en estado Trial o Trial_Expirado
        var suscripciones = await _context.Suscripciones
            .IgnoreQueryFilters()
            .Where(s => s.Estado == "Trial" || s.Estado == "Trial_Expirado")
            .ToListAsync();

        // Calcular contadores del resumen
        var totalActivos = suscripciones.Count(s => s.Estado == "Trial");
        var porExpirar = suscripciones.Count(s =>
            s.Estado == "Trial"
            && Math.Max(0, s.FechaProximoCorte.DayNumber - hoy.DayNumber) <= 5);
        var expirados = suscripciones.Count(s => s.Estado == "Trial_Expirado");

        return new TrialResumenDto(totalActivos, porExpirar, expirados);
    }

    /// <inheritdoc />
    public async Task<bool> EsElegibleParaTrialAsync(string ruc)
    {
        // Verificar si existe algún comercio con el RUC dado (cualquier estado)
        var comercioExiste = await _context.Comercios
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Ruc == ruc);

        if (comercioExiste)
            return false;

        // Verificar si existe historial de trial para el RUC
        var tieneHistorialTrial = await _context.Suscripciones
            .IgnoreQueryFilters()
            .Include(s => s.Comercio)
            .AnyAsync(s => s.Comercio.Ruc == ruc
                && (s.Estado == "Trial" || s.Estado == "Trial_Expirado"));

        if (tieneHistorialTrial)
            return false;

        // Si no existe comercio ni historial de trial, es elegible
        return true;
    }

    /// <summary>
    /// Registra en el log de auditoría un intento rechazado de creación de trial.
    /// </summary>
    private async Task RegistrarIntentoRechazadoAsync(string ruc, int superAdminId, string motivo)
    {
        try
        {
            // Se busca un comercio ficticio con ID 0 para asociar el log cuando no hay comercio creado.
            // Como ComercioId es requerido, usamos el superAdminId como referencia del usuario.
            var logAuditoria = new LogAuditoria
            {
                ComercioId = 0, // No hay comercio asociado en intentos rechazados
                UsuarioId = superAdminId,
                Accion = "Trial_Rechazado",
                FechaHora = DateTime.UtcNow,
                TablaAfectada = "Comercios",
                RegistroId = ruc,
                ValoresAnteriores = null,
                ValoresNuevos = JsonSerializer.Serialize(new
                {
                    Ruc = ruc,
                    Motivo = motivo
                })
            };

            _context.LogsAuditoria.Add(logAuditoria);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // No se debe fallar la operación principal por un error en el log
            _logger.LogError(ex, "Error al registrar intento rechazado de trial para RUC: {Ruc}", ruc);
        }
    }
}
