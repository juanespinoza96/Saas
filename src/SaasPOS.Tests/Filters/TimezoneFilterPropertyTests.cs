using FsCheck;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Api.Filters;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Tests.Filters;

/// <summary>
/// Tests de propiedades para TimezoneResponseFilter.
/// Valida que el filtro convierte correctamente campos DateTime en respuestas
/// y que omite la conversión cuando la zona es UTC.
/// </summary>
public class TimezoneFilterPropertyTests
{
    #region DTOs de prueba

    /// <summary>
    /// DTO plano con múltiples campos DateTime para probar conversión.
    /// </summary>
    private class FlatDto
    {
        public DateTime FechaVenta { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int Cantidad { get; set; }
    }

    /// <summary>
    /// DTO con objeto anidado que contiene campos DateTime.
    /// </summary>
    private class NestedDto
    {
        public DateTime FechaPrincipal { get; set; }
        public DetalleDto? Detalle { get; set; }
    }

    /// <summary>
    /// DTO interno para pruebas de objetos anidados.
    /// </summary>
    private class DetalleDto
    {
        public DateTime FechaDetalle { get; set; }
        public DateTime? FechaOpcional { get; set; }
    }

    /// <summary>
    /// DTO con colección de objetos que contienen DateTime.
    /// </summary>
    private class CollectionDto
    {
        public DateTime FechaRegistro { get; set; }
        public List<ItemDto> Items { get; set; } = new();
    }

    /// <summary>
    /// DTO de item para pruebas de colecciones.
    /// </summary>
    private class ItemDto
    {
        public DateTime FechaItem { get; set; }
        public DateTime? FechaAnulacion { get; set; }
    }

    #endregion

    #region Generadores

    /// <summary>
    /// Genera DateTimes UTC aleatorios en el rango 2000-2100.
    /// </summary>
    private static Gen<DateTime> GenUtcDateTime()
    {
        return Gen.Choose(2000, 2100).SelectMany(year =>
            Gen.Choose(1, 12).SelectMany(month =>
            Gen.Choose(1, DateTime.DaysInMonth(year, month)).SelectMany(day =>
            Gen.Choose(0, 23).SelectMany(hour =>
            Gen.Choose(0, 59).SelectMany(minute =>
            Gen.Choose(0, 59).Select(second =>
                new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc)
            ))))));
    }

    /// <summary>
    /// Genera un ID de zona horaria IANA válido que NO sea UTC.
    /// </summary>
    private static Gen<string> GenValidTimezoneNotUtc()
    {
        var zones = TimeZoneInfo.GetSystemTimeZones()
            .Select(tz => tz.Id)
            .Where(id => !string.Equals(id, "UTC", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Gen.Elements(zones);
    }

    /// <summary>
    /// Genera un FlatDto con campos DateTime UTC aleatorios.
    /// </summary>
    private static Gen<FlatDto> GenFlatDto()
    {
        return GenUtcDateTime().SelectMany(fecha1 =>
            GenUtcDateTime().SelectMany(fecha2 =>
            GenUtcDateTime().Select(fecha3 =>
                new FlatDto
                {
                    FechaVenta = fecha1,
                    FechaCreacion = fecha2,
                    FechaModificacion = fecha3,
                    Nombre = "Test",
                    Cantidad = 1
                })));
    }

    /// <summary>
    /// Genera un NestedDto con campos DateTime UTC aleatorios.
    /// </summary>
    private static Gen<NestedDto> GenNestedDto()
    {
        return GenUtcDateTime().SelectMany(fechaPrincipal =>
            GenUtcDateTime().SelectMany(fechaDetalle =>
            GenUtcDateTime().Select(fechaOpcional =>
                new NestedDto
                {
                    FechaPrincipal = fechaPrincipal,
                    Detalle = new DetalleDto
                    {
                        FechaDetalle = fechaDetalle,
                        FechaOpcional = fechaOpcional
                    }
                })));
    }

    /// <summary>
    /// Genera un CollectionDto con una lista de items con DateTimes.
    /// </summary>
    private static Gen<CollectionDto> GenCollectionDto()
    {
        var genItem = GenUtcDateTime().SelectMany(fechaItem =>
            GenUtcDateTime().Select(fechaAnulacion =>
                new ItemDto
                {
                    FechaItem = fechaItem,
                    FechaAnulacion = fechaAnulacion
                }));

        return GenUtcDateTime().SelectMany(fechaRegistro =>
            Gen.ListOf(3, genItem).Select(items =>
                new CollectionDto
                {
                    FechaRegistro = fechaRegistro,
                    Items = items.ToList()
                }));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Crea un ResultExecutingContext simulado con el objeto de respuesta y timezone dados.
    /// </summary>
    private static ResultExecutingContext CreateContext(object responseObject, string timezone)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items["ResolvedTimezone"] = timezone;

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());

        var objectResult = new ObjectResult(responseObject);

        return new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            objectResult,
            controller: null!);
    }

