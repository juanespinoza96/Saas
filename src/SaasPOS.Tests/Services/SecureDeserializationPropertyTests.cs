using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FsCheck;
using FsCheck.Xunit;

namespace SaasPOS.Tests.Services;

// Feature: mejoras-operativas-v2, Property 4: Deserialización segura ignora marcadores polimórficos
/// <summary>
/// Property-based tests para la deserialización segura de JSON.
/// **Validates: Requirements 3.3**
///
/// Property 4: Deserialización segura ignora marcadores polimórficos
/// "For any payload JSON que contenga propiedades $type, $ref u otros marcadores de inferencia
/// de tipos, la deserialización con opciones restrictivas SHALL ignorar dichos marcadores
/// y producir un objeto sin tipos inferidos dinámicamente."
/// </summary>
public class SecureDeserializationPropertyTests
{
    /// <summary>
    /// Opciones restrictivas de System.Text.Json idénticas a las configuradas en Program.cs (Req 3.3).
    /// </summary>
    private static readonly JsonSerializerOptions RestrictiveOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 32
    };

    /// <summary>
    /// Marcadores polimórficos comunes usados en ataques de deserialización insegura.
    /// </summary>
    private static readonly string[] PolymorphicMarkers =
    [
        "$type",
        "$ref",
        "$id",
        "$values",
        "$schema"
    ];

    /// <summary>
    /// DTO de ejemplo que simula un cuerpo de solicitud HTTP típico.
    /// No tiene atributos de polimorfismo declarados.
    /// </summary>
    private class SimpleRequestDto
    {
        public string? Nombre { get; set; }
        public int Cantidad { get; set; }
        public decimal Precio { get; set; }
        public string? Descripcion { get; set; }
    }

    /// <summary>
    /// Generador de nombres de propiedad válidos para los campos del DTO.
    /// </summary>
    private static Gen<string> ValidStringGen()
    {
        var chars = Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j',
            'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't',
            'u', 'v', 'w', 'x', 'y', 'z', ' ', '1', '2', '3');

        return from len in Gen.Choose(1, 20)
               from arr in Gen.ArrayOf(len, chars)
               select new string(arr);
    }

    /// <summary>
    /// Generador de marcadores polimórficos.
    /// </summary>
    private static Gen<string> PolymorphicMarkerGen()
    {
        return Gen.Elements(PolymorphicMarkers);
    }

    /// <summary>
    /// Generador de valores maliciosos para marcadores polimórficos (simulan tipos .NET).
    /// </summary>
    private static Gen<string> MaliciousTypeValueGen()
    {
        return Gen.Elements(
            "System.Diagnostics.Process, System",
            "System.IO.File, System.IO",
            "System.Runtime.Remoting.ObjectRef",
            "System.Windows.Forms.AxHost+State",
            "Microsoft.VisualStudio.Text.Formatting.TextFormattingRunProperties",
            "System.Data.DataSet, System.Data",
            "System.Security.Principal.WindowsIdentity"
        );
    }

    #region Property 4a: Deserialización ignora marcadores polimórficos y deserializa correctamente propiedades legítimas

    /// <summary>
    /// Property 4a: Para cualquier payload JSON que contenga marcadores polimórficos
    /// ($type, $ref, $id, $values, $schema) junto con datos legítimos,
    /// la deserialización con opciones restrictivas produce un objeto con las propiedades
    /// legítimas intactas, ignorando los marcadores polimórficos.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DeserializacionIgnoraMarcadoresPolimorficos_PropiedadesLegitimasIntactas()
    {
        var gen = from nombre in ValidStringGen()
                  from cantidad in Gen.Choose(1, 10000)
                  from precioInt in Gen.Choose(1, 100000)
                  from marker in PolymorphicMarkerGen()
                  from maliciousValue in MaliciousTypeValueGen()
                  let precio = (decimal)precioInt / 100m
                  select (nombre, cantidad, precio, marker, maliciousValue);

        return Prop.ForAll(gen.ToArbitrary(), input =>
        {
            // Construir JSON con marcador polimórfico inyectado
            var json = $$"""
                {
                    "{{input.marker}}": "{{input.maliciousValue}}",
                    "nombre": "{{EscapeJson(input.nombre)}}",
                    "cantidad": {{input.cantidad}},
                    "precio": {{input.precio.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
                }
                """;

            // Deserializar con opciones restrictivas
            var result = JsonSerializer.Deserialize<SimpleRequestDto>(json, RestrictiveOptions);

            // El objeto debe existir y tener las propiedades legítimas correctas
            var objectCreated = result != null;
            var nombreCorrecta = result?.Nombre == input.nombre;
            var cantidadCorrecta = result?.Cantidad == input.cantidad;
            var precioCorrector = result?.Precio == input.precio;

            return (objectCreated && nombreCorrecta && cantidadCorrecta && precioCorrector)
                .ToProperty()
                .Label($"Marcador '{input.marker}' con valor '{input.maliciousValue}' " +
                       $"debería ser ignorado. Resultado: nombre={result?.Nombre}, " +
                       $"cantidad={result?.Cantidad}, precio={result?.Precio}");
        });
    }

    #endregion

    #region Property 4b: Marcadores polimórficos NO producen instanciación de tipos dinámicos

    /// <summary>
    /// Property 4b: Para cualquier payload JSON que contenga marcadores polimórficos,
    /// el tipo del objeto deserializado SIEMPRE es el tipo destino solicitado (SimpleRequestDto),
    /// nunca un tipo inferido dinámicamente a partir de los marcadores.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DeserializacionNuncaInstanciaTiposDinamicos()
    {
        var gen = from marker in PolymorphicMarkerGen()
                  from maliciousValue in MaliciousTypeValueGen()
                  from cantidad in Gen.Choose(1, 999)
                  select (marker, maliciousValue, cantidad);

        return Prop.ForAll(gen.ToArbitrary(), input =>
        {
            var json = $$"""
                {
                    "{{input.marker}}": "{{input.maliciousValue}}",
                    "cantidad": {{input.cantidad}}
                }
                """;

            var result = JsonSerializer.Deserialize<SimpleRequestDto>(json, RestrictiveOptions);

            // El tipo SIEMPRE debe ser SimpleRequestDto, nunca un tipo inferido
            var tipoEsSimpleDto = result != null && result.GetType() == typeof(SimpleRequestDto);
            // La cantidad debe estar correctamente deserializada
            var cantidadCorrecta = result?.Cantidad == input.cantidad;

            return (tipoEsSimpleDto && cantidadCorrecta)
                .ToProperty()
                .Label($"Marcador '{input.marker}' con valor '{input.maliciousValue}' " +
                       $"resultó en tipo {result?.GetType().FullName ?? "null"} " +
                       $"(esperado: SimpleRequestDto)");
        });
    }

    #endregion

    #region Property 4c: Múltiples marcadores polimórficos simultáneos son todos ignorados

    /// <summary>
    /// Property 4c: Para cualquier payload JSON que contenga MÚLTIPLES marcadores polimórficos
    /// simultáneamente, todos son ignorados y solo las propiedades legítimas son deserializadas.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MultiplesMarkadoresPolimorficos_TodosIgnorados()
    {
        var gen = from nombre in ValidStringGen()
                  from cantidad in Gen.Choose(1, 5000)
                  from precioInt in Gen.Choose(100, 50000)
                  from numMarkers in Gen.Choose(2, 5)
                  from markers in Gen.ArrayOf(numMarkers, PolymorphicMarkerGen())
                  from values in Gen.ArrayOf(numMarkers, MaliciousTypeValueGen())
                  let precio = (decimal)precioInt / 100m
                  // Asegurar marcadores únicos para JSON válido
                  let uniqueMarkers = markers.Distinct().Zip(values).ToArray()
                  where uniqueMarkers.Length >= 2
                  select (nombre, cantidad, precio, uniqueMarkers);

        return Prop.ForAll(gen.ToArbitrary(), input =>
        {
            // Construir JSON con múltiples marcadores polimórficos
            var markerLines = string.Join(",\n    ",
                input.uniqueMarkers.Select(m => $"\"{m.First}\": \"{EscapeJson(m.Second)}\""));

            var json = $$"""
                {
                    {{markerLines}},
                    "nombre": "{{EscapeJson(input.nombre)}}",
                    "cantidad": {{input.cantidad}},
                    "precio": {{input.precio.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
                }
                """;

            var result = JsonSerializer.Deserialize<SimpleRequestDto>(json, RestrictiveOptions);

            var objectCreated = result != null;
            var tipoEsCorrecto = result?.GetType() == typeof(SimpleRequestDto);
            var nombreCorrecta = result?.Nombre == input.nombre;
            var cantidadCorrecta = result?.Cantidad == input.cantidad;
            var precioCorrector = result?.Precio == input.precio;

            return (objectCreated && tipoEsCorrecto && nombreCorrecta && cantidadCorrecta && precioCorrector)
                .ToProperty()
                .Label($"Con {input.uniqueMarkers.Length} marcadores polimórficos simultáneos, " +
                       $"la deserialización debería ignorarlos todos. " +
                       $"Resultado: tipo={result?.GetType().Name}, nombre={result?.Nombre}");
        });
    }

    #endregion

    #region Property 4d: Deserialización a Dictionary tampoco infiere tipos de marcadores

    /// <summary>
    /// Property 4d: Incluso al deserializar a Dictionary (escenario más permisivo),
    /// los marcadores polimórficos son tratados como propiedades regulares (strings),
    /// nunca como instrucciones de tipo.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DeserializacionADictionary_MarcadoresSonPropiedadesRegulares()
    {
        var gen = from marker in PolymorphicMarkerGen()
                  from maliciousValue in MaliciousTypeValueGen()
                  from dataKey in Gen.Elements("nombre", "valor", "dato", "campo")
                  from dataValue in ValidStringGen()
                  select (marker, maliciousValue, dataKey, dataValue);

        return Prop.ForAll(gen.ToArbitrary(), input =>
        {
            var json = $$"""
                {
                    "{{input.marker}}": "{{input.maliciousValue}}",
                    "{{input.dataKey}}": "{{EscapeJson(input.dataValue)}}"
                }
                """;

            var result = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, RestrictiveOptions);

            // El marcador debe existir como propiedad regular en el diccionario
            var marcadorEsPropiedad = result != null && result.ContainsKey(input.marker);
            // El valor del marcador es un JsonElement (no un tipo instanciado)
            var valorEsTexto = result != null && result.ContainsKey(input.marker)
                && result[input.marker] is System.Text.Json.JsonElement elem
                && elem.ValueKind == JsonValueKind.String;
            // Los datos legítimos también están presentes
            var datosPresentes = result != null && result.ContainsKey(input.dataKey);

            return (marcadorEsPropiedad && valorEsTexto && datosPresentes)
                .ToProperty()
                .Label($"Marcador '{input.marker}' debería ser una propiedad regular (string) " +
                       $"en el diccionario, no una instrucción de tipo. " +
                       $"Presente={marcadorEsPropiedad}, EsTexto={valorEsTexto}");
        });
    }

    #endregion

    /// <summary>
    /// Escapa caracteres especiales de JSON en un string para evitar JSON inválido.
    /// </summary>
    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
