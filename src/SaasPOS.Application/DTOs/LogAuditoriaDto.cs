namespace SaasPOS.Application.DTOs;

/// <summary>
/// DTO for LogAuditoria responses (Req 4.2, 15.4).
/// </summary>
public record LogAuditoriaDto(
    int Id,
    int ComercioId,
    int? UsuarioId,
    string Accion,
    DateTime FechaHora,
    string TablaAfectada,
    string RegistroId,
    string? ValoresAnteriores,
    string? ValoresNuevos);

/// <summary>
/// Generic paginated result wrapper.
/// </summary>
public record PaginatedResult<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize);
