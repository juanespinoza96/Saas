namespace SaasPOS.Application.Helpers;

/// <summary>
/// Encapsula la Jerarquia_Roles y la parte de rol puro de la matriz de aprobación (Requirement 3).
/// Orden estricto de mayor a menor: SuperAdmin > Dueño > Gerente > Supervisor > Bodeguero > Cajero.
/// Esta clase decide únicamente la relación de rango entre roles; las restricciones de tenant y de
/// contexto (mismo ComercioId en el POS, o SuperAdmin aprobando a un Dueño cross-tenant) dependen de
/// datos y se aplican en el servicio (AuthService), no aquí.
/// </summary>
public static class RoleHierarchy
{
    /// <summary>
    /// Rango de autoridad por rol: a mayor número, mayor autoridad.
    /// Los nombres de rol pertenecen al dominio de negocio y por convención van en español.
    /// </summary>
    private static readonly Dictionary<string, int> Rank = new()
    {
        ["SuperAdmin"] = 5,
        ["Dueño"] = 4,
        ["Gerente"] = 3,
        ["Supervisor"] = 2,
        ["Bodeguero"] = 1,
        ["Cajero"] = 0,
    };

    /// <summary>
    /// Devuelve el rango de autoridad del rol indicado.
    /// Un rol desconocido, nulo o vacío devuelve -1 para que nunca pueda aprobar ni ser
    /// tratado como un rol válido en la comparación (queda por debajo de cualquier rol conocido).
    /// </summary>
    /// <param name="role">El nombre del rol (dominio en español, p. ej. "Dueño", "Cajero").</param>
    /// <returns>El rango del rol, o -1 si el rol es nulo, vacío o no está en la jerarquía.</returns>
    public static int RankOf(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return -1;
        }

        return Rank.TryGetValue(role, out var rango) ? rango : -1;
    }

    /// <summary>
    /// Indica si <paramref name="approverRole"/> puede aprobar a <paramref name="requesterRole"/>
    /// según la jerarquía estricta.
    /// Regla base: rank(approver) &gt; rank(requester) (jerarquía estricta, sin igualdad).
    /// Como un rol desconocido/nulo tiene rango -1, nunca podrá aprobar ni ser aprobado por debajo.
    ///
    /// Las restricciones adicionales de contexto se aplican en el servicio porque dependen de datos:
    /// - Dueño de Gerente: solo un Dueño (rank 4) sobre un Gerente (rank 3), dentro del mismo ComercioId.
    /// - Dueño solo por SuperAdmin (cross-tenant).
    /// </summary>
    /// <param name="approverRole">Rol del aprobador.</param>
    /// <param name="requesterRole">Rol del usuario solicitante.</param>
    /// <returns>Verdadero si el aprobador tiene un rango estrictamente superior al del solicitante.</returns>
    public static bool CanApprove(string approverRole, string requesterRole)
    {
        var rangoAprobador = RankOf(approverRole);
        var rangoSolicitante = RankOf(requesterRole);

        // Un rol desconocido (rango -1) nunca aprueba: aunque el solicitante también sea -1,
        // la comparación estricta (-1 > -1) es falsa, por lo que la operación queda denegada.
        return rangoAprobador > rangoSolicitante;
    }
}
