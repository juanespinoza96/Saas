namespace SaasPOS.Application.DTOs;

// ── Response DTOs ────────────────────────────────────────────────────────────

public record NotificacionResponse(
    int Id,
    int ComercioId,
    int? SucursalId,
    int? ProductoId,
    string Titulo,
    string Mensaje,
    bool Leida,
    string TipoNotificacion,
    DateTime FechaEmision);

public record NotificacionListResponse(
    List<NotificacionResponse> Notificaciones,
    int TotalPendientes);
