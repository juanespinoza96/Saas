namespace SaasPOS.Domain;

/// <summary>
/// Fuente de verdad inmutable para roles permitidos por plan.
/// No depende de ningún servicio externo ni base de datos.
/// SuperAdmin está excluido de este mapping ya que es un rol de plataforma,
/// no un rol de comercio.
/// </summary>
public static class RolePlanMapping
{
    /// <summary>
    /// Diccionario inmutable que define los roles permitidos para cada plan de suscripción.
    /// Las comparaciones de nombres de plan son case-insensitive.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RolesPorPlan =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Básico"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Dueño", "Cajero" },
            ["Intermedio"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Dueño", "Gerente", "Cajero", "Bodeguero" },
            ["Empresarial"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Dueño", "Gerente", "Supervisor", "Cajero", "Bodeguero" }
        };

    /// <summary>
    /// Conjunto inmutable con todos los roles válidos del sistema (roles de comercio).
    /// Excluye SuperAdmin que es un rol de plataforma.
    /// </summary>
    private static readonly IReadOnlySet<string> RolesValidos =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Dueño", "Gerente", "Supervisor", "Cajero", "Bodeguero" };

    /// <summary>
    /// Conjunto vacío retornado para planes desconocidos.
    /// Evita crear nuevas instancias en cada llamada.
    /// </summary>
    private static readonly IReadOnlySet<string> ConjuntoVacio =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Retorna el conjunto de roles permitidos para un plan dado.
    /// Si el plan no existe o es nulo, retorna un conjunto vacío (nunca lanza excepción).
    /// </summary>
    /// <param name="nombrePlan">Nombre del plan (Básico, Intermedio, Empresarial). Case-insensitive.</param>
    /// <returns>Conjunto inmutable de roles permitidos, o conjunto vacío si el plan no existe.</returns>
    public static IReadOnlySet<string> GetRolesParaPlan(string nombrePlan)
    {
        if (string.IsNullOrWhiteSpace(nombrePlan))
            return ConjuntoVacio;

        return RolesPorPlan.TryGetValue(nombrePlan, out var roles)
            ? roles
            : ConjuntoVacio;
    }

    /// <summary>
    /// Verifica si un rol específico está permitido para un plan dado.
    /// Retorna false para planes desconocidos o roles no reconocidos.
    /// </summary>
    /// <param name="nombrePlan">Nombre del plan. Case-insensitive.</param>
    /// <param name="rol">Nombre del rol a verificar. Case-insensitive.</param>
    /// <returns>true si el rol está permitido en el plan; false en caso contrario.</returns>
    public static bool EsRolPermitido(string nombrePlan, string rol)
    {
        if (string.IsNullOrWhiteSpace(nombrePlan) || string.IsNullOrWhiteSpace(rol))
            return false;

        return RolesPorPlan.TryGetValue(nombrePlan, out var roles) && roles.Contains(rol);
    }

    /// <summary>
    /// Verifica si un rol es reconocido por el sistema como rol válido de comercio.
    /// SuperAdmin NO es un rol válido en este contexto.
    /// </summary>
    /// <param name="rol">Nombre del rol a verificar. Case-insensitive.</param>
    /// <returns>true si el rol es válido; false en caso contrario.</returns>
    public static bool EsRolValido(string rol)
    {
        if (string.IsNullOrWhiteSpace(rol))
            return false;

        return RolesValidos.Contains(rol);
    }

    /// <summary>
    /// Retorna el conjunto completo de todos los roles válidos del sistema.
    /// Útil para mensajes de error que listan los roles reconocidos.
    /// </summary>
    /// <returns>Conjunto inmutable con todos los roles válidos de comercio.</returns>
    public static IReadOnlySet<string> GetTodosLosRolesValidos()
    {
        return RolesValidos;
    }
}
