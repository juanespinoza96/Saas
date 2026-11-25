using SaasPOS.Domain;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Tests unitarios para RolePlanMapping, la fuente de verdad de roles permitidos por plan.
/// Fijan la matriz plan x rol, la comparación case-insensitive, el manejo de entradas
/// inválidas (nulas, vacías, desconocidas) y la exclusión de SuperAdmin.
/// </summary>
public class RolePlanMappingTests
{
    private const string Dueno = "Dueño";
    private const string Gerente = "Gerente";
    private const string Supervisor = "Supervisor";
    private const string Cajero = "Cajero";
    private const string Bodeguero = "Bodeguero";

    private static readonly string[] TodosLosRoles = { Dueno, Gerente, Supervisor, Cajero, Bodeguero };

    /// <summary>
    /// Matriz esperada plan x rol. Es la regla de negocio vigente y se mantiene
    /// escrita aquí a propósito para que un cambio en RolePlanMapping rompa la prueba.
    /// </summary>
    private static readonly Dictionary<string, string[]> MatrizEsperada = new()
    {
        ["Básico"] = new[] { Dueno, Cajero },
        ["Intermedio"] = new[] { Dueno, Gerente, Cajero, Bodeguero },
        ["Empresarial"] = new[] { Dueno, Gerente, Supervisor, Cajero, Bodeguero }
    };

    #region Matriz exacta plan x rol

    [Theory]
    [InlineData("Básico")]
    [InlineData("Intermedio")]
    [InlineData("Empresarial")]
    public void GetRolesParaPlan_PlanConocido_RetornaConjuntoExacto(string plan)
    {
        // Act
        var roles = RolePlanMapping.GetRolesParaPlan(plan);

        // Assert: conjunto exacto, sin roles de más ni de menos
        Assert.True(roles.SetEquals(MatrizEsperada[plan]),
            $"Roles de '{plan}': esperado [{string.Join(", ", MatrizEsperada[plan])}], obtenido [{string.Join(", ", roles)}]");
    }

    [Theory]
    [InlineData("Básico", Dueno, true)]
    [InlineData("Básico", Gerente, false)]
    [InlineData("Básico", Supervisor, false)]
    [InlineData("Básico", Cajero, true)]
    [InlineData("Básico", Bodeguero, false)]
    [InlineData("Intermedio", Dueno, true)]
    [InlineData("Intermedio", Gerente, true)]
    [InlineData("Intermedio", Supervisor, false)]
    [InlineData("Intermedio", Cajero, true)]
    [InlineData("Intermedio", Bodeguero, true)]
    [InlineData("Empresarial", Dueno, true)]
    [InlineData("Empresarial", Gerente, true)]
    [InlineData("Empresarial", Supervisor, true)]
    [InlineData("Empresarial", Cajero, true)]
    [InlineData("Empresarial", Bodeguero, true)]
    public void EsRolPermitido_CombinacionPlanRol_CoincideConLaMatriz(string plan, string rol, bool esperado)
    {
        // Act
        var resultado = RolePlanMapping.EsRolPermitido(plan, rol);

        // Assert
        Assert.Equal(esperado, resultado);
    }

    [Fact]
    public void EsRolPermitido_TodasLasCombinaciones_CoincideConGetRolesParaPlan()
    {
        // Verifica que ambos métodos sean coherentes entre sí para toda la matriz
        foreach (var plan in MatrizEsperada.Keys)
        {
            var roles = RolePlanMapping.GetRolesParaPlan(plan);
            foreach (var rol in TodosLosRoles)
            {
                Assert.Equal(roles.Contains(rol), RolePlanMapping.EsRolPermitido(plan, rol));
            }
        }
    }

    #endregion

    #region Comparaciones case-insensitive

    [Theory]
    [InlineData("básico")]
    [InlineData("BÁSICO")]
    [InlineData("intermedio")]
    [InlineData("INTERMEDIO")]
    [InlineData("empresarial")]
    [InlineData("EMPRESARIAL")]
    public void GetRolesParaPlan_PlanConOtraCapitalizacion_RetornaElMismoConjunto(string plan)
    {
        // Arrange: se busca el plan canónico equivalente ignorando mayúsculas
        var canonico = MatrizEsperada.Keys.Single(k => string.Equals(k, plan, StringComparison.OrdinalIgnoreCase));

        // Act
        var roles = RolePlanMapping.GetRolesParaPlan(plan);

        // Assert
        Assert.True(roles.SetEquals(MatrizEsperada[canonico]));
    }

    [Theory]
    [InlineData("básico", "dueño", true)]
    [InlineData("BÁSICO", "CAJERO", true)]
    [InlineData("básico", "gerente", false)]
    [InlineData("INTERMEDIO", "bodeguero", true)]
    [InlineData("intermedio", "SUPERVISOR", false)]
    [InlineData("empresarial", "SUPERVISOR", true)]
    [InlineData("EMPRESARIAL", "dueño", true)]
    public void EsRolPermitido_PlanYRolConOtraCapitalizacion_IgnoraMayusculas(string plan, string rol, bool esperado)
    {
        // Act
        var resultado = RolePlanMapping.EsRolPermitido(plan, rol);

        // Assert
        Assert.Equal(esperado, resultado);
    }