    /// <summary>
    /// Ejecuta el filtro con el contexto proporcionado.
    /// </summary>
    private static async Task ExecuteFilter(
        TimezoneResponseFilter filter,
        ResultExecutingContext context)
    {
        // Crear un delegate que simula la ejecución del resultado
        ResultExecutionDelegate next = () => Task.FromResult(
            new ResultExecutedContext(
                new ActionContext(
                    context.HttpContext,
                    context.HttpContext.GetRouteData(),
                    context.ActionDescriptor,
                    context.ModelState),
                context.Filters.ToList(),
                context.Result,
                controller: null!));

        await filter.OnResultExecutionAsync(context, next);
    }

    /// <summary>
    /// Configura el mock de ITimezoneService para sumar 5 horas a cada DateTime (simula conversión).
    /// </summary>
    private static Mock<ITimezoneService> CreateConvertingMock()
    {
        var mock = new Mock<ITimezoneService>();
        mock.Setup(s => s.ConvertFromUtc(It.IsAny<DateTime>(), It.IsAny<string>()))
            .Returns((DateTime dt, string _) => dt.AddHours(5));
        return mock;
    }

    /// <summary>
    /// Configura el mock de ITimezoneService que no debería ser llamado.
    /// </summary>
    private static Mock<ITimezoneService> CreateNonCalledMock()
    {
        var mock = new Mock<ITimezoneService>();
        mock.Setup(s => s.ConvertFromUtc(It.IsAny<DateTime>(), It.IsAny<string>()))
            .Returns((DateTime dt, string _) => dt.AddHours(5));
        return mock;
    }

    #endregion

    #region Property 7: Filtro convierte todos los campos DateTime

    /// <summary>
    /// Property 7: Filtro de respuesta convierte todos los campos DateTime.
    /// Para cualquier objeto de respuesta con campos DateTime y cualquier zona válida != UTC,
    /// el filtro convierte TODOS los campos DateTime usando ConvertFromUtc.
    /// Verifica objetos planos con DateTime y DateTime?.
    /// 
    /// **Validates: Requirements 3.2, 3.3, 3.4, 3.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Filtro_Convierte_Todos_Campos_DateTime_ObjetoPlano()
    {
        var gen = GenFlatDto().SelectMany(dto =>
            GenValidTimezoneNotUtc().Select(zone => (dto, zone)));

        return Prop.ForAll(Arb.From(gen), tuple =>
        {
            var (dto, timezone) = tuple;

            // Guardar valores originales
            var originalFechaVenta = dto.FechaVenta;
            var originalFechaCreacion = dto.FechaCreacion;
            var originalFechaModificacion = dto.FechaModificacion;

            // Configurar mock que suma 5 horas para cada conversión
            var timezoneMock = CreateConvertingMock();
            var loggerMock = new Mock<ILogger<TimezoneResponseFilter>>();
            var filter = new TimezoneResponseFilter(timezoneMock.Object, loggerMock.Object);

            // Ejecutar el filtro
            var context = CreateContext(dto, timezone);
            ExecuteFilter(filter, context).GetAwaiter().GetResult();

            // Verificar que TODOS los campos DateTime fueron convertidos (sumaron 5 horas)
            var fechaVentaConvertida = dto.FechaVenta == originalFechaVenta.AddHours(5);
            var fechaCreacionConvertida = dto.FechaCreacion == originalFechaCreacion.AddHours(5);
            var fechaModificacionConvertida = dto.FechaModificacion == originalFechaModificacion!.Value.AddHours(5);

            return fechaVentaConvertida && fechaCreacionConvertida && fechaModificacionConvertida;
        });
    }

