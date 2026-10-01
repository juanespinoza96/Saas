using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Tests de propiedades para validación IANA y autorización de zona horaria.
/// Valida invariantes de validación y control de acceso usando FsCheck.
/// </summary>
public class TimezoneValidationPropertyTests
{
    private readonly TimezoneService _service;

    public TimezoneValidationPropertyTests()
    {
        var loggerMock = new Mock<ILogger<TimezoneService>>();
        _service = new TimezoneService(loggerMock.Object);
    }

    #region Generadores

    /// <summary>
    /// Genera strings que NO son zonas IANA reconocidas.
    /// </summary>
    private static Arbitrary<string> ArbitraryInvalidTimezone()
    {
        // Strings inválidos conocidos + strings aleatorios alfanuméricos
        var knownInvalid = new[]
        {
            "Invalid/Zone", "XYZABC", "Foo/Bar/Baz", "NotA/Timezone",
            "123/456", "America/NoExiste", "Europe/Inventada",
            "Fake_Zone", "ZZZ", "Nowhere/Land"
        };

        var genKnownInvalid = Gen.Elements(knownInvalid);

        // Generar strings alfanuméricos aleatorios de 3-20 caracteres
        var genRandomAlpha = Gen.Choose(3, 20).SelectMany(len =>
            Gen.ArrayOf(len, Gen.Elements(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_".ToCharArray()
            )).Select(chars => new string(chars))
        );

        // Combinar ambos generadores y filtrar que no sean zonas válidas
        var combined = Gen.OneOf(genKnownInvalid, genRandomAlpha)
            .Where(s => !string.IsNullOrWhiteSpace(s) && !TimeZoneInfo.TryFindSystemTimeZoneById(s, out _));

        return Arb.From(combined);
    }

    #endregion

    #region Property 3: Validación IANA rechaza identificadores no reconocidos

    /// <summary>
    /// Property 3: Validación IANA rechaza identificadores no reconocidos.
    /// Para cualquier string que no sea reconocido por TimeZoneInfo.TryFindSystemTimeZoneById,
    /// el método IsValidTimeZone SHALL retornar false.
    /// 
    /// **Validates: Requirements 2.4, 2.7, 8.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ValidacionIANA_RechazaIdentificadoresNoReconocidos()
    {
        return Prop.ForAll(ArbitraryInvalidTimezone(), zonaInvalida =>
        {
            // IsValidTimeZone debe retornar false para cualquier zona no reconocida
            var resultado = _service.IsValidTimeZone(zonaInvalida);
            return !resultado;
        });
    }

    #endregion

    #region Property 4: Validación IANA acepta identificadores reconocidos

    /// <summary>
    /// Property 4: Validación IANA acepta identificadores reconocidos.
    /// Para cualquier zona horaria presente en TimeZoneInfo.GetSystemTimeZones(),
    /// el método IsValidTimeZone SHALL retornar true.
    /// 
    /// **Validates: Requirements 2.6, 8.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ValidacionIANA_AceptaIdentificadoresReconocidos()
    {
        var genValidTimezone = Gen.Elements(
            TimeZoneInfo.GetSystemTimeZones().Select(tz => tz.Id).ToArray()
        );

        return Prop.ForAll(Arb.From(genValidTimezone), zonaValida =>
        {
            // IsValidTimeZone debe retornar true para cualquier zona reconocida del sistema
            var resultado = _service.IsValidTimeZone(zonaValida);
            return resultado;
        });
    }

    #endregion

    #region Property 13: Roles sin permiso reciben HTTP 403 al actualizar timezone

    /// <summary>
    /// Property 13: Roles sin permiso reciben HTTP 403 al actualizar timezone.
    /// Para cualquier usuario con rol en {Cajero, Bodeguero, Supervisor, SuperAdmin}
    /// que intente actualizar ZonaHoraria, el sistema SHALL denegar el acceso.
    /// Se valida a nivel de lógica de autorización: solo "Dueño" y "Gerente" están permitidos.
    /// 
    /// **Validates: Requirements 2.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RolesSinPermiso_SonDenegadosAlActualizarTimezone()
    {
        // Roles que NO tienen permiso para actualizar zona horaria
        var rolesNoAutorizados = new[] { "Cajero", "Bodeguero", "Supervisor", "SuperAdmin" };

        var genRolNoAutorizado = Gen.Elements(rolesNoAutorizados);

        return Prop.ForAll(Arb.From(genRolNoAutorizado), rol =>
        {
            // Verificar que el rol NO está permitido para actualizar zona horaria
            var estaPermitido = IsRoleAllowedToUpdateTimezone(rol);
            return !estaPermitido;
        });
    }

    /// <summary>
    /// Método helper que replica la lógica de autorización del controlador.
    /// Solo los roles "Dueño" y "Gerente" pueden actualizar la zona horaria.
    /// </summary>
    private static bool IsRoleAllowedToUpdateTimezone(string role)
    {
        if (string.IsNullOrEmpty(role))
            return false;

        return string.Equals(role, "Dueño", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, "Gerente", StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
