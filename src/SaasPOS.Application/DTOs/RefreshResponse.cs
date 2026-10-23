namespace SaasPOS.Application.DTOs;

/// <summary>
/// Respuesta del endpoint de renovación silenciosa de sesión
/// (POST /api/tenants/auth/refresh, Requirements 1.3, 1.6).
/// Tiene el mismo shape que la respuesta de login (<c>{ "token": "&lt;jwt&gt;" }</c>):
/// únicamente devuelve el access token renovado, SIN persistencia, sin entidad ni tabla nueva.
/// </summary>
public class RefreshResponse
{
    /// <summary>Access token (JWT) renovado que el cliente debe usar a partir de ahora.</summary>
    public string Token { get; set; } = string.Empty;
}
