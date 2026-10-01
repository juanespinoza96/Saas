using System.Security.Cryptography;

namespace SaasPOS.Application.Helpers;

/// <summary>
/// Resultado de la validación de una contraseña contra la Politica_Password estricta.
/// Expone si la contraseña es válida y la lista de mensajes de error por cada regla incumplida.
/// </summary>
public sealed class PasswordValidationResult
{
    /// <summary>Indica si la contraseña cumple todas las reglas de la política.</summary>
    public bool IsValid { get; }

    /// <summary>Mensajes descriptivos de las reglas incumplidas (vacío cuando <see cref="IsValid"/> es verdadero).</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Primer mensaje de error, o cadena vacía si la contraseña es válida (conveniencia para la capa API).</summary>
    public string FirstError => Errors.Count > 0 ? Errors[0] : string.Empty;

    private PasswordValidationResult(bool isValid, IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    /// <summary>Crea un resultado exitoso sin errores.</summary>
    public static PasswordValidationResult Success() =>
        new(true, Array.Empty<string>());

    /// <summary>Crea un resultado fallido con los mensajes de error indicados.</summary>
    public static PasswordValidationResult Failure(IReadOnlyList<string> errors) =>
        new(false, errors);
}

/// <summary>
/// Única fuente de verdad de la Politica_Password estricta (Requirement 11).
/// El frontend replica estas reglas para validación en cliente; el backend es la autoridad.
/// Reglas: longitud mínima, al menos una mayúscula, al menos un dígito, al menos un
/// carácter especial de la lista blanca {@, -, _} y NINGÚN carácter fuera de [A-Za-z0-9@-_].
/// </summary>
public static class PasswordPolicy
{
    /// <summary>Longitud mínima exigida a la contraseña.</summary>
    public const int MinLength = 6;

    /// <summary>Conjunto exacto de caracteres especiales permitidos por la lista blanca estricta.</summary>
    public const string SpecialChars = "@-_";

    /// <summary>
    /// Valida una contraseña contra la política estricta y devuelve un resultado con
    /// éxito/fallo y un mensaje descriptivo por cada regla incumplida.
    /// </summary>
    /// <param name="password">La contraseña a validar.</param>
    /// <returns>Un <see cref="PasswordValidationResult"/> con el detalle de la validación.</returns>
    public static PasswordValidationResult Validate(string password)
    {
        var errors = new List<string>();

        // Una contraseña nula o vacía se trata como violación de longitud.
        if (string.IsNullOrEmpty(password))
        {
            errors.Add($"La contraseña debe tener al menos {MinLength} caracteres.");
            return PasswordValidationResult.Failure(errors);
        }

        // Regla 1 (Requirement 11.1): longitud mínima.
        if (password.Length < MinLength)
        {
            errors.Add($"La contraseña debe tener al menos {MinLength} caracteres.");
        }

        var tieneMayuscula = false;
        var tieneDigito = false;
        var tieneEspecialPermitido = false;
        var tieneCaracterNoPermitido = false;

        // Recorrido único de los caracteres para evaluar todas las reglas de composición.
        foreach (var c in password)
        {
            if (c >= 'A' && c <= 'Z')
            {
                tieneMayuscula = true;
            }
            else if (c >= 'a' && c <= 'z')
            {
                // Letra minúscula: permitida por la lista blanca, sin regla propia.
            }
            else if (c >= '0' && c <= '9')
            {
                tieneDigito = true;
            }
            else if (SpecialChars.IndexOf(c) >= 0)
            {
                tieneEspecialPermitido = true;
            }
            else
            {
                // Cualquier otro carácter viola la lista blanca estricta.
                tieneCaracterNoPermitido = true;
            }
        }

        // Regla 2 (Requirement 11.2): al menos una letra mayúscula.
        if (!tieneMayuscula)
        {
            errors.Add("La contraseña debe contener al menos una letra mayúscula.");
        }

        // Regla 3 (Requirement 11.3): al menos un dígito.
        if (!tieneDigito)
        {
            errors.Add("La contraseña debe contener al menos un número.");
        }

        // Regla 4 (Requirement 11.4): al menos un carácter especial de {@, -, _}.
        if (!tieneEspecialPermitido)
        {
            errors.Add("La contraseña debe contener al menos uno de los caracteres especiales permitidos: @, - o _.");
        }

        // Regla 5 (Requirement 11.5): lista blanca estricta, sin caracteres fuera de [A-Za-z0-9@-_].
        if (tieneCaracterNoPermitido)
        {
            errors.Add("La contraseña solo puede contener letras, números y los caracteres especiales @, - o _.");
        }

        // Regla 6 (Requirement 11.6): si no hay errores, la contraseña es aceptada.
        return errors.Count == 0
            ? PasswordValidationResult.Success()
            : PasswordValidationResult.Failure(errors);
    }

