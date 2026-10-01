using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Implements billing logic: plan contracting with proportional cuota, daily cuts, and payments.
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
    public decimal CalcularCuotaProporcional(decimal precioPlan, DateTime fechaContratacion)
    {
        // Req 19.3: Full charge when contracted on Dia_Corte (day 3)
        if (fechaContratacion.Day == 3)
            return precioPlan;

        // Calculate next Dia_Corte (day 3)
        DateTime proximoDia3;
        if (fechaContratacion.Day < 3)
        {
            // Next day 3 is in the current month
            proximoDia3 = new DateTime(fechaContratacion.Year, fechaContratacion.Month, 3);
        }
        else
        {
            // Next day 3 is in the next month
            var nextMonth = fechaContratacion.AddMonths(1);
            proximoDia3 = new DateTime(nextMonth.Year, nextMonth.Month, 3);
        }

        // Req 19.2: (PrecioPlan / DiasTotalesMes) × DiasRestantes rounded to 2 decimals
        var diasTotalesMes = DateTime.DaysInMonth(fechaContratacion.Year, fechaContratacion.Month);
        var diasRestantes = (proximoDia3 - fechaContratacion).Days;

        var cuota = (precioPlan / diasTotalesMes) * diasRestantes;
        return Math.Round(cuota, 2);
    }

    /// <inheritdoc />
    public async Task<Suscripcion> ContratarPlanAsync(int comercioId, int planId, DateTime fechaContratacion)
    {
        // 1. Get the plan to determine the price
        var plan = await _db.Planes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == planId)
            ?? throw new InvalidOperationException("Plan no encontrado");

        // 2. Calculate cuota (Req 19.2, 19.3)
        var cuota = CalcularCuotaProporcional(plan.Precio, fechaContratacion);
        var esProporcional = fechaContratacion.Day != 3;

        // 3. Calculate FechaProximoCorte (next day 3) — Req 19.1
        DateOnly proximoCorte;
        if (fechaContratacion.Day < 3)
        {
            proximoCorte = new DateOnly(fechaContratacion.Year, fechaContratacion.Month, 3);
        }
        else
        {
            var nextMonth = fechaContratacion.AddMonths(1);
            proximoCorte = new DateOnly(nextMonth.Year, nextMonth.Month, 3);
        }

        // 4. Create Suscripcion record (Req 19.13)
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

        // 5. Audit log (Req 19.10)
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

        // 1. Find subscriptions where FechaProximoCorte is 7 days away → "Por vencer" (Req 19.4)
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
            // Enqueue email (Req 19.11)
            var gerente = await GetGerenteEmail(suscripcion.ComercioId);
            if (gerente != null)
                await _emailService.EnqueueAsync(suscripcion.ComercioId, gerente,
                    "Suscripción por vencer",
                    $"Su suscripción vence el {suscripcion.FechaProximoCorte}. Monto: ${suscripcion.MontoCuota}");
        }

        await _db.SaveChangesAsync();

        // 2. Find subscriptions where today IS the corte date and no payment → "En mora" (Req 19.5)
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
            var gerente = await GetGerenteEmail(suscripcion.ComercioId);
            if (gerente != null)
                await _emailService.EnqueueAsync(suscripcion.ComercioId, gerente,
                    "Suscripción en mora",
                    $"Su suscripción ha entrado en mora. Realice el pago de ${suscripcion.MontoCuota} para mantener el acceso.");

            // Req 19.10: Audit state change to "En mora"
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

        // 3. Find subscriptions 3 days AFTER mora without payment → suspend (Req 19.6)
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
            // Invalidate all sessions (Req 19.6)
            _jtiBlocklist.BlockAllForComercio(suscripcion.ComercioId);

            // Req 19.10: Audit state change to "Suspendido"
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

    private async Task<string?> GetGerenteEmail(int comercioId)
    {
        return await _db.Usuarios
            .IgnoreQueryFilters()
            .Where(u => u.ComercioId == comercioId && u.Rol == "Gerente" && u.Activo)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();
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
        // 1. Find the latest subscription for this comercio
        var suscripcion = await _db.Suscripciones
            .IgnoreQueryFilters()
            .Where(s => s.ComercioId == comercioId)
            .OrderByDescending(s => s.FechaInicio)
            .FirstOrDefaultAsync();

        if (suscripcion is null) return false;

        // 2. Create PagoComercio record
        var pagoRecord = new PagoComercio
        {
            ComercioId = comercioId,
            SuscripcionId = suscripcion.Id,
            MontoPagado = pago.MontoPagado,
            FechaPago = DateOnly.FromDateTime(DateTime.UtcNow),
            MetodoPago = pago.MetodoPago,
            Referencia = pago.Referencia,
            RegistradoPor = pago.RegistradoPor
        };
        _db.PagosComercio.Add(pagoRecord);

        // 3. Update subscription state to "Activa" (Req 19.9)
        suscripcion.Estado = "Activa";
        suscripcion.FechaUltimoPago = DateOnly.FromDateTime(DateTime.UtcNow);

        // 4. Recalculate FechaProximoCorte (next month, day 3)
        var today = DateTime.UtcNow;
        if (today.Day < 3)
            suscripcion.FechaProximoCorte = new DateOnly(today.Year, today.Month, 3);
        else
        {
            var nextMonth = today.AddMonths(1);
            suscripcion.FechaProximoCorte = new DateOnly(nextMonth.Year, nextMonth.Month, 3);
        }

        // 5. Reactivate Comercio if suspended (Req 19.8)
        var comercio = await _db.Comercios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == comercioId);

        if (comercio != null && comercio.Estado == "Suspendido")
            comercio.Estado = "Activo";

        await _db.SaveChangesAsync();

        // 6. Audit log (Req 19.13)
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
