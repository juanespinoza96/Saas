using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/comercios")]
[Authorize(Policy = "SuperAdminOnly")]
public class ComerciosController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IJtiBlocklist _jtiBlocklist;
    private readonly IAuditService _auditService;
    private readonly IBillingService _billingService;

    public ComerciosController(AppDbContext db, IJtiBlocklist jtiBlocklist, IAuditService auditService, IBillingService billingService)
    {
        _db = db;
        _jtiBlocklist = jtiBlocklist;
        _auditService = auditService;
        _billingService = billingService;
    }

    /// <summary>
    /// GET /api/admin/comercios — List all comercios with plan info
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var comercios = await _db.Comercios
            .IgnoreQueryFilters()
            .Include(c => c.Plan)
            .Include(c => c.Suscripciones)
            .Select(c => new
            {
                c.Id,
                c.Ruc,
                c.RazonSocial,
                c.PlanId,
                PlanNombre = c.Plan.Nombre,
                c.Estado,
                c.UsaFacturacionSRI,
                c.FechaRegistro,
                FechaProximoCorte = c.Suscripciones
                    .OrderByDescending(s => s.FechaInicio)
                    .Select(s => (DateOnly?)s.FechaProximoCorte)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(comercios);
    }

    /// <summary>
    /// POST /api/admin/comercios — Create new comercio (Req 2.1, 2.2, 17.1)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateComercioRequest request)
    {
        // Validate RUC uniqueness (Req 2.2)
        var rucExists = await _db.Comercios
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Ruc == request.Ruc);

        if (rucExists)
            return Conflict(new { error = "El RUC ya está registrado.", code = "RUC_DUPLICATE" });

        // Validate Plan exists
        var plan = await _db.Planes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == request.PlanId);

        if (plan is null)
            return NotFound(new { error = "Plan no encontrado.", code = "PLAN_NOT_FOUND" });

        var comercio = new Comercio
        {
            Ruc = request.Ruc,
            RazonSocial = request.RazonSocial,
            PlanId = request.PlanId,
            UsaFacturacionSRI = request.UsaFacturacionSRI,
            Estado = "Activo",
            FechaRegistro = DateTime.UtcNow
        };

        _db.Comercios.Add(comercio);
        await _db.SaveChangesAsync();

        // Create ConfiguracionesComercio with default values (Req 17.1)
        var configuracion = new ConfiguracionComercio
        {
            ComercioId = comercio.Id,
            EsBarEscolar = false,
            MostrarBotonCliente = true,
            PermiteVentaEnNegativo = false,
            ImpresionAutomaticaTicket = false
        };

        _db.ConfiguracionesComercio.Add(configuracion);
        await _db.SaveChangesAsync();

        // Req 15.1: Audit log for comercio creation
        var usuarioId = GetUsuarioId();
        await _auditService.RegistrarAsync(
            comercio.Id,
            usuarioId,
            "Crear",
            "Comercios",
            comercio.Id.ToString(),
            null,
            new { comercio.Id, comercio.Ruc, comercio.RazonSocial, comercio.PlanId, comercio.UsaFacturacionSRI, comercio.Estado });

        var dto = new ComercioDto(
            comercio.Id,
            comercio.Ruc,
            comercio.RazonSocial,
            comercio.PlanId,
            plan.Nombre,
            comercio.Estado,
            comercio.UsaFacturacionSRI,
            comercio.FechaRegistro);

        return CreatedAtAction(nameof(GetById), new { id = comercio.Id }, dto);
    }

    /// <summary>
    /// GET /api/admin/comercios/{id} — Get single comercio detail
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var comercio = await _db.Comercios
            .IgnoreQueryFilters()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (comercio is null)
            return NotFound(new { error = "Comercio no encontrado.", code = "COMERCIO_NOT_FOUND" });

        var dto = new ComercioDto(
            comercio.Id,
            comercio.Ruc,
            comercio.RazonSocial,
            comercio.PlanId,
            comercio.Plan.Nombre,
            comercio.Estado,
            comercio.UsaFacturacionSRI,
            comercio.FechaRegistro);

        return Ok(dto);
    }

    /// <summary>
    /// PUT /api/admin/comercios/{id} — Update comercio (RazonSocial, PlanId, UsaFacturacionSRI)
    /// Req 1.6: Si PlanId cambia, desactiva usuarios con roles no permitidos por el nuevo plan.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateComercioRequest request)
    {
        var comercio = await _db.Comercios
            .IgnoreQueryFilters()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (comercio is null)
            return NotFound(new { error = "Comercio no encontrado.", code = "COMERCIO_NOT_FOUND" });

        // Validate Plan exists
        var plan = await _db.Planes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == request.PlanId);

        if (plan is null)
            return NotFound(new { error = "Plan no encontrado.", code = "PLAN_NOT_FOUND" });

        // Capture previous values for audit
        var valoresAnteriores = new { comercio.Id, comercio.RazonSocial, PlanId = comercio.PlanId, comercio.UsaFacturacionSRI };

        var planCambio = comercio.PlanId != request.PlanId;

        comercio.RazonSocial = request.RazonSocial;
        comercio.PlanId = request.PlanId;
        comercio.UsaFacturacionSRI = request.UsaFacturacionSRI;

        await _db.SaveChangesAsync();

        // Req 1.6: Si el plan cambió, desactivar usuarios con roles no permitidos por el nuevo plan
        if (planCambio)
        {
            await DesactivarUsuariosConRolesInvalidosAsync(id, plan.Nombre);
        }

        // Req 15.1: Audit log for comercio update
        var usuarioId = GetUsuarioId();
        await _auditService.RegistrarAsync(
            comercio.Id,
            usuarioId,
            "Actualizar",
            "Comercios",
            comercio.Id.ToString(),
            valoresAnteriores,
            new { comercio.Id, comercio.RazonSocial, comercio.PlanId, comercio.UsaFacturacionSRI });

        var dto = new ComercioDto(
            comercio.Id,
            comercio.Ruc,
            comercio.RazonSocial,
            comercio.PlanId,
            plan.Nombre,
            comercio.Estado,
            comercio.UsaFacturacionSRI,
            comercio.FechaRegistro);

        return Ok(dto);
    }

    /// <summary>
    /// PATCH /api/admin/comercios/{id}/suspender — Suspend comercio (Req 2.3)
    /// Sets Activo=false and invalidates all active sessions via blocklist.
    /// </summary>
    [HttpPatch("{id:int}/suspender")]
    public async Task<IActionResult> Suspender(int id)
    {
        var comercio = await _db.Comercios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (comercio is null)
            return NotFound(new { error = "Comercio no encontrado.", code = "COMERCIO_NOT_FOUND" });

        comercio.Estado = "Suspendido";
        await _db.SaveChangesAsync();

        // Invalidate all active sessions for this comercio (Req 2.3)
        _jtiBlocklist.BlockAllForComercio(id);

        // Req 15.1: Audit log for comercio suspension
        var usuarioId = GetUsuarioId();
        await _auditService.RegistrarAsync(
            id,
            usuarioId,
            "Suspender",
            "Comercios",
            id.ToString(),
            new { Estado = "Activo" },
            new { Estado = "Suspendido" });

        return Ok(new { message = "Comercio suspendido exitosamente." });
    }

    /// <summary>
    /// PATCH /api/admin/comercios/{id}/reactivar — Reactivate comercio (Req 2.4)
    /// Sets Activo=true. Previously active users regain access automatically
    /// because the blocklist timestamp is in the past for new tokens.
    /// </summary>
    [HttpPatch("{id:int}/reactivar")]
    public async Task<IActionResult> Reactivar(int id)
    {
        var comercio = await _db.Comercios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (comercio is null)
            return NotFound(new { error = "Comercio no encontrado.", code = "COMERCIO_NOT_FOUND" });

        comercio.Estado = "Activo";
        await _db.SaveChangesAsync();

        // Req 15.1: Audit log for comercio reactivation
        var usuarioId = GetUsuarioId();
        await _auditService.RegistrarAsync(
            id,
            usuarioId,
            "Reactivar",
            "Comercios",
            id.ToString(),
            new { Estado = "Suspendido" },
            new { Estado = "Activo" });

        return Ok(new { message = "Comercio reactivado exitosamente." });
    }

    /// <summary>
    /// PATCH /api/admin/comercios/{id}/plan — Change plan (Req 2.5, Req 1.6)
    /// Updates PlanId on the Comercio; SubscriptionMiddleware applies new limits immediately.
    /// Si el nuevo plan no permite ciertos roles, los usuarios con esos roles quedan inactivos.
    /// </summary>
    [HttpPatch("{id:int}/plan")]
    public async Task<IActionResult> CambiarPlan(int id, [FromBody] CambiarPlanRequest request)
    {
        var comercio = await _db.Comercios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (comercio is null)
            return NotFound(new { error = "Comercio no encontrado.", code = "COMERCIO_NOT_FOUND" });

        var plan = await _db.Planes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == request.PlanId);

        if (plan is null)
            return NotFound(new { error = "Plan no encontrado.", code = "PLAN_NOT_FOUND" });

        comercio.PlanId = request.PlanId;
        await _db.SaveChangesAsync();

        // Req 1.6: Desactivar usuarios con roles no permitidos por el nuevo plan
        var usuariosDesactivados = await DesactivarUsuariosConRolesInvalidosAsync(id, plan.Nombre);

        return Ok(new { message = "Plan actualizado exitosamente.", planId = plan.Id, planNombre = plan.Nombre, usuariosDesactivados });
    }

    /// <summary>
    /// GET /api/admin/comercios/{id}/pagos — Historial de pagos
    /// </summary>
    [HttpGet("{id:int}/pagos")]
    public async Task<IActionResult> GetPagos(int id)
    {
        var pagos = await _db.PagosComercio
            .IgnoreQueryFilters()
            .Where(p => p.ComercioId == id)
            .OrderByDescending(p => p.FechaPago)
            .Select(p => new PagoComercioDto(
                p.Id,
                p.ComercioId,
                p.SuscripcionId,
                p.MontoPagado,
                p.FechaPago,
                p.MetodoPago,
                p.Referencia,
                p.RegistradoPor))
            .ToListAsync();

        return Ok(pagos);
    }

    /// <summary>
    /// POST /api/admin/comercios/{id}/pagos — Registrar pago manual (Req 19.8, 20.6)
    /// </summary>
    [HttpPost("{id:int}/pagos")]
    public async Task<IActionResult> RegistrarPago(int id, [FromBody] PagoDto request)
    {
        var success = await _billingService.RegistrarPagoAsync(id, request);
        if (!success)
            return NotFound(new { error = "No se encontró suscripción activa para el comercio.", code = "SUSCRIPCION_NOT_FOUND" });

        return Ok(new { message = "Pago registrado exitosamente." });
    }

    /// <summary>
    /// Req 1.6: Desactiva usuarios activos cuyo rol no está permitido por el nuevo plan.
    /// El rol Dueño está permitido en todos los planes, por lo que nunca se desactiva.
    /// </summary>
    /// <param name="comercioId">ID del comercio cuyo plan cambió.</param>
    /// <param name="nombrePlan">Nombre del nuevo plan asignado al comercio.</param>
    /// <returns>Cantidad de usuarios desactivados.</returns>
    private async Task<int> DesactivarUsuariosConRolesInvalidosAsync(int comercioId, string nombrePlan)
    {
        var rolesPermitidos = RolePlanMapping.GetRolesParaPlan(nombrePlan);

        // Obtener usuarios activos del comercio cuyo rol NO está en los roles permitidos del nuevo plan
        var usuariosInvalidos = await _db.Usuarios
            .IgnoreQueryFilters()
            .Where(u => u.ComercioId == comercioId && u.Activo)
            .ToListAsync();

        var desactivados = 0;
        foreach (var usuario in usuariosInvalidos)
        {
            if (!rolesPermitidos.Contains(usuario.Rol))
            {
                usuario.Activo = false;
                desactivados++;
            }
        }

        if (desactivados > 0)
        {
            await _db.SaveChangesAsync();
        }

        return desactivados;
    }

    private int? GetUsuarioId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return sub is not null ? int.Parse(sub) : null;
    }
}

/// <summary>
/// Request body for the PATCH /api/admin/comercios/{id}/plan endpoint
/// </summary>
public record CambiarPlanRequest(int PlanId);
