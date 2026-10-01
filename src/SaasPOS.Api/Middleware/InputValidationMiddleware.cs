using System.Text.RegularExpressions;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Middleware;

/// <summary>
/// Validates request input against common SQL injection and XSS patterns (Req 23.7, 23.9, 23.11, 23.12).
/// - For requests with a body (POST/PUT/PATCH): validates Content-Type and scans body content.
/// - For GET/DELETE requests: scans query string parameter values.
/// Uses EnableBuffering to allow downstream handlers to read the body after validation.
/// </summary>
public class InputValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<InputValidationMiddleware> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    // SQL injection patterns — specific enough to avoid false positives on normal text
    private static readonly Regex[] SqlInjectionPatterns =
    [
        new Regex(@"';\s*DROP\s", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"--\s", RegexOptions.Compiled),
        new Regex(@"\bUNION\s+SELECT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bOR\s+1\s*=\s*1\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"'\s+OR\s+'1'\s*=\s*'1'", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@";\s*DROP\s+TABLE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    // XSS patterns — target actual injection attempts, not innocent words
    // Incluye variantes con escape Unicode de JSON (\u003c = <, \u003e = >) para detectar payloads serializados
    private static readonly Regex[] XssPatterns =
    [
        new Regex(@"<\s*script", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\\u003c\s*script", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\\u003c\s*/\s*script", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"javascript\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bonerror\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bonload\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bonclick\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly HashSet<string> MethodsWithBody = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH"
    };

    public InputValidationMiddleware(RequestDelegate next, ILogger<InputValidationMiddleware> logger, IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;

        if (MethodsWithBody.Contains(method))
        {
            // Validate Content-Type for requests that should have a body
            var contentType = context.Request.ContentType;
            if (string.IsNullOrEmpty(contentType) || !contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Request rejected: invalid Content-Type '{ContentType}' from {IP}",
                    contentType,
                    context.Connection.RemoteIpAddress);

                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Content-Type must be application/json.",
                    code = "UNSUPPORTED_MEDIA_TYPE"
                });
                return;
            }

            // Enable buffering so the body can be read multiple times
            context.Request.EnableBuffering();

            // Read the body for scanning
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();

            // Rewind the body stream for downstream handlers
            context.Request.Body.Position = 0;

            if (ContainsMaliciousContent(body))
            {
                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var detectedType = DetectMaliciousContentType(body) ?? "Unknown";

                _logger.LogWarning(
                    "Potentially dangerous input detected in request body from {IP} to {Path}",
                    ipAddress,
                    context.Request.Path);

                LogInjectionPatternAsync(ipAddress, context.Request.Path, detectedType);

                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Input contains potentially dangerous content.",
                    code = "DANGEROUS_INPUT"
                });
                return;
            }
        }
        else
        {
            // For GET/DELETE/HEAD/OPTIONS: scan query string values
            if (context.Request.QueryString.HasValue)
            {
                foreach (var param in context.Request.Query)
                {
                    foreach (var value in param.Value)
                    {
                        if (value != null && ContainsMaliciousContent(value))
                        {
                            var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                            var detectedType = DetectMaliciousContentType(value) ?? "Unknown";

                            _logger.LogWarning(
                                "Potentially dangerous input detected in query parameter '{Param}' from {IP}",
                                param.Key,
                                ipAddress);

                            LogInjectionPatternAsync(ipAddress, context.Request.Path, detectedType);

                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            context.Response.ContentType = "application/json";
                            await context.Response.WriteAsJsonAsync(new
                            {
                                error = "Input contains potentially dangerous content.",
                                code = "DANGEROUS_INPUT"
                            });
                            return;
                        }
                    }
                }
            }
        }

        await _next(context);
    }

    private static bool ContainsMaliciousContent(string input)
    {
        return DetectMaliciousContentType(input) != null;
    }

    private static string? DetectMaliciousContentType(string input)
    {
        foreach (var pattern in SqlInjectionPatterns)
        {
            if (pattern.IsMatch(input))
                return "SQLInjection";
        }

        foreach (var pattern in XssPatterns)
        {
            if (pattern.IsMatch(input))
                return "XSS";
        }

        return null;
    }

    /// <summary>
    /// Logs a detected injection pattern to the security audit trail (Req 3.4).
    /// Fire-and-forget to avoid impacting response latency.
    /// Utiliza ISecurityAuditService para auditoría de eventos de seguridad.
    /// </summary>
    private void LogInjectionPatternAsync(string ipAddress, string path, string type)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var securityAuditService = scope.ServiceProvider.GetRequiredService<ISecurityAuditService>();
                await securityAuditService.LogInputValidationErrorAsync(ipAddress, path, type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log injection pattern audit for {IP}", ipAddress);
            }
        });
    }
}
