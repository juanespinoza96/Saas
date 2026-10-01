using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Controllers.Tenants;

/// <summary>
/// Endpoints for notification management within a tenant.
/// Req 14.2: Show count of unread notifications for Gerente/Dueño roles.
/// Req 14.3: Mark notification as read.
/// Req 14.4: Tenant isolation via ComercioId verification.
/// </summary>
[ApiController]
[Route("api/tenants/notificaciones")]
public class NotificacionesController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly ITenantContext _tenantContext;

    public NotificacionesController(
        INotificationService notificationService,
        ITenantContext tenantContext)
    {
        _notificationService = notificationService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// GET /api/tenants/notificaciones — Returns pending (unread) notifications for the commerce.
    /// Req 14.2: Shows count of unread notifications.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetPendientes()
    {
        var comercioId = GetComercioId();

        var notificaciones = await _notificationService.GetNotificacionesPendientesAsync(comercioId);

        var response = new NotificacionListResponse(
            notificaciones.Select(n => new NotificacionResponse(
                n.Id,
                n.ComercioId,
                n.SucursalId,
                n.ProductoId,
                n.Titulo,
                n.Mensaje,
                n.Leida,
                n.TipoNotificacion,
                n.FechaEmision)).ToList(),
            notificaciones.Count);

        return Ok(response);
    }

    /// <summary>
    /// PATCH /api/tenants/notificaciones/{id}/leer — Marks a notification as read.
    /// Req 14.3: Update Leida = TRUE.
    /// Req 14.4: Verifies ComercioId for cross-tenant isolation.
    /// </summary>
    [HttpPatch("{id:int}/leer")]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> MarcarLeida(int id)
    {
        var comercioId = GetComercioId();

        try
        {
            await _notificationService.MarcarLeidaAsync(id, comercioId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message, code = "NOTIFICACION_NOT_FOUND" });
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private int GetComercioId() =>
        _tenantContext.ComercioId
            ?? throw new UnauthorizedAccessException("ComercioId not available in tenant context.");
}
