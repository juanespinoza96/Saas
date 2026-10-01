namespace SaasPOS.Domain.Entities;

public class ColaCorreo
{
    public int Id { get; set; }
    public int? ComercioId { get; set; }
    public string Destinatario { get; set; } = string.Empty;
    public string Asunto { get; set; } = string.Empty;
    public string CuerpoHtml { get; set; } = string.Empty;
    public int Intentos { get; set; }
    public int MaxIntentos { get; set; } = 3;
    /// <summary>Pendiente | Enviado | Fallido</summary>
    public string Estado { get; set; } = "Pendiente";
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaEnvio { get; set; }
    public string? UltimoError { get; set; }

    // Navigation
    public Comercio? Comercio { get; set; }
}
