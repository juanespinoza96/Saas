namespace SaasPOS.Application.DTOs;

public record PlanDto(int Id, string Nombre, decimal Precio, int LimiteUsuarios, int LimiteAtributos);
public record UpdatePlanRequest(decimal Precio, int LimiteUsuarios, int LimiteAtributos);