    [Theory]
    [InlineData("dueño")]
    [InlineData("DUEÑO")]
    [InlineData("gerente")]
    [InlineData("SUPERVISOR")]
    [InlineData("cajero")]
    [InlineData("BODEGUERO")]
    public void EsRolValido_RolConOtraCapitalizacion_RetornaTrue(string rol)
    {
        // Act
        var resultado = RolePlanMapping.EsRolValido(rol);

        // Assert
        Assert.True(resultado);
    }

    #endregion

    #region Plan nulo, vacío, con espacios o desconocido

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" Básico ")]
    [InlineData("Premium")]
    [InlineData("Trial")]
    public void EsRolPermitido_PlanInvalidoODesconocido_RetornaFalseSinLanzarExcepcion(string? plan)
    {
        foreach (var rol in TodosLosRoles)
        {
            // Act
            var resultado = Record.Exception(() => RolePlanMapping.EsRolPermitido(plan!, rol));

            // Assert: nunca lanza y siempre deniega
            Assert.Null(resultado);
            Assert.False(RolePlanMapping.EsRolPermitido(plan!, rol));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" Básico ")]
    [InlineData("Premium")]
    [InlineData("Trial")]
    public void GetRolesParaPlan_PlanInvalidoODesconocido_RetornaConjuntoVacioSinLanzarExcepcion(string? plan)
    {
        // Act
        IReadOnlySet<string>? roles = null;
        var excepcion = Record.Exception(() => roles = RolePlanMapping.GetRolesParaPlan(plan!));

        // Assert
        Assert.Null(excepcion);
        Assert.NotNull(roles);
        Assert.Empty(roles);
    }

    #endregion

    #region Rol nulo o vacío

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EsRolPermitido_RolNuloOVacio_RetornaFalseEnTodosLosPlanes(string? rol)
    {
        foreach (var plan in MatrizEsperada.Keys)
        {
            Assert.False(RolePlanMapping.EsRolPermitido(plan, rol!));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EsRolValido_RolNuloOVacio_RetornaFalse(string? rol)
    {
        // Act
        var resultado = RolePlanMapping.EsRolValido(rol!);

        // Assert
        Assert.False(resultado);
    }

    #endregion

    #region SuperAdmin no es rol de comercio

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("superadmin")]
    public void EsRolValido_SuperAdmin_RetornaFalse(string rol)
    {
        // Act
        var resultado = RolePlanMapping.EsRolValido(rol);

        // Assert: SuperAdmin es un rol de plataforma, no de comercio
        Assert.False(resultado);
    }

    [Theory]
    [InlineData("Básico")]
    [InlineData("Intermedio")]
    [InlineData("Empresarial")]
    public void EsRolPermitido_SuperAdmin_RetornaFalseEnTodosLosPlanes(string plan)
    {
        // Act
        var resultado = RolePlanMapping.EsRolPermitido(plan, "SuperAdmin");

        // Assert
        Assert.False(resultado);
        Assert.DoesNotContain("SuperAdmin", RolePlanMapping.GetRolesParaPlan(plan), StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    #region Roles válidos de comercio

    [Theory]
    [InlineData(Dueno)]
    [InlineData(Gerente)]
    [InlineData(Supervisor)]
    [InlineData(Cajero)]
    [InlineData(Bodeguero)]
    public void EsRolValido_RolDeComercio_RetornaTrue(string rol)
    {
        // Act
        var resultado = RolePlanMapping.EsRolValido(rol);

        // Assert
        Assert.True(resultado);
    }

    [Fact]
    public void GetTodosLosRolesValidos_RetornaLosCincoRolesDeComercio()
    {
        // Act
        var roles = RolePlanMapping.GetTodosLosRolesValidos();

        // Assert
        Assert.True(roles.SetEquals(TodosLosRoles));
    }

    #endregion

    #region Regla de trial

    /// <summary>
    /// Documenta la regla vigente: el trial se crea sobre el Plan Básico (TrialService),
    /// así que admite Dueño y Cajero y NO admite Gerente. La regla antigua
    /// "en trial solo Gerente y Cajero" quedó obsoleta.
    /// </summary>
    [Fact]
    public void ComercioEnTrialPlanBasico_AdmiteDuenoYCajero_YNoAdmiteGerente()
    {
        // Arrange: el trial usa las reglas del plan Básico
        const string planDelTrial = "Básico";

        // Assert
        Assert.True(RolePlanMapping.EsRolPermitido(planDelTrial, Dueno));
        Assert.True(RolePlanMapping.EsRolPermitido(planDelTrial, Cajero));
        Assert.False(RolePlanMapping.EsRolPermitido(planDelTrial, Gerente));
    }

    #endregion
}
