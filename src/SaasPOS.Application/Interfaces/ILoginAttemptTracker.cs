namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Rastrea los intentos fallidos de login por combinación IP + email
/// dentro de una ventana de 30 minutos (Req 4.5, 4.6, 4.7).
/// Complementa la LoginPolicy del rate limiter, que bloquea TODAS las peticiones
/// al endpoint tras 3 intentos. Este tracker se usa para informar al usuario
/// cuántos intentos le quedan cuando las credenciales son inválidas.
/// </summary>
public interface ILoginAttemptTracker
{
    /// <summary>
    /// Registra un intento fallido de login para la combinación IP + email.
    /// </summary>
    /// <param name="ip">Dirección IP del cliente.</param>
    /// <param name="email">Email del intento de login (normalizado).</param>
    /// <returns>El número total de intentos fallidos en la ventana actual.</returns>
    int RecordFailedAttempt(string ip, string email);

    /// <summary>
    /// Obtiene el número actual de intentos fallidos para la combinación IP + email.
    /// </summary>
    /// <param name="ip">Dirección IP del cliente.</param>
    /// <param name="email">Email del intento de login (normalizado).</param>
    /// <returns>Número de intentos fallidos en la ventana actual.</returns>
    int GetCurrentAttempts(string ip, string email);

    /// <summary>
    /// Reinicia el contador de intentos fallidos tras un login exitoso.
    /// </summary>
    /// <param name="ip">Dirección IP del cliente.</param>
    /// <param name="email">Email del login exitoso (normalizado).</param>
    void ResetAttempts(string ip, string email);
}
