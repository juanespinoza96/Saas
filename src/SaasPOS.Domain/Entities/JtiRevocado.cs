namespace SaasPOS.Domain.Entities;

/// <summary>
/// Token JWT individual revocado, persistido para que la revocación por "jti"
/// sobreviva a los reinicios del proceso (estado de seguridad global, sin tenant).
/// </summary>
public class JtiRevocado
{
    public int Id { get; set; }

    // Identificador único del token (claim "jti").
    public string Jti { get; set; } = string.Empty;

    // Expiración original del token (UTC). Pasada esta fecha el jti se considera
    // no bloqueado (auto-expiración lógica) y es candidato a limpieza.
    public DateTime Expiracion { get; set; }

    // Instante en que se revocó el token (UTC), para auditoría.
    public DateTime FechaRevocacion { get; set; }
}
