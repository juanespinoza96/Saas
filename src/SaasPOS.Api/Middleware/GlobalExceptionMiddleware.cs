using Npgsql;
using System.Net.Sockets;

namespace SaasPOS.Api.Middleware;

/// <summary>
/// Middleware global de manejo de excepciones (Req 18.2, 18.3):
/// - Devuelve HTTP 503 solo cuando PostgreSQL no está disponible (fallos de conexión, socket,
///   timeout o excepciones transitorias de Npgsql).
/// - Devuelve HTTP 500 para el resto de excepciones no controladas, incluidos los errores LÓGICOS
///   de PostgreSQL (PostgresException con SqlState: columna inexistente, violación de FK/unique,
///   etc.), ya que en esos casos el servidor sí está disponible y un 503 enmascararía un bug real.
/// Debe registrarse primero en el pipeline para capturar todos los errores posteriores.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred");

            // Prevent writing to an already-started response
            if (context.Response.HasStarted)
            {
                _logger.LogWarning("Response has already started, cannot write error response.");
                throw;
            }

            // Req 18.2: HTTP 503 when PostgreSQL is unavailable
            if (IsPostgresUnavailable(ex))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "El servicio no está disponible temporalmente.",
                    code = "SERVICE_UNAVAILABLE"
                });
                return;
            }

            // Req 18.3: HTTP 500 for other unhandled errors
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Error interno del servidor.",
                code = "INTERNAL_ERROR"
            });
        }
    }

    private static bool IsPostgresUnavailable(Exception ex)
    {
        // Se recorre la cadena de excepciones (EF Core suele envolver la NpgsqlException real).
        var current = ex;
        while (current != null)
        {
            // Un PostgresException con SqlState es un error LÓGICO del servidor (columna inexistente,
            // violación de FK/unique, etc.). El servidor SÍ está disponible: no debe mapearse a 503,
            // sino a 500 para no enmascarar bugs de esquema/consulta.
            if (current is PostgresException)
                return false;

            // Fallo de socket subyacente → la BD no es alcanzable (503).
            if (current is SocketException)
                return true;

            if (current is NpgsqlException npgsqlEx)
            {
                // Errores transitorios/de infraestructura marcados por Npgsql como reintentables
                // (caídas de conexión, timeouts de red) indican indisponibilidad real → 503.
                if (npgsqlEx.IsTransient)
                    return true;

                // Señales explícitas de conexión o timeout en el mensaje → 503.
                if (npgsqlEx.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)
                    || npgsqlEx.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                    return true;

                // Cualquier otra NpgsqlException sin SqlState (no es PostgresException) se considera
                // un problema de la capa de conexión → 503.
                return true;
            }

            // InvalidOperationException de EF Core cuando no logra abrir la conexión a la BD → 503.
            if (current is InvalidOperationException && current.Message.Contains("database", StringComparison.OrdinalIgnoreCase))
                return true;

            current = current.InnerException;
        }

        return false;
    }
}