    /// <summary>
    /// Property 7 (continuación): Filtro convierte campos DateTime en objetos anidados.
    /// Para cualquier objeto con sub-objetos que contienen DateTime,
    /// el filtro los convierte recursivamente.
    /// 
    /// **Validates: Requirements 3.2, 3.3, 3.4, 3.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Filtro_Convierte_Todos_Campos_DateTime_ObjetoAnidado()
    {
        var gen = GenNestedDto().SelectMany(dto =>
            GenValidTimezoneNotUtc().Select(zone => (dto, zone)));

        return Prop.ForAll(Arb.From(gen), tuple =>
        {
            var (dto, timezone) = tuple;

            // Guardar valores originales
            var originalPrincipal = dto.FechaPrincipal;
            var originalDetalle = dto.Detalle!.FechaDetalle;
            var originalOpcional = dto.Detalle!.FechaOpcional;

            // Configurar mock
            var timezoneMock = CreateConvertingMock();
            var loggerMock = new Mock<ILogger<TimezoneResponseFilter>>();
            var filter = new TimezoneResponseFilter(timezoneMock.Object, loggerMock.Object);

            // Ejecutar el filtro
            var context = CreateContext(dto, timezone);
            ExecuteFilter(filter, context).GetAwaiter().GetResult();

            // Verificar conversión recursiva
            var principalConvertida = dto.FechaPrincipal == originalPrincipal.AddHours(5);
            var detalleConvertida = dto.Detalle!.FechaDetalle == originalDetalle.AddHours(5);
            var opcionalConvertida = dto.Detalle!.FechaOpcional == originalOpcional!.Value.AddHours(5);

            return principalConvertida && detalleConvertida && opcionalConvertida;
        });
    }

    /// <summary>
    /// Property 7 (continuación): Filtro convierte campos DateTime en colecciones.
    /// Para cualquier objeto con listas de sub-objetos que contienen DateTime,
    /// el filtro los convierte para cada elemento de la colección.
    /// 
    /// **Validates: Requirements 3.2, 3.3, 3.4, 3.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Filtro_Convierte_Todos_Campos_DateTime_Coleccion()
    {
        var gen = GenCollectionDto().SelectMany(dto =>
            GenValidTimezoneNotUtc().Select(zone => (dto, zone)));

        return Prop.ForAll(Arb.From(gen), tuple =>
        {
            var (dto, timezone) = tuple;

            // Guardar valores originales
            var originalFechaRegistro = dto.FechaRegistro;
            var originalesItems = dto.Items.Select(i => (i.FechaItem, i.FechaAnulacion)).ToList();

            // Configurar mock
            var timezoneMock = CreateConvertingMock();
            var loggerMock = new Mock<ILogger<TimezoneResponseFilter>>();
            var filter = new TimezoneResponseFilter(timezoneMock.Object, loggerMock.Object);

            // Ejecutar el filtro
            var context = CreateContext(dto, timezone);
            ExecuteFilter(filter, context).GetAwaiter().GetResult();

            // Verificar conversión del campo raíz
            var registroConvertido = dto.FechaRegistro == originalFechaRegistro.AddHours(5);

            // Verificar conversión de cada item en la colección
            var itemsConvertidos = dto.Items
                .Zip(originalesItems, (actual, original) =>
                    actual.FechaItem == original.FechaItem.AddHours(5) &&
                    actual.FechaAnulacion == original.FechaAnulacion!.Value.AddHours(5))
                .All(ok => ok);

            return registroConvertido && itemsConvertidos;
        });
    }

    #endregion

    #region Property 8: Si timezone es UTC, valores DateTime permanecen inalterados

