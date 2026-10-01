namespace SaasPOS.Application.DTOs;

public record PagoDto(
    decimal MontoPagado,
    string MetodoPago,
    string? Referencia,
    int RegistradoPor);
