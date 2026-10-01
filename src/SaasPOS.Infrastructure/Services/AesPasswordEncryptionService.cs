using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Implementación de <see cref="IPasswordEncryptionService"/> basada en AES-256-CBC.
/// Replica el mismo esquema de cifrado que <c>SRIService</c> para mantener consistencia:
/// la clave de 32 bytes se deriva mediante SHA-256 del secreto configurado, se genera un
/// IV aleatorio de 16 bytes por operación, se antepone el IV al texto cifrado y el
/// resultado (IV + ciphertext) se codifica en Base64. Se usa relleno PKCS7 (Requirement 4.5).
/// </summary>
public class AesPasswordEncryptionService : IPasswordEncryptionService
{
    // Nombre del secreto que contiene la clave de cifrado de la contraseña temporal.
    private const string ClaveConfigNombre = "PASSWORD_RECOVERY_ENCRYPTION_KEY";

    // Tamaño del IV para AES-CBC (128 bits = 16 bytes).
    private const int TamanoIvBytes = 16;

    private readonly IConfiguration _configuration;

    public AesPasswordEncryptionService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public string Encrypt(string plainText)
    {
        // Se admite el texto vacío, pero no un valor nulo.
        ArgumentNullException.ThrowIfNull(plainText);

        // Derivar la clave AES-256 (32 bytes) a partir del secreto configurado.
        var keyBytes = ObtenerClaveDerivada();

        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Generar un IV aleatorio distinto en cada operación de cifrado.
        aes.GenerateIV();
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherText = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Concatenar IV + texto cifrado para poder recuperar el IV al descifrar.
        var resultado = new byte[iv.Length + cipherText.Length];
        Buffer.BlockCopy(iv, 0, resultado, 0, iv.Length);
        Buffer.BlockCopy(cipherText, 0, resultado, iv.Length, cipherText.Length);

        return Convert.ToBase64String(resultado);
    }

    /// <inheritdoc />
    public string Decrypt(string encryptedBase64)
    {
        // Un valor nulo o vacío no puede contener el IV: se considera dato inválido.
        if (string.IsNullOrEmpty(encryptedBase64))
            throw new ArgumentException("El valor cifrado no puede ser nulo o vacío.", nameof(encryptedBase64));

        // Decodificar Base64. Convert.FromBase64String lanza FormatException si es inválido.
        var encryptedBytes = Convert.FromBase64String(encryptedBase64);

        // Debe existir al menos el IV completo; de lo contrario los datos son inválidos.
        if (encryptedBytes.Length < TamanoIvBytes)
            throw new ArgumentException("Los datos cifrados son demasiado cortos para contener el IV.", nameof(encryptedBase64));

        // Derivar la misma clave AES-256 (32 bytes) usada al cifrar.
        var keyBytes = ObtenerClaveDerivada();

        // Los primeros 16 bytes son el IV; el resto es el texto cifrado.
        var iv = encryptedBytes[..TamanoIvBytes];
        var cipherText = encryptedBytes[TamanoIvBytes..];

        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Un relleno PKCS7 incorrecto provoca CryptographicException (dato inválido): se propaga.
        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);

        return Encoding.UTF8.GetString(decryptedBytes);
    }

    /// <summary>
    /// Obtiene el secreto de configuración/entorno y deriva una clave de 32 bytes
    /// (AES-256) mediante SHA-256, siguiendo el mismo patrón que <c>SRIService</c>.
    /// Se lee primero de <see cref="IConfiguration"/> con reserva a la variable de entorno.
    /// </summary>
    private byte[] ObtenerClaveDerivada()
    {
        var key = _configuration[ClaveConfigNombre]
            ?? Environment.GetEnvironmentVariable(ClaveConfigNombre);

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                $"No se ha configurado el secreto '{ClaveConfigNombre}' para el cifrado de la contraseña temporal.");

        // La clave AES-256 debe ser de 32 bytes; SHA-256 garantiza esa longitud.
        return SHA256.HashData(Encoding.UTF8.GetBytes(key));
    }
}
