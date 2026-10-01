namespace SaasPOS.Application.DTOs;

// ── Request DTOs ─────────────────────────────────────────────────────────────

public record CreateClienteRequest(
    string Identificacion,
    string Nombre,
    string? Correo,
    string? Direccion,
    string? Telefono,
    bool EsConsumidorFinal);

public record UpdateClienteRequest(
    string Nombre,
    string? Correo,
    string? Direccion,
    string? Telefono,
    bool EsConsumidorFinal);

// ── Response DTOs ────────────────────────────────────────────────────────────

public record ClienteResponse(
    int Id,
    int ComercioId,
    string Identificacion,
    string Nombre,
    string? Correo,
    string? Direccion,
    string? Telefono,
    bool EsConsumidorFinal);

public record ClienteListResponse(
    List<ClienteResponse> Items,
    int Total,
    int Page,
    int PageSize);
