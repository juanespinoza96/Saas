using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.Helpers;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Feature: recuperacion-password-jerarquica, Property 1 (parcial, jerarquía pura):
/// Aprobación autorizada por jerarquía estricta.
///
/// Verifica únicamente la lógica de rol puro de <see cref="RoleHierarchy.CanApprove"/>:
/// devuelve verdadero si y solo si rank(aprobador) &gt; rank(solicitante) según la jerarquía
/// estricta SuperAdmin(5) &gt; Dueño(4) &gt; Gerente(3) &gt; Supervisor(2) &gt; Bodeguero(1) &gt; Cajero(0).
/// Las restricciones de tenant/contexto se validan más adelante (tarea 14).
///
/// **Validates: Requirements 3.1, 3.2, 3.4**
/// </summary>
public class PasswordRecovery_Property1_RoleHierarchyTests
{
    // Roles válidos del dominio con su rango de autoridad esperado (mayor = más autoridad).
    // Esta tabla es la fuente de verdad del test y es independiente de la implementación.
    private static readonly (string Rol, int Rango)[] RolesConRango =
    {
        ("SuperAdmin", 5),
        ("Dueño", 4),
        ("Gerente", 3),
        ("Supervisor", 2),
        ("Bodeguero", 1),
        ("Cajero", 0),
    };

    // Generador de un rol válido cualquiera de la jerarquía.
    private static Gen<(string Rol, int Rango)> GenRolConRango() =>
        Gen.Elements(RolesConRango);

    /// <summary>
    /// Propiedad: para cualquier par de roles válidos (aprobador, solicitante),
    /// CanApprove devuelve verdadero exactamente cuando rank(aprobador) &gt; rank(solicitante).
    /// Esto cubre tanto los casos autorizados (rango estrictamente superior) como los
    /// no autorizados (rango igual o inferior), según Requirements 3.1, 3.2 y 3.4.
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.4**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property CanApprove_EsVerdaderoSiiRangoAprobadorEsEstrictamenteSuperior()
    {
        var gen =
            from aprobador in GenRolConRango()
            from solicitante in GenRolConRango()
            select (aprobador, solicitante);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (aprobador, solicitante) = tuple;

            var esperado = aprobador.Rango > solicitante.Rango;
            var obtenido = RoleHierarchy.CanApprove(aprobador.Rol, solicitante.Rol);

            return (obtenido == esperado)
                .Label($"CanApprove('{aprobador.Rol}'[{aprobador.Rango}], '{solicitante.Rol}'[{solicitante.Rango}]) " +
                       $"= {obtenido}, esperado {esperado}");
        });
    }

    /// <summary>
    /// Propiedad: un aprobador con rango igual o inferior al del solicitante nunca puede aprobar.
    /// Refuerza explícitamente Requirement 3.4 (rol no estrictamente inferior ⇒ operación denegada)
    /// y Requirement 3.5 en su vertiente de rol (nadie del mismo rango puede aprobar, lo que incluye
    /// el caso de un rol contra sí mismo).
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.4**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property CanApprove_RangoIgualOInferiorNuncaAprueba()
    {
        var gen =
            from aprobador in GenRolConRango()
            from solicitante in GenRolConRango()
            // Restringimos el espacio de entrada a los pares donde el aprobador NO es
            // estrictamente superior (rango igual o inferior).
            where aprobador.Rango <= solicitante.Rango
            select (aprobador, solicitante);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (aprobador, solicitante) = tuple;

            // En todo este subespacio, la aprobación debe estar denegada.
            var obtenido = RoleHierarchy.CanApprove(aprobador.Rol, solicitante.Rol);

            return (obtenido == false)
                .Label($"CanApprove('{aprobador.Rol}'[{aprobador.Rango}], '{solicitante.Rol}'[{solicitante.Rango}]) " +
                       $"debió ser false por rango no superior, pero fue {obtenido}");
        });
    }
}
