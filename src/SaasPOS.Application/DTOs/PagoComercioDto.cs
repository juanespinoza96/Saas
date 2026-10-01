namespace SaasPOS.Application.DTOs;

public record PagoComercioDto(
    int Id,
    int ComercioId,
    int SuscripcionId,
    decimal MontoPagado,
    DateOnly FechaPago,
    string MetodoPago,
    string? Referencia,
    int RegistradoPor);
