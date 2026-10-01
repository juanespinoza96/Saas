using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Middleware;

/// <summary>
/// Middleware for DDoS protection via IP blocking (Req 23.6).
/// Checks if the requesting IP is blocked and tracks requests per IP.
/// Must be placed BEFORE the rate limiter in the pipeline.
/// </summary>
public class IpBlockingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IIpBlockingService _ipBlockingService;

    public IpBlockingMiddleware(RequestDelegate next, IIpBlockingService ipBlockingService)
    {
        _next = next;
        _ipBlockingService = ipBlockingService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (_ipBlockingService.IsBlocked(ipAddress))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Acceso denegado. Su dirección IP ha sido bloqueada temporalmente.",
                code = "IP_BLOCKED"
            });
            return;
        }

        _ipBlockingService.TrackRequest(ipAddress);

        await _next(context);
    }
}
