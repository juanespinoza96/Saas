using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.Helpers;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Pruebas basadas en propiedades (FsCheck.Xunit, ≥100 iteraciones) para el helper
/// <see cref="PasswordPolicy"/> del flujo de recuperación de contraseña jerárquica.
///
/// Cubre dos propiedades del diseño:
///   - Property 3: la contraseña temporal generada siempre cumple la política.
///   - Property 13: aceptación de la política con lista blanca estricta (aceptar sii cumple todas las reglas).
///
/// El backend es la autoridad de la Politica_Password: longitud ≥ 6, al menos una
/// mayúscula, al menos un dígito, al menos uno de {@, -, _} y ningún carácter fuera
/// de [A-Za-z0-9@-_].
/// </summary>
public class PasswordPolicy_Property3_13_Tests
{
    // Conjunto exacto de caracteres especiales permitidos por la lista blanca estricta.
    private const string Especiales = "@-_";

    /// <summary>
    /// Verifica de forma independiente (sin reutilizar la implementación bajo prueba)
    /// que una contraseña cumple todas las reglas de la Politica_Password estricta.
    /// Se usa como oráculo para la Property 13.
    /// </summary>
    private static bool CumplePoliticaOraculo(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        // Regla de longitud mínima.
        if (password.Length < PasswordPolicy.MinLength)
        {
            return false;
        }

        var tieneMayuscula = false;
        var tieneDigito = false;
        var tieneEspecialPermitido = false;

        foreach (var c in password)
        {
            var esMayuscula = c >= 'A' && c <= 'Z';
            var esMinuscula = c >= 'a' && c <= 'z';
            var esDigito = c >= '0' && c <= '9';
            var esEspecialPermitido = Especiales.IndexOf(c) >= 0;

            if (esMayuscula)
            {
                tieneMayuscula = true;
            }
            else if (esDigito)
            {
                tieneDigito = true;
            }
            else if (esEspecialPermitido)
            {
                tieneEspecialPermitido = true;
            }
            else if (!esMinuscula)
            {
                // Cualquier carácter que no sea [A-Za-z0-9@-_] viola la lista blanca estricta.
                return false;
            }
        }

        return tieneMayuscula && tieneDigito && tieneEspecialPermitido;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 3: La contraseña temporal generada siempre cumple la política.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Feature: recuperacion-password-jerarquica, Property 3: la contraseña temporal generada
    /// siempre cumple la política (longitud exacta 12, ≥1 mayúscula, ≥1 dígito, ≥1 especial de
    /// {@,-,_}, y ningún carácter fuera de [A-Za-z0-9@-_]).
    ///
    /// Para toda invocación del generador por defecto (12 caracteres), la salida tiene
    /// exactamente 12 caracteres, satisface <see cref="PasswordPolicy.Validate"/> y es
    /// aceptada por el oráculo independiente.
    ///
    /// **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property TemporalGenerada_SiempreCumpleLaPolitica()
    {
        // El parámetro semilla solo fuerza a FsCheck a repetir la propiedad muchas veces;
        // el generador interno usa un CSPRNG, así que cada iteración produce una temporal nueva.
        return Prop.ForAll(Arb.From<int>(), _ =>
        {
            var temporal = PasswordPolicy.GenerateCompliantTemporary();

            var longitudCorrecta = temporal.Length == 12;
            var validaSegunPolicy = PasswordPolicy.Validate(temporal).IsValid;
            var validaSegunOraculo = CumplePoliticaOraculo(temporal);

            return (longitudCorrecta && validaSegunPolicy && validaSegunOraculo)
                .Label($"Temporal generada '{temporal}' (len={temporal.Length}) debe medir 12 y cumplir la política.");
        });
    }

    /// <summary>
    /// Feature: recuperacion-password-jerarquica, Property 3: la contraseña temporal generada
    /// siempre cumple la política para longitudes solicitadas arbitrarias.
    ///
    /// Para toda longitud solicitada, la salida nunca baja de <see cref="PasswordPolicy.MinLength"/>
    /// y siempre satisface la política (así el generador es seguro aunque se pida una longitud pequeña).
    ///
    /// **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property TemporalGenerada_CualquierLongitud_CumpleLaPolitica()
    {
        // Longitudes entre 1 y 64: incluye valores por debajo del mínimo (para verificar el piso)
        // y valores holgados por encima.
        var genLongitud = Gen.Choose(1, 64);

        return Prop.ForAll(genLongitud.ToArbitrary(), longitud =>
        {
            var temporal = PasswordPolicy.GenerateCompliantTemporary(longitud);

            var respetaMinimo = temporal.Length >= PasswordPolicy.MinLength;
            var respetaSolicitada = temporal.Length >= longitud || longitud < PasswordPolicy.MinLength;
            var valida = PasswordPolicy.Validate(temporal).IsValid && CumplePoliticaOraculo(temporal);

            return (respetaMinimo && respetaSolicitada && valida)
                .Label($"len solicitada={longitud}, generada='{temporal}' (len={temporal.Length}) debe cumplir la política y el mínimo.");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 13: PasswordPolicy.Validate acepta sii cumple todas las reglas.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Genera cadenas arbitrarias de longitud variable a partir de un alfabeto amplio que
    /// mezcla mayúsculas, minúsculas, dígitos, los especiales permitidos {@,-,_} y varios
    /// caracteres NO permitidos (incluyendo símbolos ASCII y Unicode) y espacios.
    /// Así el espacio de entrada cubre tanto contraseñas válidas como los cinco tipos de
    /// violación de la política.
    /// </summary>
    private static Gen<string> GenCadenaMixta()
    {
        // Alfabeto sesgado hacia caracteres relevantes de la política, incluyendo violaciones.
        var alfabeto = Gen.Frequency(
            Tuple.Create(6, Gen.Elements("ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray())),
            Tuple.Create(6, Gen.Elements("abcdefghijklmnopqrstuvwxyz".ToCharArray())),
            Tuple.Create(5, Gen.Elements("0123456789".ToCharArray())),
            Tuple.Create(4, Gen.Elements('@', '-', '_')),
            Tuple.Create(4, Gen.Elements('!', '#', '$', '%', '&', '*', '.', '/', '+', '=', ' ', 'ñ', 'á', 'Ü', '€', '\t'))
        );

        // Longitudes de 0 a 20 para incluir cadenas vacías y cortas (violación de longitud).
        return Gen.Choose(0, 20)
            .SelectMany(n => Gen.ArrayOf(n, alfabeto))
            .Select(chars => new string(chars));
    }

    /// <summary>
    /// Feature: recuperacion-password-jerarquica, Property 13: PasswordPolicy.Validate acepta
    /// una cadena sii cumple longitud ≥ 6, ≥1 mayúscula, ≥1 dígito, ≥1 especial de {@,-,_} y
    /// lista blanca estricta [A-Za-z0-9@-_]. Bicondicional contra un oráculo independiente.
    ///
    /// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 17.4, 17.5**
    /// </summary>
    [Property(MaxTest = 500)]
    public Property Validate_AceptaSiiCumpleTodasLasReglas()
    {
        return Prop.ForAll(GenCadenaMixta().ToArbitrary(), password =>
        {
            var aceptadaPorPolicy = PasswordPolicy.Validate(password).IsValid;
            var aceptadaPorOraculo = CumplePoliticaOraculo(password);

            // Bicondicional: Validate debe coincidir exactamente con el oráculo.
            return (aceptadaPorPolicy == aceptadaPorOraculo)
                .Label($"Validate('{password}')={aceptadaPorPolicy} debe coincidir con el oráculo={aceptadaPorOraculo}.");
        });
    }

    /// <summary>
    /// Feature: recuperacion-password-jerarquica, Property 13: cada regla incumplida provoca
    /// rechazo. Construye una base válida y le aplica exactamente una mutación que rompe una
    /// sola regla; Validate debe rechazar en todos los casos.
    ///
    /// Casos: demasiado corta, sin mayúscula, sin dígito, sin especial permitido y con un
    /// carácter especial no permitido.
    ///
    /// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 17.4, 17.5**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property Validate_RechazaCadaViolacionDeRegla()
    {
        // 0=corta, 1=sin mayúscula, 2=sin dígito, 3=sin especial permitido, 4=carácter no permitido.
        var genViolacion = Gen.Choose(0, 4);

        return Prop.ForAll(genViolacion.ToArbitrary(), tipoViolacion =>
        {
            // Base válida conocida (cumple todas las reglas): mayúscula, minúsculas, dígito y especial.
            // "Abc1@x" tiene longitud 6, 'A' mayúscula, '1' dígito, '@' especial permitido.
            string entrada = tipoViolacion switch
            {
                0 => "Ab1@",          // Longitud 4 (< 6): viola la regla de longitud.
                1 => "abc1@x",        // Sin mayúscula.
                2 => "Abcd@x",        // Sin dígito.
                3 => "Abcd1x",        // Sin especial permitido.
                4 => "Abc1@x!",       // Contiene '!' fuera de la lista blanca.
                _ => "Abc1@x"
            };

            var resultado = PasswordPolicy.Validate(entrada);

            // Debe rechazar y aportar al menos un mensaje de error descriptivo.
            return (!resultado.IsValid && resultado.Errors.Count > 0)
                .Label($"Entrada '{entrada}' (violación tipo {tipoViolacion}) debía ser rechazada con mensaje de error.");
        });
    }
}
