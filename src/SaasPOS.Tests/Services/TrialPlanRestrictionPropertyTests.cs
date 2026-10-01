using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

// Feature: prueba-gratuita, Property 4: Restricciones de plan durante trial
/// <summary>
/// Property-based tests para restricciones del plan durante período de trial.
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
///
/// Property 4: Restricciones de plan durante trial
/// "For any comercio con Suscripción en Estado 'Trial': (a) los límites del Plan Básico aplican
/// (max 2 usuarios, 1 sucursal, 2 atributos por categoría), (b) reportes están bloqueados,
/// (c) facturación electrónica SRI está bloqueada, y (d) los únicos roles permitidos son
/// Gerente y Cajero."
/// </summary>
public class TrialPlanRestrictionPropertyTests
{
    private static readonly Mock<ILogger<SubscriptionGuard>> LoggerMock = new();

    /// <summary>
    /// Roles que deben ser PERMITIDOS durante el trial.
    /// </summary>
    private static readonly string[] RolesPermitidos = { "Gerente", "Cajero" };

    /// <summary>
    /// Roles que deben ser DENEGADOS durante el trial.
    /// </summary>
    private static readonly string[] RolesDenegados = { "Dueño", "Supervisor", "Bodeguero" };

    /// <summary>
    /// Crea un contexto InMemory con ITenantContext configurado como SuperAdmin
    /// para evitar query filters.
    /// </summary>
    private static (AppDbContext db, SubscriptionGuard guard) CreateContext()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);
        var guard = new SubscriptionGuard(db, LoggerMock.Object);
        return (db, guard);
    }

    /// <summary>
    /// Configura un comercio con Plan Básico y una suscripción en estado "Trial".
    /// </summary>
    private static (Comercio comercio, Suscripcion suscripcion) SeedComercioEnTrial(
        AppDbContext db,
        int comercioId = 1,
        bool usaFacturacionSRI = false)
    {
        var plan = new Plan
        {
            Id = 1,
            Nombre = "Básico",
            Precio = 350m,
            LimiteUsuarios = 2,
            LimiteAtributos = 2,
            LimiteSucursales = 1
        };
        db.Planes.Add(plan);

        var comercio = new Comercio
        {
            Id = comercioId,
            Ruc = "0912345678001",
            RazonSocial = "Comercio Trial Test",
            PlanId = 1,
            Plan = plan,
            Estado = "Activo",
            UsaFacturacionSRI = usaFacturacionSRI,
            FechaRegistro = DateTime.UtcNow
        };
        db.Comercios.Add(comercio);

        var suscripcion = new Suscripcion
        {
            Id = 1,
            ComercioId = comercioId,
            PlanId = 1,
            FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow),
            FechaProximoCorte = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
            MontoCuota = 0m,
            EsProporcional = false,
            Estado = "Trial"
        };
        db.Suscripciones.Add(suscripcion);
        db.SaveChanges();

        return (comercio, suscripcion);
    }

    #region Property 4a: Límites del Plan Básico aplican durante Trial

    /// <summary>
    /// Property 4a.1: Durante trial con Plan Básico, máximo 2 usuarios.
    /// Cuando ya hay 2 usuarios activos, CanAddUser debe retornar false.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Trial_LimiteUsuarios_DeniegaCuandoAlcanzaMaximo()
    {
        // Generador: cantidad de usuarios ya existentes entre 0 y 5
        var userCountGen = Gen.Choose(0, 5);

        return Prop.ForAll(userCountGen.ToArbitrary(), (int existingUsers) =>
        {
            var (db, guard) = CreateContext();
            using (db)
            {
                SeedComercioEnTrial(db);

                // Agregar usuarios activos existentes
                for (int i = 0; i < existingUsers; i++)
                {
                    db.Usuarios.Add(new Usuario
                    {
                        Id = 100 + i,
                        ComercioId = 1,
                        Nombre = $"Usuario {i}",
                        Email = $"user{i}@test.com",
                        PasswordHash = "hash",
                        Rol = "Cajero",
                        Activo = true
                    });
                }
                db.SaveChanges();

                var result = guard.CanAddUserAsync(comercioId: 1).GetAwaiter().GetResult();

                // Plan Básico: max 2 usuarios. Si ya hay >= 2, no se puede agregar.
                var expected = existingUsers < 2;
                return (result == expected).ToProperty()
                    .Label($"Con {existingUsers} usuarios existentes, esperado={expected} pero obtenido={result}");
            }
        });
    }

    /// <summary>
    /// Property 4a.2: Durante trial con Plan Básico, máximo 1 sucursal.
    /// Cuando ya hay 1 sucursal, CanAddSucursal debe retornar false.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Trial_LimiteSucursales_DeniegaCuandoAlcanzaMaximo()
    {
        // Generador: 0 o más sucursales existentes
        var sucCountGen = Gen.Choose(0, 3);

        return Prop.ForAll(sucCountGen.ToArbitrary(), (int existingSucursales) =>
        {
            var (db, guard) = CreateContext();
            using (db)
            {
                SeedComercioEnTrial(db);

                // Agregar sucursales existentes
                for (int i = 0; i < existingSucursales; i++)
                {
                    db.Sucursales.Add(new Sucursal
                    {
                        Id = 100 + i,
                        ComercioId = 1,
                        Nombre = $"Sucursal {i}"
                    });
                }
                db.SaveChanges();

                var result = guard.CanAddSucursalAsync(comercioId: 1).GetAwaiter().GetResult();

                // Plan Básico: max 1 sucursal. Si ya hay >= 1, no se puede agregar.
                var expected = existingSucursales < 1;
                return (result == expected).ToProperty()
                    .Label($"Con {existingSucursales} sucursales existentes, esperado={expected} pero obtenido={result}");
            }
        });
    }

    /// <summary>
    /// Property 4a.3: Durante trial con Plan Básico, máximo 2 atributos por categoría.
    /// Cuando ya hay 2 atributos en la categoría, CanAddAtributoCategoria debe retornar false.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Trial_LimiteAtributos_DeniegaCuandoAlcanzaMaximo()
    {
        // Generador: cantidad de atributos existentes en la categoría
        var attrCountGen = Gen.Choose(0, 5);

        return Prop.ForAll(attrCountGen.ToArbitrary(), (int existingAttrs) =>
        {
            var (db, guard) = CreateContext();
            using (db)
            {
                SeedComercioEnTrial(db);

                // Agregar categoría
                var categoria = new Categoria
                {
                    Id = 1,
                    ComercioId = 1,
                    Nombre = "Categoría Test"
                };
                db.Categorias.Add(categoria);

                // Agregar atributos existentes a la categoría
                for (int i = 0; i < existingAttrs; i++)
                {
                    db.AtributosCategoria.Add(new AtributoCategoria
                    {
                        Id = 100 + i,
                        CategoriaId = 1,
                        NombreAtributo = $"Atributo {i}",
                        TipoDato = "Texto"
                    });
                }
                db.SaveChanges();

                var result = guard.CanAddAtributoCategoriaAsync(comercioId: 1, categoriaId: 1)
                    .GetAwaiter().GetResult();

                // Plan Básico: max 2 atributos. Si ya hay >= 2, no se puede agregar.
                var expected = existingAttrs < 2;
                return (result == expected).ToProperty()
                    .Label($"Con {existingAttrs} atributos existentes, esperado={expected} pero obtenido={result}");
            }
        });
    }

    #endregion

    #region Property 4b: Reportes bloqueados durante Trial

    /// <summary>
    /// Property 4b: Para cualquier comercio con suscripción en estado "Trial",
    /// CanGenerateReportAsync siempre retorna false independientemente de la configuración.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Trial_ReportesBloqueados_SiempreRetornaFalse()
    {
        // Generador: booleano para UsaFacturacionSRI (demostrar que es independiente)
        var boolGen = Arb.Generate<bool>();

        return Prop.ForAll(boolGen.ToArbitrary(), (bool usaSri) =>
        {
            var (db, guard) = CreateContext();
            using (db)
            {
                SeedComercioEnTrial(db, usaFacturacionSRI: usaSri);

                var result = guard.CanGenerateReportAsync(comercioId: 1).GetAwaiter().GetResult();

                // Reportes SIEMPRE bloqueados durante trial
                return (!result).ToProperty()
                    .Label($"Reportes deberían estar bloqueados durante trial (usaSri={usaSri})");
            }
        });
    }

    #endregion

    #region Property 4c: Facturación electrónica SRI bloqueada durante Trial

    /// <summary>
    /// Property 4c: Para cualquier comercio con suscripción en estado "Trial",
    /// CanEmitirFacturaElectronicaAsync siempre retorna false, independientemente
    /// de si UsaFacturacionSRI está habilitado.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Trial_SRIBloqueado_SiempreRetornaFalse()
    {
        // Generador: booleano para UsaFacturacionSRI (probar ambos casos)
        var boolGen = Arb.Generate<bool>();

        return Prop.ForAll(boolGen.ToArbitrary(), (bool usaSri) =>
        {
            var (db, guard) = CreateContext();
            using (db)
            {
                SeedComercioEnTrial(db, usaFacturacionSRI: usaSri);

                var result = guard.CanEmitirFacturaElectronicaAsync(comercioId: 1).GetAwaiter().GetResult();

                // SRI SIEMPRE bloqueado durante trial, sin importar configuración
                return (!result).ToProperty()
                    .Label($"SRI debería estar bloqueado durante trial (usaSri={usaSri})");
            }
        });
    }

    #endregion

    #region Property 4d: Solo roles Gerente y Cajero permitidos durante Trial

    /// <summary>
    /// Property 4d.1: Para cualquier comercio en trial, los roles "Gerente" y "Cajero"
    /// siempre son permitidos por CanUseRoleAsync.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Trial_RolesPermitidos_GerenteYCajero_RetornaTrue()
    {
        // Generador: selecciona uno de los roles permitidos
        var rolGen = Gen.Elements(RolesPermitidos);

        return Prop.ForAll(rolGen.ToArbitrary(), (string rol) =>
        {
            var (db, guard) = CreateContext();
            using (db)
            {
                SeedComercioEnTrial(db);

                var result = guard.CanUseRoleAsync(comercioId: 1, role: rol).GetAwaiter().GetResult();

                return result.ToProperty()
                    .Label($"Rol '{rol}' debería estar permitido durante trial pero fue denegado");
            }
        });
    }

    /// <summary>
    /// Property 4d.2: Para cualquier comercio en trial, los roles "Dueño", "Supervisor"
    /// y "Bodeguero" siempre son denegados por CanUseRoleAsync.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Trial_RolesDenegados_DuenoSupervisorBodeguero_RetornaFalse()
    {
        // Generador: selecciona uno de los roles que deben ser denegados
        var rolGen = Gen.Elements(RolesDenegados);

        return Prop.ForAll(rolGen.ToArbitrary(), (string rol) =>
        {
            var (db, guard) = CreateContext();
            using (db)
            {
                SeedComercioEnTrial(db);

                var result = guard.CanUseRoleAsync(comercioId: 1, role: rol).GetAwaiter().GetResult();

                return (!result).ToProperty()
                    .Label($"Rol '{rol}' debería estar denegado durante trial pero fue permitido");
            }
        });
    }

    /// <summary>
    /// Property 4d.3: Para cualquier string aleatorio que NO sea "Gerente" ni "Cajero",
    /// CanUseRoleAsync retorna false durante trial.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Trial_RolAleatorio_NoPermitido_SiNoEsGerenteOCajero()
    {
        // Generador: strings no vacíos aleatorios
        var rolGen = Arb.Generate<NonEmptyString>().Select(s => s.Get);

        return Prop.ForAll(rolGen.ToArbitrary(), (string rol) =>
        {
            // Solo evaluar roles que NO sean Gerente ni Cajero
            if (string.Equals(rol, "Gerente", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rol, "Cajero", StringComparison.OrdinalIgnoreCase))
                return true.ToProperty(); // Se descarta (trivially true)

            var (db, guard) = CreateContext();
            using (db)
            {
                SeedComercioEnTrial(db);

                var result = guard.CanUseRoleAsync(comercioId: 1, role: rol).GetAwaiter().GetResult();

                return (!result).ToProperty()
                    .Label($"Rol arbitrario '{rol}' debería estar denegado durante trial pero fue permitido");
            }
        });
    }

    #endregion

    #region Property 4 combinada: Todas las restricciones se aplican simultáneamente

    /// <summary>
    /// Property 4 combinada: Para cualquier comercio en estado trial, las 4 restricciones
    /// se aplican simultáneamente: límites de Plan Básico + reportes bloqueados + SRI bloqueado
    /// + solo roles Gerente/Cajero.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Trial_TodasLasRestricciones_SeAplicanSimultaneamente()
    {
        // Generador: booleanos para configuración variada
        var boolGen = Arb.Generate<bool>();

        return Prop.ForAll(boolGen.ToArbitrary(), (bool usaSri) =>
        {
            var (db, guard) = CreateContext();
            using (db)
            {
                SeedComercioEnTrial(db, usaFacturacionSRI: usaSri);

                // Agregar 2 usuarios (alcanzar límite)
                db.Usuarios.Add(new Usuario { Id = 10, ComercioId = 1, Nombre = "U1", Email = "u1@t.com", PasswordHash = "h", Rol = "Gerente", Activo = true });
                db.Usuarios.Add(new Usuario { Id = 11, ComercioId = 1, Nombre = "U2", Email = "u2@t.com", PasswordHash = "h", Rol = "Cajero", Activo = true });
                // Agregar 1 sucursal (alcanzar límite)
                db.Sucursales.Add(new Sucursal { Id = 10, ComercioId = 1, Nombre = "Suc1" });
                // Agregar categoría con 2 atributos (alcanzar límite)
                db.Categorias.Add(new Categoria { Id = 1, ComercioId = 1, Nombre = "Cat1" });
                db.AtributosCategoria.Add(new AtributoCategoria { Id = 10, CategoriaId = 1, NombreAtributo = "Attr1", TipoDato = "Texto" });
                db.AtributosCategoria.Add(new AtributoCategoria { Id = 11, CategoriaId = 1, NombreAtributo = "Attr2", TipoDato = "Texto" });
                db.SaveChanges();

                // Verificar todas las restricciones
                var canAddUser = guard.CanAddUserAsync(comercioId: 1).GetAwaiter().GetResult();
                var canAddSucursal = guard.CanAddSucursalAsync(comercioId: 1).GetAwaiter().GetResult();
                var canAddAttr = guard.CanAddAtributoCategoriaAsync(comercioId: 1, categoriaId: 1).GetAwaiter().GetResult();
                var canReport = guard.CanGenerateReportAsync(comercioId: 1).GetAwaiter().GetResult();
                var canSri = guard.CanEmitirFacturaElectronicaAsync(comercioId: 1).GetAwaiter().GetResult();
                var canGerente = guard.CanUseRoleAsync(comercioId: 1, role: "Gerente").GetAwaiter().GetResult();
                var canCajero = guard.CanUseRoleAsync(comercioId: 1, role: "Cajero").GetAwaiter().GetResult();
                var canDueno = guard.CanUseRoleAsync(comercioId: 1, role: "Dueño").GetAwaiter().GetResult();
                var canSupervisor = guard.CanUseRoleAsync(comercioId: 1, role: "Supervisor").GetAwaiter().GetResult();

                // Todas las restricciones simultáneas:
                // (a) Límites alcanzados → no se puede agregar más
                var limitesCumplidos = !canAddUser && !canAddSucursal && !canAddAttr;
                // (b) Reportes bloqueados
                var reportesBloqueados = !canReport;
                // (c) SRI bloqueado
                var sriBloqueado = !canSri;
                // (d) Solo Gerente y Cajero permitidos
                var rolesCorrectos = canGerente && canCajero && !canDueno && !canSupervisor;

                return (limitesCumplidos && reportesBloqueados && sriBloqueado && rolesCorrectos).ToProperty()
                    .Label($"limites={limitesCumplidos}, reportes={reportesBloqueados}, sri={sriBloqueado}, roles={rolesCorrectos}");
            }
        });
    }

    #endregion
}
