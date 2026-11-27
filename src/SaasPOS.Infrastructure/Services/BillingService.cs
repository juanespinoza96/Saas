using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Helpers;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Implementa la lógica de cobro: contratación de plan con cuota proporcional, cortes diarios y pagos.
/// </summary>
public class BillingService : IBillingService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _auditService;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;
    private readonly IJtiBlocklist _jtiBlocklist;
    private readonly ILogger<BillingService> _logger;

    public BillingService(
        AppDbContext db,
        IAuditService auditService,
        INotificationService notificationService,
        IEmailService emailService,
        IJtiBlocklist jtiBlocklist,
        ILogger<BillingService> logger)
    {
        _db = db;
        _auditService = auditService;
        _notificationService = notificationService;
        _emailService = emailService;
        _jtiBlocklist = jtiBlocklist;
        _logger = logger;
    }

    /// <inheritdoc />
    public decimal CalcularCuotaProporcional(decimal mensualidad, DateTime fechaContratacion)
    {
        // Se convierte la fecha a DateOnly y se delega en la lógica pura del ciclo de cobro.
        return CicloCobro.CuotaProporcional(mensualidad, DateOnly.FromDateTime(fechaContratacion));
    }

    /// <inheritdoc />
    public async Task<Suscripcion> ContratarPlanAsync(int comercioId, int planId, DateTime fechaContratacion)
    {
        // 1. Obtener el plan para conocer su mensualidad
        var plan = await _db.Planes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == planId)
            ?? throw new InvalidOperationException("Plan no encontrado");

        // 2. Calcular cuota, próximo corte y proporcionalidad con la lógica pura del ciclo
        var ciclo = CicloCobro.Calcular(plan.CuotaMensual, DateOnly.FromDateTime(fechaContratacion));
        var cuota = ciclo.Cuota;
        var esProporcional = ciclo.EsProporcional;
        var proximoCorte = ciclo.ProximoCorte;

        // 3. Crear el registro de Suscripcion
        var suscripcion = new Suscripcion
        {
            ComercioId = comercioId,
            PlanId = planId,
            FechaInicio = DateOnly.FromDateTime(fechaContratacion),
            FechaProximoCorte = proximoCorte,
            MontoCuota = cuota,
            EsProporcional = esProporcional,
            Estado = "Activa",
            FechaUltimoPago = null
        };

        _db.Suscripciones.Add(suscripcion);
        await _db.SaveChangesAsync();

        // 5. Registro de auditoría (Req 19.10)
        await _auditService.RegistrarAsync(
            comercioId,
            null,
            "ContratarPlan",
            "Comercios",
            comercioId.ToString(),
            null,
            new
            {
                PlanId = planId,
                MontoCuota = cuota,
                EsProporcional = esProporcional,
                FechaProximoCorte = proximoCorte.ToString()
            });

        _logger.LogInformation(
            "Plan {PlanId} contratado para comercio {ComercioId}. Cuota: {Cuota}, Proporcional: {EsProporcional}",
            planId, comercioId, cuota, esProporcional);

        return suscripcion;
    }

    /// <inheritdoc />
    public async Task ProcesarCortesDiariosAsync(DateTime fechaActual)
    {
        var hoy = DateOnly.FromDateTime(fechaActual);

        // ═══════════════════════════════════════════════════════════════
        // FASE 0: Procesamiento de Trials (antes del ciclo de cobros)
        // ═══════════════════════════════════════════════════════════════
        await ProcesarTrialsAsync(hoy);

        // 1. Buscar suscripciones cuyo FechaProximoCorte está a 7 días → "Por vencer" (Req 19.4)
        var sieteDiasAntes = hoy.AddDays(7);
        var porVencer = await _db.Suscripciones
            .IgnoreQueryFilters()
            .Include(s => s.Comercio)
            .Where(s => s.Estado == "Activa" && s.FechaProximoCorte == sieteDiasAntes && s.Comercio.Estado == "Activo")
            .ToListAsync();

        foreach (var suscripcion in porVencer)
        {
            suscripcion.Estado = "Por vencer";
            await _notificationService.CrearNotificacionPagoProximoAsync(
                suscripcion.ComercioId, suscripcion.MontoCuota, suscripcion.FechaProximoCorte.ToDateTime(TimeOnly.MinValue));
            // Encolar correo (Req 19.11)
            foreach (var destinatario in await GetDestinatariosCobroAsync(suscripcion.ComercioId))
                await _emailService.EnqueueAsync(suscripcion.ComercioId, destinatario,
                    "Suscripción por vencer",
                    $"Su suscripción vence el {suscripcion.FechaProximoCorte}. Monto: ${suscripcion.MontoCuota}");
        }

        await _db.SaveChangesAsync();

        // 2. Buscar suscripciones cuya fecha de corte es hoy y sin pago → "En mora" (Req 19.5)
        var enMora = await _db.Suscripciones
            .IgnoreQueryFilters()
            .Include(s => s.Comercio)
            .Where(s => (s.Estado == "Activa" || s.Estado == "Por vencer")
                        && s.FechaProximoCorte == hoy
                        && s.FechaUltimoPago != hoy
                        && s.Comercio.Estado == "Activo")
            .ToListAsync();

        foreach (var suscripcion in enMora)
        {
            suscripcion.Estado = "En mora";
            await _notificationService.CrearNotificacionMoraAsync(suscripcion.ComercioId);
            foreach (var destinatario in await GetDestinatariosCobroAsync(suscripcion.ComercioId))
                await _emailService.EnqueueAsync(suscripcion.ComercioId, destinatario,
                    "Suscripción en mora",
                    $"Su suscripción ha entrado en mora. Realice el pago de ${suscripcion.MontoCuota} para mantener el acceso.");

            // Req 19.10: Auditar el cambio de estado a "En mora"
            await _auditService.RegistrarAsync(
                suscripcion.ComercioId,
                null,
                "CambioEstadoMora",
                "Suscripciones",
                suscripcion.Id.ToString(),
                new { Estado = "Activa" },
                new { Estado = "En mora", FechaCorte = suscripcion.FechaProximoCorte.ToString() });
        }

        await _db.SaveChangesAsync();

        // 3. Buscar suscripciones 3 días DESPUÉS de la mora sin pago → suspender (Req 19.6)
        var tresDiasMora = hoy.AddDays(-3);
        var suspender = await _db.Suscripciones
            .IgnoreQueryFilters()
            .Include(s => s.Comercio)
            .Where(s => s.Estado == "En mora"
                        && s.FechaProximoCorte <= tresDiasMora
                        && s.Comercio.Estado == "Activo")
            .ToListAsync();

        foreach (var suscripcion in suspender)
        {
            suscripcion.Estado = "Suspendido";
            suscripcion.Comercio.Estado = "Suspendido";
            // Req 7.2: registrar que la suspensión es por mora (un pago de mensualidad la revierte)
            suscripcion.Comercio.MotivoSuspension = MotivosSuspension.Mora;
            // Invalidar todas las sesiones (Req 19.6)
            _jtiBlocklist.BlockAllForComercio(suscripcion.ComercioId);

            // Req 19.10: Auditar el cambio de estado a "Suspendido"
            await _auditService.RegistrarAsync(
                suscripcion.ComercioId,
                null,
                "CambioEstadoSuspendido",
                "Suscripciones",
                suscripcion.Id.ToString(),
                new { Estado = "En mora", ComercioActivo = true },
                new { Estado = "Suspendido", ComercioActivo = false });
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Cortes diarios procesados: {PorVencer} por vencer, {EnMora} en mora, {Suspendidos} suspendidos",
            porVencer.Count, enMora.Count, suspender.Count);
    }

    /// <summary>
    /// Destinatarios de los correos de cobro: el Dueño activo y, si existe, también el Gerente activo.
    /// Sin duplicados (comparación sin distinguir mayúsculas) y sin fallar si falta alguno.
    /// </summary>
    private async Task<List<string>> GetDestinatariosCobroAsync(int comercioId)
    {
        var destinatarios = new List<string>();
        var dueno = await GetDuenoEmail(comercioId);
        if (!string.IsNullOrWhiteSpace(dueno))
            destinatarios.Add(dueno);

        var gerente = await _db.Usuarios
            .IgnoreQueryFilters()
            .Where(u => u.ComercioId == comercioId && u.Rol == "Gerente" && u.Activo)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(gerente)
            && !destinatarios.Contains(gerente, StringComparer.OrdinalIgnoreCase))
            destinatarios.Add(gerente);

        return destinatarios;
    }

    /// <summary>
    /// Obtiene el email del usuario con rol "Dueño" activo de un comercio.
    /// </summary>
    private async Task<string?> GetDuenoEmail(int comercioId)
    {
        return await _db.Usuarios
            .IgnoreQueryFilters()
            .Where(u => u.ComercioId == comercioId && u.Rol == "Dueño" && u.Activo)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Verifica si ya existe una notificación con el mismo TipoNotificacion para la misma Suscripcion.
    /// </summary>
    private async Task<bool> NotificacionExisteAsync(int suscripcionId, string tipoNotificacion)
    {
        return await _db.Notificaciones
            .IgnoreQueryFilters()
            .AnyAsync(n => n.SuscripcionId == suscripcionId && n.TipoNotificacion == tipoNotificacion);
    }

    /// <summary>
    /// Procesa todas las suscripciones en estado Trial: genera notificaciones
    /// a 5, 2, y 0 días de expiración, y suspende las que ya expiraron.
    /// </summary>
    private async Task ProcesarTrialsAsync(DateOnly hoy)
    {
        var trials = await _db.Suscripciones
            .IgnoreQueryFilters()
            .Include(s => s.Comercio)
            .Where(s => s.Estado == "Trial")
            .ToListAsync();

        var procesados = 0;
        var errores = 0;

        foreach (var suscripcion in trials)
        {
            try
            {
                var diasRestantes = suscripcion.FechaProximoCorte.DayNumber - hoy.DayNumber;
                var razonSocial = suscripcion.Comercio.RazonSocial;

                // Determinar si corresponde crear notificación
                string? tipoNotificacion = diasRestantes switch
                {
                    5 => "Trial por Expirar",
                    2 => "Trial Urgente",
                    0 => "Trial Expirado",
                    _ => null
                };

                // Crear notificación si corresponde y no es duplicada
                if (tipoNotificacion != null)
                {
                    var yaExiste = await NotificacionExisteAsync(suscripcion.Id, tipoNotificacion);
                    if (!yaExiste)
                    {
                        var mensaje = tipoNotificacion switch
                        {
                            "Trial por Expirar" => $"Su prueba gratuita para {razonSocial} expira en {diasRestantes} días. Contacte al administrador para adquirir un plan.",
                            "Trial Urgente" => $"Su prueba gratuita para {razonSocial} expira en {diasRestantes} días. Contacte al administrador de inmediato para no perder acceso.",
                            "Trial Expirado" => $"La prueba gratuita para {razonSocial} ha expirado. Contacte al administrador para adquirir un plan y recuperar el acceso.",
                            _ => string.Empty
                        };

                        var notificacion = new Notificacion
                        {
                            ComercioId = suscripcion.ComercioId,
                            SuscripcionId = suscripcion.Id,
                            Titulo = tipoNotificacion,
                            Mensaje = mensaje,
                            TipoNotificacion = tipoNotificacion,
                            FechaEmision = DateTime.UtcNow,
                            Leida = false
                        };

                        _db.Notificaciones.Add(notificacion);

                        // Encolar email al Dueño (solo si existe Dueño activo)
                        var duenoEmail = await GetDuenoEmail(suscripcion.ComercioId);
                        if (duenoEmail != null)
                        {
                            await _emailService.EnqueueAsync(
                                suscripcion.ComercioId,
                                duenoEmail,
                                tipoNotificacion,
                                mensaje);
                        }
                    }
                }

                // Suspensión automática si el trial ya expiró (diasRestantes <= 0)
                if (diasRestantes <= 0)
                {
                    // Operación atómica: cambiar estados e invalidar sesiones
                    var estadoAnterior = suscripcion.Estado;
                    suscripcion.Estado = "Trial_Expirado";
                    suscripcion.Comercio.Estado = "Suspendido";
                    // Req 7.4: registrar que la suspensión es por trial expirado
                    suscripcion.Comercio.MotivoSuspension = MotivosSuspension.TrialExpirado;

                    await _db.SaveChangesAsync();

                    // Invalidar sesiones JTI (fuera de la transacción de BD pero dentro del try)
                    _jtiBlocklist.BlockAllForComercio(suscripcion.ComercioId);

                    // Registrar auditoría de suspensión
                    await _auditService.RegistrarAsync(
                        suscripcion.ComercioId,
                        null,
                        "TrialExpirado",
                        "Suscripciones",
                        suscripcion.Id.ToString(),
                        new { Estado = estadoAnterior, ComercioEstado = "Activo" },
                        new { Estado = "Trial_Expirado", ComercioEstado = "Suspendido", FechaSuspension = hoy.ToString() });
                }

                procesados++;
            }
            catch (Exception ex)
            {
                errores++;
                _logger.LogError(ex,
                    "Error procesando trial para suscripción {SuscripcionId} del comercio {ComercioId}",
                    suscripcion.Id, suscripcion.ComercioId);
            }
        }

        // Guardar cambios de notificaciones (las suspensiones ya se guardaron individualmente)
        await _db.SaveChangesAsync();

        if (trials.Count > 0)
        {
            _logger.LogInformation(
                "Trials procesados: {Total} total, {Procesados} exitosos, {Errores} con error",
                trials.Count, procesados, errores);
        }
    }

    /// <inheritdoc />
    public async Task<bool> RegistrarPagoAsync(int comercioId, PagoDto pago)
    {
        // 1. Buscar la suscripción más reciente de este comercio
        var suscripcion = await _db.Suscripciones
            .IgnoreQueryFilters()
            .Where(s => s.ComercioId == comercioId)
            .OrderByDescending(s => s.FechaInicio)
            .FirstOrDefaultAsync();

        if (suscripcion is null) return false;

        // 2. Un trial no admite pago de mensualidad: debe convertirse primero. No se modifica nada.
        if (suscripcion.Estado == "Trial" || suscripcion.Estado == "Trial_Expirado")
            throw new InvalidOperationException("TRIAL_REQUIERE_CONVERSION");

        // La mensualidad sale del plan vigente de la suscripción
        var plan = await _db.Planes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == suscripcion.PlanId)
            ?? throw new InvalidOperationException("Plan no encontrado");

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        // 3. Crear el registro del pago como mensualidad
        var pagoRecord = new PagoComercio
        {
            ComercioId = comercioId,
            SuscripcionId = suscripcion.Id,
            MontoPagado = pago.MontoPagado,
            FechaPago = hoy,
            MetodoPago = pago.MetodoPago,
            Referencia = pago.Referencia,
            RegistradoPor = pago.RegistradoPor,
            Tipo = TiposPago.Mensualidad
        };
        _db.PagosComercio.Add(pagoRecord);

        // 4. Actualizar la suscripción: el ciclo siguiente nunca es proporcional y usa la mensualidad completa
        var corteAnterior = suscripcion.FechaProximoCorte;
        suscripcion.Estado = "Activa";
        suscripcion.FechaUltimoPago = hoy;
        suscripcion.EsProporcional = false;
        suscripcion.MontoCuota = plan.CuotaMensual;

        // 5. Nuevo corte: primer día 3 posterior al mayor entre hoy y el corte vigente.
        //    Un pago antes del corte mueve el corte al mes siguiente y no deja la suscripción en mora.
        var baseCorte = hoy > corteAnterior ? hoy : corteAnterior;
        suscripcion.FechaProximoCorte = CicloCobro.ProximoCorteDesde(baseCorte);

        // 6. Reactivar solo si la suspensión fue por mora. Con motivo Administrativa o vacío
        //    (fail-safe) se registra el pago sin reactivar.
        var comercio = await _db.Comercios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == comercioId);

        if (comercio != null
            && comercio.Estado == "Suspendido"
            && comercio.MotivoSuspension == MotivosSuspension.Mora)
        {
            comercio.Estado = "Activo";
            comercio.MotivoSuspension = null;
        }

        await _db.SaveChangesAsync();

        // 7. Registro de auditoría (Req 19.13)
        await _auditService.RegistrarAsync(
            comercioId,
            pago.RegistradoPor,
            "RegistrarPago",
            "Comercios",
            comercioId.ToString(),
            null,
            new
            {
                MontoPagado = pago.MontoPagado,
                MetodoPago = pago.MetodoPago,
                NuevoEstado = "Activa",
                FechaProximoCorte = suscripcion.FechaProximoCorte.ToString()
            });

        _logger.LogInformation(
            "Pago registrado para comercio {ComercioId}. Monto: {Monto}, Método: {Metodo}, Próximo corte: {ProximoCorte}",
            comercioId, pago.MontoPagado, pago.MetodoPago, suscripcion.FechaProximoCorte);

        return true;
    }
}
