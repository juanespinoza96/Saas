using System.Text;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Feature: recuperacion-password-jerarquica, Property 5:
/// Round-trip de cifrado AES-256 de la contraseña temporal.
///
/// Verifica dos invariantes del <see cref="AesPasswordEncryptionService"/> (Requirement 4.5):
/// 1. Para cualquier texto plano, Decrypt(Encrypt(plainText)) == plainText.
/// 2. Cifrar el mismo texto plano dos veces produce cifrados distintos (IV aleatorio por operación).
///
/// **Validates: Requirements 4.5**
/// </summary>
public class PasswordRecovery_Property5_AesRoundTripTests
{
    // Clave secreta de prueba: el servicio deriva la clave AES-256 vía SHA-256, por lo que
    // cualquier valor no vacío es válido. Se provee mediante IConfiguration en memoria,
    // imitando cómo la aplicación lee PASSWORD_RECOVERY_ENCRYPTION_KEY en producción.
    private const string ClaveNombre = "PASSWORD_RECOVERY_ENCRYPTION_KEY";
    private const string ClaveValor = "clave-de-prueba-para-cifrado-de-contrasena-temporal-32bytes+";

    // Construye el servicio de cifrado con una configuración en memoria que contiene el secreto.
    private static IPasswordEncryptionService CrearServicio()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ClaveNombre] = ClaveValor,
            })
            .Build();

        return new AesPasswordEncryptionService(configuration);
    }

    /// <summary>
    /// Generador de textos planos representativos: cualquier cadena no nula, incluyendo la
    /// cadena vacía y textos con caracteres Unicode. El servicio admite el texto vacío pero no
    /// un valor nulo, por lo que se filtran los nulos que FsCheck pudiera generar.
    /// </summary>
    private static Gen<string> GenTextoPlano() =>
        Arb.Default.String().Generator.Where(s => s != null);

    /// <summary>
    /// Propiedad: el descifrado del cifrado devuelve exactamente el texto plano original.
    ///
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property Decrypt_De_Encrypt_DevuelveElTextoOriginal()
    {
        var servicio = CrearServicio();

        return Prop.ForAll(GenTextoPlano().ToArbitrary(), plainText =>
        {
            var cifrado = servicio.Encrypt(plainText);
            var descifrado = servicio.Decrypt(cifrado);

            return (descifrado == plainText)
                .Label($"Round-trip fallido: original.Length={plainText.Length}, " +
                       $"descifrado.Length={descifrado.Length}");
        });
    }

    /// <summary>
    /// Propiedad: cifrar el mismo texto plano dos veces produce cifrados diferentes,
    /// porque cada operación genera un IV aleatorio. Ambos cifrados, no obstante, deben
    /// descifrarse al mismo texto plano original.
    ///
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property Encrypt_Repetido_ProduceCifradosDistintos_PeroMismoDescifrado()
    {
        var servicio = CrearServicio();

        // Restringimos a textos no vacíos: dos cifrados de la cadena vacía con distinto IV
        // siempre difieren en el prefijo del IV, pero exigir contenido hace la propiedad
        // más significativa respecto a la aleatoriedad del IV sobre el texto cifrado.
        var gen = GenTextoPlano().Where(s => s.Length > 0);

        return Prop.ForAll(gen.ToArbitrary(), plainText =>
        {
            var cifrado1 = servicio.Encrypt(plainText);
            var cifrado2 = servicio.Encrypt(plainText);

            var sonDistintos = !string.Equals(cifrado1, cifrado2, StringComparison.Ordinal);
            var descifranIgual = servicio.Decrypt(cifrado1) == plainText
                                 && servicio.Decrypt(cifrado2) == plainText;

            return (sonDistintos && descifranIgual)
                .Label($"IV aleatorio esperado: distintos={sonDistintos}, descifranIgual={descifranIgual}, " +
                       $"len(plainText)={plainText.Length}");
        });
    }
}
