namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Servicio de cifrado simétrico para la contraseña temporal de recuperación.
/// Encapsula el esquema AES-256-CBC de modo reutilizable y testeable, de forma
/// que la contraseña temporal se pueda almacenar cifrada y volver a descifrarse
/// para su visualización por parte del aprobador (Requirement 4.5).
/// </summary>
public interface IPasswordEncryptionService
{
    /// <summary>
    /// Cifra un texto plano con AES-256-CBC.
    /// Genera un IV aleatorio por operación, lo antepone al texto cifrado
    /// y devuelve el resultado (IV + ciphertext) codificado en Base64.
    /// </summary>
    /// <param name="plainText">Texto plano a cifrar.</param>
    /// <returns>Cadena Base64 que contiene el IV seguido del texto cifrado.</returns>
    string Encrypt(string plainText);

    /// <summary>
    /// Descifra un valor previamente producido por <see cref="Encrypt"/>.
    /// Lanza una excepción cuando los datos son inválidos (Base64 inválido,
    /// longitud insuficiente para contener el IV, relleno incorrecto, etc.).
    /// </summary>
    /// <param name="encryptedBase64">Cadena Base64 con el IV + texto cifrado.</param>
    /// <returns>El texto plano original.</returns>
    string Decrypt(string encryptedBase64);
}
