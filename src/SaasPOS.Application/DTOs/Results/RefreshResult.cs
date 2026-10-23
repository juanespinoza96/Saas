namespace SaasPOS.Application.DTOs.Results;

/// <summary>
/// Resultado de la renovación silenciosa del access token (Requirements 1.3, 1.4, 1.6).
/// Tiene dos casos: <c>Ok(token)</c> cuando la sesión aún está dentro del tope absoluto y se
/// re-emite el token, y <c>Fail</c> cuando se supera el tope absoluto del rol. El controlador
/// traduce <c>Fail</c> a HTTP 401. No implica persistencia: el token renovado se devuelve en memoria.
/// </summary>
public sealed class RefreshResult
{
    /// <summary>Indica si la renovación fue exitosa (dentro del tope absoluto).</summary>
    public bool Success { get; init; }

    /// <summary>Access token (JWT) renovado; nulo cuando <see cref="Success"/> es false.</summary>
    public string? Token { get; init; }

    /// <summary>Crea un resultado exitoso con el token renovado.</summary>
    public static RefreshResult Ok(string token) => new()
    {
        Success = true,
        Token = token,
    };

    /// <summary>
    /// Crea un resultado fallido (tope absoluto superado). El controlador lo mapea a 401.
    /// </summary>
    public static RefreshResult Fail() => new()
    {
        Success = false,
    };
}
