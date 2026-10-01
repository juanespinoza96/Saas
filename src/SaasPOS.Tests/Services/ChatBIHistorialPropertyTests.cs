using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

// Feature: mejoras-operativas-v2, Property 11: Historial de conversación preserva orden y completitud
/// <summary>
/// Property-based tests para verificar que el historial de conversación preserva orden y completitud.
/// **Validates: Requirements 7.7**
///
/// Property 11: Historial de conversación preserva orden y completitud
/// "For any secuencia de mensajes [m1, m2, ..., mn] enviados al Chat IA, el historial SHALL
/// contener todos los mensajes en el mismo orden, con cada mensaje manteniendo su rol y contenido intactos."
/// </summary>
public class ChatBIHistorialPropertyTests
{
    /// <summary>
    /// Roles válidos para mensajes de historial de conversación.
    /// </summary>
    private static readonly string[] RolesValidos = { "system", "user", "assistant" };

    /// <summary>
    /// Roles válidos del sistema POS para el parámetro rol de ProcesarConsultaAsync.
    /// </summary>
    private static readonly string[] RolesPOS = { "Dueño", "Gerente", "Cajero", "Supervisor", "Bodeguero" };

    /// <summary>
    /// Generador de contenido de mensaje no vacío (cadenas alfanuméricas).
    /// </summary>
    private static Gen<string> ContenidoGen =>
        Gen.Elements(
            "¿Cuáles fueron mis ventas?",
            "Tus ventas del mes fueron $5000",
            "¿Y comparado con el mes pasado?",
            "El mes pasado vendiste $4500, un incremento del 11%",
            "¿Qué producto se vende más?",
            "El producto más vendido es Coca Cola con 150 unidades",
            "Muéstrame un gráfico de ventas por día",
            "Aquí tienes el gráfico de ventas diarias",
            "¿Cuántas sucursales tengo?",
            "Tienes 3 sucursales registradas",
            "Dame el ticket promedio",
            "Tu ticket promedio es $12.50",
            "Pregunta de prueba con caracteres especiales: áéíóú ñ",
            "Respuesta con números: 1234567890",
            "Texto largo para verificar que se preserva completamente sin truncamiento alguno"
        );

    /// <summary>
    /// Generador de un ChatMessage arbitrario con rol válido y contenido no vacío.
    /// </summary>
    private static Gen<ChatMessage> ChatMessageGen =>
        from role in Gen.Elements(RolesValidos)
        from content in ContenidoGen
        select new ChatMessage { Role = role, Content = content };

    /// <summary>
    /// Generador de una lista de mensajes de historial (entre 0 y 20 mensajes).
    /// </summary>
    private static Gen<List<ChatMessage>> HistorialGen =>
        Gen.ListOf(ChatMessageGen).Select(msgs => msgs.ToList());

    /// <summary>
    /// Crea un contexto InMemory con ITenantContext mock y un servicio configurado
    /// que captura los mensajes enviados al IAIService.
    /// </summary>
    private static (ChatBusinessIntelligenceService service, Mock<IAIService> aiMock, AppDbContext db)
        CrearServicioConCaptura()
    {
        // Configurar ITenantContext como SuperAdmin para evitar query filters
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);

        // Crear un comercio mínimo para que las queries de estadísticas no fallen
        db.Database.EnsureCreated();

        var aiMock = new Mock<IAIService>();
        // Configurar el mock para devolver una respuesta exitosa simple
        aiMock.Setup(s => s.ChatCompletionAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AICompletionResult.Ok("Respuesta de prueba", 100, 50, 150));

        var loggerMock = new Mock<ILogger<ChatBusinessIntelligenceService>>();

        var service = new ChatBusinessIntelligenceService(aiMock.Object, db, loggerMock.Object);