    /// <summary>
    /// Property 8: Si timezone resuelta es UTC, los valores DateTime permanecen inalterados.
    /// Para cualquier objeto de respuesta con campos DateTime y timezone = "UTC",
    /// ConvertFromUtc NUNCA se llama y los valores permanecen exactamente iguales.
    /// 
    /// **Validates: Requirements 3.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Timezone_UTC_No_Modifica_Valores_DateTime_ObjetoPlano()
    {
        var gen = GenFlatDto();

        return Prop.ForAll(Arb.From(gen), dto =>
        {
            // Guardar valores originales
            var originalFechaVenta = dto.FechaVenta;
            var originalFechaCreacion = dto.FechaCreacion;
            var originalFechaModificacion = dto.FechaModificacion;

            // Configurar mock — NO debería ser llamado
            var timezoneMock = CreateNonCalledMock();
            var loggerMock = new Mock<ILogger<TimezoneResponseFilter>>();
            var filter = new TimezoneResponseFilter(timezoneMock.Object, loggerMock.Object);

            // Ejecutar el filtro con timezone "UTC"
            var context = CreateContext(dto, "UTC");
            ExecuteFilter(filter, context).GetAwaiter().GetResult();

            // Verificar que ConvertFromUtc NUNCA fue llamado
            timezoneMock.Verify(
                s => s.ConvertFromUtc(It.IsAny<DateTime>(), It.IsAny<string>()),
                Times.Never());

            // Verificar que los valores no cambiaron
            return dto.FechaVenta == originalFechaVenta &&
                   dto.FechaCreacion == originalFechaCreacion &&
                   dto.FechaModificacion == originalFechaModificacion;
        });
    }

    /// <summary>
    /// Property 8 (continuación): Con timezone UTC, objetos anidados no se modifican.
    /// 
    /// **Validates: Requirements 3.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Timezone_UTC_No_Modifica_Valores_DateTime_ObjetoAnidado()
    {
        var gen = GenNestedDto();

        return Prop.ForAll(Arb.From(gen), dto =>
        {
            // Guardar valores originales
            var originalPrincipal = dto.FechaPrincipal;
            var originalDetalle = dto.Detalle!.FechaDetalle;
            var originalOpcional = dto.Detalle!.FechaOpcional;

            // Configurar mock
            var timezoneMock = CreateNonCalledMock();
            var loggerMock = new Mock<ILogger<TimezoneResponseFilter>>();
            var filter = new TimezoneResponseFilter(timezoneMock.Object, loggerMock.Object);

            // Ejecutar el filtro con timezone "UTC"
            var context = CreateContext(dto, "UTC");
            ExecuteFilter(filter, context).GetAwaiter().GetResult();

            // Verificar que ConvertFromUtc NUNCA fue llamado
            timezoneMock.Verify(
                s => s.ConvertFromUtc(It.IsAny<DateTime>(), It.IsAny<string>()),
                Times.Never());

            // Verificar que los valores no cambiaron
            return dto.FechaPrincipal == originalPrincipal &&
                   dto.Detalle!.FechaDetalle == originalDetalle &&
                   dto.Detalle!.FechaOpcional == originalOpcional;
        });
    }

    /// <summary>
    /// Property 8 (continuación): Con timezone UTC, colecciones no se modifican.
    /// 
    /// **Validates: Requirements 3.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Timezone_UTC_No_Modifica_Valores_DateTime_Coleccion()
    {
        var gen = GenCollectionDto();

        return Prop.ForAll(Arb.From(gen), dto =>
        {
            // Guardar valores originales
            var originalFechaRegistro = dto.FechaRegistro;
            var originalesItems = dto.Items.Select(i => (i.FechaItem, i.FechaAnulacion)).ToList();

            // Configurar mock
            var timezoneMock = CreateNonCalledMock();
            var loggerMock = new Mock<ILogger<TimezoneResponseFilter>>();
            var filter = new TimezoneResponseFilter(timezoneMock.Object, loggerMock.Object);

            // Ejecutar el filtro con timezone "UTC"
            var context = CreateContext(dto, "UTC");
            ExecuteFilter(filter, context).GetAwaiter().GetResult();

            // Verificar que ConvertFromUtc NUNCA fue llamado
            timezoneMock.Verify(
                s => s.ConvertFromUtc(It.IsAny<DateTime>(), It.IsAny<string>()),
                Times.Never());

            // Verificar que los valores no cambiaron
            var registroSinCambio = dto.FechaRegistro == originalFechaRegistro;
            var itemsSinCambio = dto.Items
                .Zip(originalesItems, (actual, original) =>
                    actual.FechaItem == original.FechaItem &&
                    actual.FechaAnulacion == original.FechaAnulacion)
                .All(ok => ok);

            return registroSinCambio && itemsSinCambio;
        });
    }

    #endregion
}
