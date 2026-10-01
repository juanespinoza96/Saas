namespace SaasPOS.Application.DTOs;

public record SucursalDto(
    int Id,
    string Nombre,
    string? Direccion,
    string? Telefono,
    string? SerieFacturacion);

public record CreateSucursalRequest(
    string Nombre,
    string? Direccion,
    string? Telefono,
    string? SerieFacturacion);

public record UpdateSucursalRequest(
    string Nombre,
    string? Direccion,
    string? Telefono,
    string? SerieFacturacion);