        return (service, aiMock, db);
    }

    #region Property 11a: Todos los mensajes del historial están presentes en la invocación al IAIService

    /// <summary>
    /// Property 11a: Para cualquier secuencia de mensajes de historial, todos los mensajes
    /// deben estar presentes en la lista enviada al IAIService.ChatCompletionAsync,
    /// con su rol y contenido intactos.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Historial_PreservaTodosLosMensajes()
    {
        var historialArb = HistorialGen.ToArbitrary();
        var preguntaArb = ContenidoGen.ToArbitrary();
        var rolArb = Gen.Elements(RolesPOS).ToArbitrary();

        return Prop.ForAll(historialArb, preguntaArb, rolArb,
            (List<ChatMessage> historial, string pregunta, string rol) =>
            {
                var (service, aiMock, db) = CrearServicioConCaptura();

                try
                {
                    // Ejecutar la consulta
                    service.ProcesarConsultaAsync(1, 1, rol, historial, pregunta).GetAwaiter().GetResult();

                    // Capturar los mensajes enviados al IAIService
                    List<ChatMessage>? mensajesEnviados = null;
                    aiMock.Verify(s => s.ChatCompletionAsync(
                        It.IsAny<List<ChatMessage>>(),
                        It.IsAny<CancellationToken>()), Times.Once);

                    aiMock.Invocations.Clear();

                    // Reconfigurar para capturar
                    aiMock.Setup(s => s.ChatCompletionAsync(
                            It.IsAny<List<ChatMessage>>(),
                            It.IsAny<CancellationToken>()))
                        .Callback<List<ChatMessage>, CancellationToken>((msgs, _) => mensajesEnviados = msgs)
                        .ReturnsAsync(AICompletionResult.Ok("Respuesta", 10, 5, 15));

                    // Ejecutar de nuevo con el callback de captura
                    service.ProcesarConsultaAsync(1, 1, rol, historial, pregunta).GetAwaiter().GetResult();

                    if (mensajesEnviados == null)
                        return false.ToProperty().Label("No se capturaron los mensajes enviados al IAIService");

                    // Verificar que cada mensaje del historial está presente
                    // Los mensajes enviados son: [system] + [historial...] + [pregunta_usuario]
                    // El historial comienza en el índice 1 (después del system prompt)
                    foreach (var msg in historial)
                    {
                        var encontrado = mensajesEnviados.Any(m => m.Role == msg.Role && m.Content == msg.Content);
                        if (!encontrado)
                            return false.ToProperty()
                                .Label($"Mensaje no encontrado: Role='{msg.Role}', Content='{msg.Content}'");
                    }

                    return true.ToProperty();
                }
                finally
                {
                    db.Dispose();
                }
            });
    }

    #endregion

    #region Property 11b: El orden del historial se preserva en la invocación al IAIService

    /// <summary>
    /// Property 11b: Para cualquier secuencia de mensajes de historial [m1, m2, ..., mn],
    /// los mensajes deben aparecer en el mismo orden relativo en la lista enviada al IAIService.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Historial_PreservaOrdenDeMensajes()
    {
        var historialArb = HistorialGen.ToArbitrary();
        var preguntaArb = ContenidoGen.ToArbitrary();
        var rolArb = Gen.Elements(RolesPOS).ToArbitrary();

        return Prop.ForAll(historialArb, preguntaArb, rolArb,
            (List<ChatMessage> historial, string pregunta, string rol) =>
            {
                var (service, aiMock, db) = CrearServicioConCaptura();

                try
                {
                    // Capturar los mensajes enviados
                    List<ChatMessage>? mensajesEnviados = null;
                    aiMock.Reset();
                    aiMock.Setup(s => s.ChatCompletionAsync(
                            It.IsAny<List<ChatMessage>>(),
                            It.IsAny<CancellationToken>()))
                        .Callback<List<ChatMessage>, CancellationToken>((msgs, _) =>
                            mensajesEnviados = new List<ChatMessage>(msgs))
                        .ReturnsAsync(AICompletionResult.Ok("Respuesta", 10, 5, 15));

                    // Ejecutar la consulta
                    service.ProcesarConsultaAsync(1, 1, rol, historial, pregunta).GetAwaiter().GetResult();

                    if (mensajesEnviados == null)
                        return false.ToProperty().Label("No se capturaron los mensajes enviados al IAIService");

                    if (historial.Count == 0)
                        return true.ToProperty().Label("Historial vacío: nada que verificar");

                    // Los mensajes enviados tienen la estructura:
                    // [0] = system prompt
                    // [1..n] = historial
                    // [n+1] = pregunta del usuario actual
                    // Verificar que el historial comienza en la posición 1
                    var inicioHistorial = 1; // Después del system prompt

                    for (int i = 0; i < historial.Count; i++)
                    {
                        var posicionEsperada = inicioHistorial + i;
                        if (posicionEsperada >= mensajesEnviados.Count)
                            return false.ToProperty()
                                .Label($"Posición {posicionEsperada} excede el tamaño de mensajes enviados ({mensajesEnviados.Count})");

                        var esperado = historial[i];
                        var actual = mensajesEnviados[posicionEsperada];

                        if (esperado.Role != actual.Role || esperado.Content != actual.Content)
                            return false.ToProperty()
                                .Label($"Mensaje en posición {i} difiere. " +
                                       $"Esperado: Role='{esperado.Role}', Content='{esperado.Content}'. " +
                                       $"Actual: Role='{actual.Role}', Content='{actual.Content}'");
                    }

                    return true.ToProperty();
                }
                finally
                {
                    db.Dispose();
                }
            });
    }

    #endregion

    #region Property 11c: La pregunta actual se agrega al final del historial

    /// <summary>
    /// Property 11c: Para cualquier historial y pregunta, el último mensaje enviado al IAIService
    /// (antes de la respuesta) debe ser la pregunta actual del usuario con role="user".
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Historial_PreguntaActualAlFinal()
    {
        var historialArb = HistorialGen.ToArbitrary();
        var preguntaArb = ContenidoGen.ToArbitrary();
        var rolArb = Gen.Elements(RolesPOS).ToArbitrary();

        return Prop.ForAll(historialArb, preguntaArb, rolArb,
            (List<ChatMessage> historial, string pregunta, string rol) =>
            {
                var (service, aiMock, db) = CrearServicioConCaptura();

                try
                {
                    // Capturar los mensajes enviados
                    List<ChatMessage>? mensajesEnviados = null;
                    aiMock.Reset();
                    aiMock.Setup(s => s.ChatCompletionAsync(
                            It.IsAny<List<ChatMessage>>(),
                            It.IsAny<CancellationToken>()))
                        .Callback<List<ChatMessage>, CancellationToken>((msgs, _) =>
                            mensajesEnviados = new List<ChatMessage>(msgs))
                        .ReturnsAsync(AICompletionResult.Ok("Respuesta", 10, 5, 15));

                    service.ProcesarConsultaAsync(1, 1, rol, historial, pregunta).GetAwaiter().GetResult();

                    if (mensajesEnviados == null)
                        return false.ToProperty().Label("No se capturaron los mensajes enviados");

                    // El último mensaje debe ser la pregunta actual del usuario
                    var ultimoMensaje = mensajesEnviados[^1];
                    var esUltimoUser = ultimoMensaje.Role == "user" && ultimoMensaje.Content == pregunta;

                    return esUltimoUser.ToProperty()
                        .Label($"Último mensaje esperado: Role='user', Content='{pregunta}'. " +
                               $"Actual: Role='{ultimoMensaje.Role}', Content='{ultimoMensaje.Content}'");
                }
                finally
                {
                    db.Dispose();
                }
            });
    }

    #endregion

    #region Property 11d: Estructura completa - system + historial + pregunta

    /// <summary>
    /// Property 11d: La lista de mensajes enviada al IAIService tiene exactamente la estructura:
    /// 1 system prompt + N mensajes de historial + 1 pregunta del usuario.
    /// Total = 1 + historial.Count + 1 = historial.Count + 2.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Historial_EstructuraCompleta_SystemHistorialPregunta()
    {
        var historialArb = HistorialGen.ToArbitrary();
        var preguntaArb = ContenidoGen.ToArbitrary();
        var rolArb = Gen.Elements(RolesPOS).ToArbitrary();

        return Prop.ForAll(historialArb, preguntaArb, rolArb,
            (List<ChatMessage> historial, string pregunta, string rol) =>
            {
                var (service, aiMock, db) = CrearServicioConCaptura();

                try
                {
                    // Capturar los mensajes enviados
                    List<ChatMessage>? mensajesEnviados = null;
                    aiMock.Reset();
                    aiMock.Setup(s => s.ChatCompletionAsync(
                            It.IsAny<List<ChatMessage>>(),
                            It.IsAny<CancellationToken>()))
                        .Callback<List<ChatMessage>, CancellationToken>((msgs, _) =>
                            mensajesEnviados = new List<ChatMessage>(msgs))
                        .ReturnsAsync(AICompletionResult.Ok("Respuesta", 10, 5, 15));

                    service.ProcesarConsultaAsync(1, 1, rol, historial, pregunta).GetAwaiter().GetResult();

                    if (mensajesEnviados == null)
                        return false.ToProperty().Label("No se capturaron los mensajes");

                    // Verificar tamaño total: 1 (system) + historial.Count + 1 (pregunta)
                    var tamanoEsperado = 1 + historial.Count + 1;
                    var tamanoCorecto = mensajesEnviados.Count == tamanoEsperado;

                    if (!tamanoCorecto)
                        return false.ToProperty()
                            .Label($"Tamaño esperado: {tamanoEsperado}, actual: {mensajesEnviados.Count}");

                    // Verificar que el primer mensaje es system
                    var primerMensajeEsSystem = mensajesEnviados[0].Role == "system";
                    if (!primerMensajeEsSystem)
                        return false.ToProperty()
                            .Label($"Primer mensaje debe ser 'system', actual: '{mensajesEnviados[0].Role}'");

                    // Verificar que el último mensaje es la pregunta del usuario
                    var ultimoEsPregunta = mensajesEnviados[^1].Role == "user"
                                           && mensajesEnviados[^1].Content == pregunta;

                    return ultimoEsPregunta.ToProperty()
                        .Label($"Último mensaje debe ser pregunta del usuario");
                }
                finally
                {
                    db.Dispose();
                }
            });
    }

    #endregion
}
