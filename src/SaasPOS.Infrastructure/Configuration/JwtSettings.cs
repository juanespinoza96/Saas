namespace SaasPOS.Infrastructure.Configuration;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "saas-pos-api";
    public string Audience { get; set; } = "saas-pos-clients";

    // Se conserva SOLO por compatibilidad con el binding de la cookie/antiforgery existente.
    // YA NO es el fallback de la vida del access token: la fuente de verdad de esa vida es
    // InactivityMinutesByRole (con fallback InactivityMinutesDefault). Ver el invariante abajo.
    public int ExpirationMinutes { get; set; } = 60;

    // Vida del access token (en minutos) por rol. La clave es el nombre EXACTO de Usuario.Rol,
    // incluida la "ñ" de "Dueño". Es documental: DEBE coincidir con InactivityMinutesByRole por
    // el invariante vida == ventana. La emisión del token NO lee este diccionario para fijar la
    // vida; toma el valor de InactivityMinutesByRole. Si por error difiere, la ventana manda.
    public Dictionary<string, int> AccessTokenMinutesByRole { get; set; } = new();

    // Ventana de inactividad (en minutos) por rol. Clave = Usuario.Rol exacto (incluida la "ñ").
    // Es la FUENTE DE VERDAD de la vida del access token en el backend y del reloj del frontend
    // (invariante vida == ventana): el último token emitido debe seguir siendo válido hasta el
    // deadline de inactividad, para que los avisos y "Seguir conectado" operen sobre un token
    // que el servidor aún acepta.
    public Dictionary<string, int> InactivityMinutesByRole { get; set; } = new();

    // Tope absoluto duro (en minutos) por rol, medido desde login_at. Clave = Usuario.Rol exacto.
    // Más allá de este límite el refresh NO renueva aunque haya actividad continua.
    public Dictionary<string, int> AbsoluteMaxMinutesByRole { get; set; } = new();

    // Fallback global de la ventana de inactividad para un rol no listado (Req 3.6).
    // La vida del access token de un rol no listado se iguala a este valor (NO a ExpirationMinutes),
    // para respetar el invariante vida == ventana sin fallar la emisión del token.
    public int InactivityMinutesDefault { get; set; } = 60;

    // Fallback global del tope absoluto para un rol no listado (Req 3.6). 720 min = 12 h.
    public int AbsoluteMaxMinutesDefault { get; set; } = 720;
}