    /// <summary>Alfabeto de letras mayúsculas permitido (garantiza la regla de mayúscula).</summary>
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>Alfabeto de letras minúsculas permitido por la lista blanca.</summary>
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";

    /// <summary>Alfabeto de dígitos permitido (garantiza la regla de dígito).</summary>
    private const string Digits = "0123456789";

    /// <summary>Alfabeto completo permitido por la lista blanca estricta [A-Za-z0-9@-_].</summary>
    private const string FullAlphabet = Uppercase + Lowercase + Digits + SpecialChars;

    /// <summary>
    /// Genera una contraseña temporal criptográficamente segura que SIEMPRE cumple la
    /// Politica_Password estricta (Requirements 4.1 y 4.2).
    /// Garantiza al menos una mayúscula, un dígito y un carácter especial de {@, -, _};
    /// el resto de posiciones se rellena desde el alfabeto permitido [A-Za-z0-9@-_] y el
    /// resultado se baraja con un Fisher-Yates criptográfico para que los caracteres
    /// obligatorios no queden en posiciones fijas.
    /// </summary>
    /// <param name="length">Longitud deseada de la contraseña (por defecto 12).</param>
    /// <returns>Una contraseña que satisface <see cref="Validate"/> (IsValid == true).</returns>
    public static string GenerateCompliantTemporary(int length = 12)
    {
        // Se necesitan como mínimo tres caracteres para cubrir las tres reglas obligatorias
        // (mayúscula, dígito y especial). Además nunca se produce por debajo de MinLength,
        // de modo que la salida siempre pase la regla de longitud de la política.
        var longitudEfectiva = Math.Max(length, MinLength);

        var caracteres = new char[longitudEfectiva];

        // 1) Colocar de forma provisional los tres caracteres obligatorios al inicio.
        //    El barajado posterior los reubicará en posiciones aleatorias.
        caracteres[0] = SeleccionarAleatorio(Uppercase);
        caracteres[1] = SeleccionarAleatorio(Digits);
        caracteres[2] = SeleccionarAleatorio(SpecialChars);

        // 2) Rellenar el resto de posiciones desde el alfabeto completo permitido.
        for (var i = 3; i < longitudEfectiva; i++)
        {
            caracteres[i] = SeleccionarAleatorio(FullAlphabet);
        }

        // 3) Barajar con Fisher-Yates usando un generador criptográfico sin sesgo.
        BarajarCriptografico(caracteres);

        return new string(caracteres);
    }

    /// <summary>
    /// Selecciona un carácter aleatorio del alfabeto indicado usando un CSPRNG
    /// (<see cref="RandomNumberGenerator.GetInt32(int)"/>) para evitar sesgo de módulo.
    /// </summary>
    private static char SeleccionarAleatorio(string alfabeto)
    {
        var indice = RandomNumberGenerator.GetInt32(alfabeto.Length);
        return alfabeto[indice];
    }

    /// <summary>
    /// Baraja un arreglo de caracteres in-place con el algoritmo Fisher-Yates,
    /// obteniendo cada índice aleatorio desde el CSPRNG sin sesgo.
    /// </summary>
    private static void BarajarCriptografico(char[] caracteres)
    {
        for (var i = caracteres.Length - 1; i > 0; i--)
        {
            // Índice uniforme en [0, i] mediante RandomNumberGenerator (sin sesgo de módulo).
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (caracteres[i], caracteres[j]) = (caracteres[j], caracteres[i]);
        }
    }
}
