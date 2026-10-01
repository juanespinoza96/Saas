using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Filters;

/// <summary>
/// Filtro de resultado que convierte campos DateTime en las respuestas de la API
/// a la zona horaria resuelta para el request actual.
/// Agrega el header X-Applied-Timezone indicando qué zona se aplicó.
/// </summary>
public class TimezoneResponseFilter : IAsyncResultFilter
{
    private readonly ITimezoneService _timezoneService;
    private readonly ILogger<TimezoneResponseFilter> _logger;

    /// <summary>
    /// Nombre del header de respuesta que indica la zona horaria aplicada.
    /// </summary>
    private const string AppliedTimezoneHeader = "X-Applied-Timezone";

    /// <summary>
    /// Clave en HttpContext.Items donde el middleware almacena la zona resuelta.
    /// </summary>
    private const string ResolvedTimezoneKey = "ResolvedTimezone";

    /// <summary>
    /// Profundidad máxima de recursión para evitar bucles infinitos.
    /// </summary>
    private const int MaxRecursionDepth = 20;

    public TimezoneResponseFilter(ITimezoneService timezoneService, ILogger<TimezoneResponseFilter> logger)
    {
        _timezoneService = timezoneService;
        _logger = logger;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        // Solo procesar respuestas de tipo ObjectResult (no archivos, redirects, etc.)
        if (context.Result is not ObjectResult objectResult)
        {
            await next();
            return;
        }

        // Leer la zona horaria resuelta del HttpContext
        string timezone = GetResolvedTimezone(context.HttpContext);

        // Si la zona es UTC o no hay valor, omitir conversión
        if (string.IsNullOrEmpty(timezone) || string.Equals(timezone, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            context.HttpContext.Response.Headers[AppliedTimezoneHeader] = "UTC";
            await next();
            return;
        }

        // Intentar convertir los campos DateTime del objeto de respuesta
        try
        {
            if (objectResult.Value != null)
            {
                var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
                ConvertDateTimeProperties(objectResult.Value, timezone, visited, 0);
            }

            context.HttpContext.Response.Headers[AppliedTimezoneHeader] = timezone;
        }
        catch (Exception ex)
        {
            // Fail-safe: si la conversión falla, retornar datos originales con timezone UTC
            _logger.LogError(ex, "Error al convertir campos DateTime a zona horaria '{Timezone}'. Se retorna respuesta sin modificar.", timezone);
            context.HttpContext.Response.Headers[AppliedTimezoneHeader] = "UTC";
        }

        await next();
    }

    /// <summary>
    /// Obtiene la zona horaria resuelta desde HttpContext.Items.
    /// Retorna "UTC" si no se encuentra el valor.
    /// </summary>
    private static string GetResolvedTimezone(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue(ResolvedTimezoneKey, out var value) && value is string tz)
        {
            return tz;
        }

        return "UTC";
    }

    /// <summary>
    /// Recorre recursivamente un objeto y convierte todos los campos DateTime UTC
    /// a la zona horaria especificada.
    /// </summary>
    private void ConvertDateTimeProperties(object obj, string timezone, HashSet<object> visited, int depth)
    {
        // Protección contra recursión excesiva
        if (depth > MaxRecursionDepth)
            return;

        if (obj == null)
            return;

        var type = obj.GetType();

        // Ignorar tipos primitivos, strings, enums y tipos de valor simples
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(Guid) || type == typeof(DateOnly) || type == typeof(TimeOnly))
            return;

        // Evitar referencias circulares para tipos por referencia
        if (!type.IsValueType)
        {
            if (!visited.Add(obj))
                return;
        }

        // Si es una colección (IEnumerable), recorrer cada elemento
        if (obj is IEnumerable enumerable && obj is not string)
        {
            foreach (var item in enumerable)
            {
                if (item != null)
                {
                    ConvertDateTimeProperties(item, timezone, visited, depth + 1);
                }
            }
            return;
        }

        // Recorrer las propiedades públicas del objeto
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            // Solo procesar propiedades que se pueden leer y escribir
            if (!property.CanRead || !property.CanWrite)
                continue;

            // Evitar propiedades indexadas
            if (property.GetIndexParameters().Length > 0)
                continue;

            try
            {
                var propertyType = property.PropertyType;
                var underlyingType = Nullable.GetUnderlyingType(propertyType);

                // Caso: propiedad DateTime
                if (propertyType == typeof(DateTime))
                {
                    var dateValue = (DateTime)property.GetValue(obj)!;
                    var converted = ConvertDateTime(dateValue, timezone);
                    property.SetValue(obj, converted);
                }
                // Caso: propiedad DateTime? (nullable)
                else if (underlyingType == typeof(DateTime))
                {
                    var nullableValue = (DateTime?)property.GetValue(obj);
                    if (nullableValue.HasValue)
                    {
                        var converted = ConvertDateTime(nullableValue.Value, timezone);
                        property.SetValue(obj, converted);
                    }
                }
                // Caso: propiedad compleja — recorrer recursivamente
                else if (!propertyType.IsPrimitive && propertyType != typeof(string) &&
                         propertyType != typeof(decimal) && propertyType != typeof(Guid) &&
                         !propertyType.IsEnum && propertyType != typeof(DateOnly) &&
                         propertyType != typeof(TimeOnly))
                {
                    var value = property.GetValue(obj);
                    if (value != null)
                    {
                        ConvertDateTimeProperties(value, timezone, visited, depth + 1);
                    }
                }
            }
            catch (Exception ex)
            {
                // Si falla una propiedad individual, logear y continuar con las demás
                _logger.LogWarning(ex, "Error al convertir propiedad '{Property}' del tipo '{Type}' a zona '{Timezone}'.",
                    property.Name, type.Name, timezone);
            }
        }
    }

    /// <summary>
    /// Convierte un DateTime individual usando el servicio de timezone.
    /// Solo convierte valores con DateTimeKind.Utc (por diseño, todos los valores almacenados son UTC).
    /// </summary>
    private DateTime ConvertDateTime(DateTime dateValue, string timezone)
    {
        // Solo convertir valores UTC (el sistema almacena todo en UTC)
        // Si el Kind es Unspecified, asumimos que también es UTC por diseño del sistema
        if (dateValue.Kind == DateTimeKind.Local)
            return dateValue;

        return _timezoneService.ConvertFromUtc(dateValue, timezone);
    }
}
