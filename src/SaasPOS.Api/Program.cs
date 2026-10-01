using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SaasPOS.Api.Middleware;
using SaasPOS.Application.Interfaces;
using SaasPOS.Api.Services;
using SaasPOS.Infrastructure.Configuration;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(config.GetConnectionString("DefaultConnection")));

// ── Tenant Context (Scoped — populated per-request from JWT claims) ───────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<TenantContextAccessor>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());

// ── Auth Service ──────────────────────────────────────────────────────────────
builder.Services.Configure<JwtSettings>(config.GetSection(JwtSettings.SectionName));
// Cifrado AES-256 de la contraseña temporal de recuperación; requerido por AuthService (Req 4.5).
builder.Services.AddScoped<IPasswordEncryptionService, AesPasswordEncryptionService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// ── Subscription Guard (plan limit enforcement) ───────────────────────────────
builder.Services.AddScoped<ISubscriptionGuard, SubscriptionGuard>();

// ── Role Validator (roles por plan enforcement) ───────────────────────────────
builder.Services.AddScoped<IRoleValidator, RoleValidator>();

// ── Audit Service (Req 6.7: audit log for product changes) ────────────────────
builder.Services.AddScoped<IAuditService, AuditService>();

// ── Security Audit Service (Req 3.4, 3.7: security event logging) ─────────────
builder.Services.AddScoped<ISecurityAuditService, SecurityAuditService>();

// ── Security Settings (SSRF protection + CSP configuration, Req 3.1, 3.2) ────
builder.Services.Configure<SecuritySettings>(config.GetSection(SecuritySettings.SectionName));
builder.Services.AddSingleton<IUrlValidationService, UrlValidationService>();

// ── Configuracion Sucursal Service (per-branch configuration) ─────────────────
builder.Services.AddScoped<IConfiguracionSucursalService, ConfiguracionSucursalService>();

// ── Comprobantes Config Service (Req 6.x: tipos de respaldo comercial) ────────
builder.Services.AddScoped<IComprobantesConfigService, ComprobantesConfigService>();

// ── Stock Service (Req 7.x: stock management) ─────────────────────────────────
builder.Services.AddScoped<IStockService, StockService>();

// ── Notification Service (Req 7.7: low stock notifications) ───────────────────
builder.Services.AddScoped<INotificationService, NotificationService>();

// ── Email Service (Req 14.5, 19.11, 19.12: persistent email queue) ────────────
builder.Services.Configure<SaasPOS.Infrastructure.Configuration.SmtpSettings>(
    config.GetSection(SaasPOS.Infrastructure.Configuration.SmtpSettings.SectionName));
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddTransient<ISmtpClient, SmtpEmailClient>();
builder.Services.AddHostedService<EmailProcessorBackgroundService>();

// ── Billing Service (Req 19.x: subscription billing) ──────────────────────────
builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddHostedService<BillingCutBackgroundService>();

// ── Trial Service (prueba gratuita de 15 días) ────────────────────────────────
builder.Services.AddScoped<ITrialService, TrialService>();

// ── Password Recovery Cleanup (Req 7.1: barrido periódico de solicitudes expiradas) ─
// Servicio en segundo plano que elimina/expira solicitudes de recuperación vencidas cada 15 min.
builder.Services.AddHostedService<PasswordRecoveryCleanupBackgroundService>();

// ── Report Engine (Req 13.x: report generation and export) ────────────────────
builder.Services.AddScoped<IReportEngine, ReportEngine>();

// ── SRI Service (Req 11.x: electronic invoicing) ──────────────────────────────
builder.Services.AddHttpClient<ISRIService, SRIService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ── Timezone Service (Singleton — conversión centralizada de zona horaria) ─────
builder.Services.AddSingleton<ITimezoneService, TimezoneService>();

// ── Timezone Resolver (Scoped — resuelve zona horaria por request) ──────────────
builder.Services.AddScoped<ITimezoneResolver, TimezoneResolver>();

// ── IP Blocking Service (Singleton — DDoS protection, Req 23.6) ───────────────
builder.Services.AddSingleton<IIpBlockingService, IpBlockingService>();

// ── JTI Blocklist (Singleton — state must persist across requests) ─────────────
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<JtiBlocklist>();
builder.Services.AddSingleton<IJtiBlocklist>(sp => sp.GetRequiredService<JtiBlocklist>());

// ── Account Lockout (Singleton — Req 23.8: brute-force protection) ────────────
builder.Services.AddSingleton<IAccountLockoutService, AccountLockoutService>();

// ── Login Attempt Tracker (Singleton — Req 4.5, 4.6, 4.7: intentos restantes) ─
builder.Services.AddSingleton<ILoginAttemptTracker, LoginAttemptTracker>();

// ── NVIDIA AI Service (Req 1.2, 1.4, 1.7, 3.2) ──────────────────────────────
builder.Services.Configure<NvidiaAISettings>(config.GetSection(NvidiaAISettings.SectionName));
builder.Services.AddHttpClient("NvidiaAI", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddScoped<IAIService, NvidiaAIService>();

// ── Chat Business Intelligence Service (Req 7.5, 7.6, 7.7) ───────────────────
builder.Services.AddScoped<IChatBusinessIntelligenceService, ChatBusinessIntelligenceService>();

// ── JWT Authentication ────────────────────────────────────────────────────────
var jwtSection = config.GetSection("JwtSettings");
var secretKey = jwtSection["SecretKey"] ?? throw new InvalidOperationException("JwtSettings:SecretKey is required.");
var issuer = jwtSection["Issuer"] ?? "saas-pos-api";
var audience = jwtSection["Audience"] ?? "saas-pos-clients";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Prevent .NET from remapping JWT claims (e.g., "role" → long ClaimTypes.Role URI).
        // Our middleware reads claims by their original short names.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = "role",
            NameClaimType = "sub"
        };
    });

// ── Authorization Policies (RBAC) ─────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TenantAccess", policy =>
        policy.RequireRole("Cajero", "Gerente", "Dueño", "Supervisor", "Bodeguero"));

    options.AddPolicy("CanManageProducts", policy =>
        policy.RequireRole("Gerente", "Dueño"));

    options.AddPolicy("CanManageUsers", policy =>
        policy.RequireRole("Gerente", "Dueño"));

    options.AddPolicy("CanManageSucursales", policy =>
        policy.RequireRole("Gerente", "Dueño"));

    options.AddPolicy("CanSell", policy =>
        policy.RequireRole("Cajero", "Gerente", "Dueño"));

    options.AddPolicy("CanViewReports", policy =>
        policy.RequireRole("Gerente", "Dueño", "Supervisor"));

    options.AddPolicy("CanManageStock", policy =>
        policy.RequireRole("Gerente", "Dueño", "Bodeguero"));

    options.AddPolicy("CanManageConfig", policy =>
        policy.RequireRole("Gerente", "Dueño"));

    options.AddPolicy("SuperAdminOnly", policy =>
        policy.RequireRole("SuperAdmin"));
});

// ── CORS ─────────────────────────────────────────────────────────────────────
var posOrigin = config["Cors:PosOrigin"] ?? "https://pos.saas.com";
var adminOrigin = config["Cors:AdminOrigin"] ?? "https://admin.saas.com";

builder.Services.AddCors(opt => opt.AddPolicy("SaaS", policy =>
{
    policy.WithOrigins(posOrigin, adminOrigin)
          .AllowAnyHeader()
          .AllowAnyMethod()
          .AllowCredentials();
}));

// ── Parámetros de validación JWT para Rate Limiting diferenciado ────────────────
// Se construyen aquí (fuera del lambda) para reutilizar la misma instancia en cada petición.
// Coinciden con los parámetros del middleware de Authentication.
var rateLimitTokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true,
    ValidateAudience = true,
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    ValidIssuer = issuer,
    ValidAudience = audience,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
    ClockSkew = TimeSpan.Zero,
    RoleClaimType = "role",
    NameClaimType = "sub"
};

// ── IServiceScopeFactory singleton para auditoría de rate limiting ────────────
// Se declara aquí y se asigna tras builder.Build() para evitar el race condition
// donde Task.Run accede a RequestServices ya disposed (Bug 2 — Req 2.2).
IServiceScopeFactory? hostScopeFactory = null;

// ── Rate Limiting (Req 23.1, 23.2, 23.3) ──────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    // Rate Limiting Diferenciado:
    // - Tráfico autenticado (JWT válido con claim "sub"): 300 req/min por IP:UserId
    // - Tráfico no autenticado (sin JWT o JWT inválido): 100 req/min por IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // Extraer IP real del cliente (soporta X-Forwarded-For)
        var ip = RateLimitHelpers.ObtenerIpCliente(context);

        // Intentar extraer UserId del token JWT (validación inline)
        var userId = RateLimitHelpers.ExtraerUserIdDeToken(context, rateLimitTokenValidationParameters);

        if (userId is not null)
        {
            var partitionKey = $"{ip}:{userId}";

            // Almacenar metadata para el middleware de headers X-RateLimit-*
            context.Items["RateLimit_IsAuthenticated"] = true;
            context.Items["RateLimit_Limit"] = 300;
            context.Items["RateLimit_UserId"] = userId;

            // Calcular remaining y reset para headers X-RateLimit-*
            var (remaining, resetTimestamp) = RateLimitHeadersMiddleware.TrackRequest(partitionKey, 300);
            context.Items["RateLimit_Remaining"] = remaining;
            context.Items["RateLimit_Reset"] = resetTimestamp;

            // Partición autenticada: IP:UserId → 300 req/min (Fixed Window)
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: partitionKey,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 300,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
        }

        // Almacenar metadata para tráfico anónimo
        context.Items["RateLimit_IsAuthenticated"] = false;
        context.Items["RateLimit_Limit"] = 100;

        // Partición anónima: IP → 100 req/min (Fixed Window)
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    // LoginPolicy: 3 intentos / 30 min por IP+email
    // Particionado por IP:email para protección granular contra fuerza bruta.
    // Se aplica independientemente del estado de autenticación (JWT válido o no).
    options.AddPolicy("LoginPolicy", context =>
    {
        var ip = RateLimitHelpers.ObtenerIpCliente(context);
        var email = RateLimitHelpers.ExtraerEmailDeBody(context);
        var partitionKey = $"{ip}:{email}";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(30),
                QueueLimit = 0
            });
    });

    // Req 23.3: Password Recovery — 3 requests/hour per IP (partitioned by IP)
    options.AddPolicy("PasswordRecoveryPolicy", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            }));

    // Req 9.1–9.5: AIRateLimit — Fixed Window por ComercioId, configurable desde NvidiaAISettings
    var aiSettings = config.GetSection(NvidiaAISettings.SectionName).Get<NvidiaAISettings>() ?? new NvidiaAISettings();
    options.AddPolicy("AIRateLimit", context =>
    {
        // Extraer ComercioId del token JWT para particionar por tenant
        var comercioId = ExtraerComercioIdDeToken(context, rateLimitTokenValidationParameters);
        var partitionKey = comercioId ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = aiSettings.RequestsPerMinutePerTenant,
                Window = TimeSpan.FromSeconds(60),
                QueueLimit = 0
            });
    });

    // Req 7.8: ChatBIRateLimit — 20 consultas/hora por usuario para el endpoint de Chat BI
    options.AddPolicy("ChatBIRateLimit", context =>
    {
        // Particionar por UserId extraído del token JWT
        var userId = RateLimitHelpers.ExtraerUserIdDeToken(context, rateLimitTokenValidationParameters);
        var partitionKey = userId ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"chatbi:{partitionKey}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            });
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // NOTA: OnRejected se configura con una referencia temporal; el scopeFactory real
    // se asigna después de builder.Build() mediante la variable capturada por closure.
    // Se usa un placeholder que se sobreescribe más abajo, tras construir el app.
    options.OnRejected = async (context, cancellationToken) =>
    {
        // Calcular Retry-After desde metadata del lease, con clamp entre 1 y 1800
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? (int)Math.Ceiling(retryAfter.TotalSeconds)
            : 60;
        retryAfterSeconds = Math.Clamp(retryAfterSeconds, 1, 1800);

        // Establecer headers de respuesta
        context.HttpContext.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
        context.HttpContext.Response.ContentType = "application/json";
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        // Determinar si el path es de AI para mensaje específico
        var isAI = context.HttpContext.Request.Path.Value?.Contains("/ai/", StringComparison.OrdinalIgnoreCase) == true;

        // Determinar si el path es login para incluir campo adicional retryAfterSeconds
        var isLogin = context.HttpContext.Request.Path.Value?.Contains("/auth/login", StringComparison.OrdinalIgnoreCase) == true;

        // Body JSON con campos error y code (en español)
        var responseBody = new Dictionary<string, object>
        {
            ["error"] = isAI
                ? "Límite de solicitudes de IA excedido para este tenant. Intente de nuevo más tarde."
                : "Demasiadas solicitudes. Intente de nuevo más tarde.",
            ["code"] = isAI ? "AI_RATE_LIMIT_EXCEEDED" : "RATE_LIMIT_EXCEEDED"
        };

        // Si es login, incluir campo adicional retryAfterSeconds
        if (isLogin)
            responseBody["retryAfterSeconds"] = retryAfterSeconds;

        // Si es AI, incluir retryAfterSeconds para el cliente
        if (isAI)
            responseBody["retryAfterSeconds"] = retryAfterSeconds;

        await context.HttpContext.Response.WriteAsJsonAsync(responseBody, cancellationToken);

        // Auditoría fire-and-forget (no bloquear la respuesta).
        // Se extrae toda la información del HttpContext de forma SINCRÓNICA aquí,
        // antes de que el scope del request pueda ser disposed.
        // El scopeFactory singleton se captura del host root (asignado post-Build más abajo).
        var auditIp = RateLimitHelpers.ObtenerIpCliente(context.HttpContext);
        var auditPath = context.HttpContext.Request.Path.ToString();
        int? auditUserId = null;
        if (context.HttpContext.Items.TryGetValue("RateLimit_UserId", out var auditUserIdObj)
            && auditUserIdObj is string auditUserIdStr
            && int.TryParse(auditUserIdStr, out var parsedAuditUserId))
        {
            auditUserId = parsedAuditUserId;
        }
        _ = RegistrarAuditoriaRechazoAsync(hostScopeFactory!, auditIp, auditPath, auditUserId);
    };
});

// ── Deserialización JSON segura (Req 3.3) ─────────────────────────────────────
// Opciones restrictivas que prohíben la inferencia de tipos polimórficos no declarados.
// System.Text.Json por defecto NO soporta polimorfismo salvo que se configure explícitamente,
// pero se deshabilita explícitamente para defensa en profundidad.
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    // Prohibir lecturas/escrituras polimórficas no declaradas
    options.SerializerOptions.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
    // No permitir trailing commas ni comments (payloads estrictos)
    options.SerializerOptions.ReadCommentHandling = JsonCommentHandling.Disallow;
    options.SerializerOptions.AllowTrailingCommas = false;
    // Profundidad máxima de deserialización para prevenir ataques de recursión
    options.SerializerOptions.MaxDepth = 32;
});

// ── Cookie Policy segura (Req 3.6) ───────────────────────────────────────────
// Configura todas las cookies de sesión con HttpOnly, Secure, SameSite=Strict
// y expiración alineada con el tiempo de vida del JWT.
var jwtExpirationMinutes = config.GetValue("JwtSettings:ExpirationMinutes", 60);
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always;
    options.Secure = CookieSecurePolicy.Always;
    options.MinimumSameSitePolicy = SameSiteMode.Strict;
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.MaxAge = TimeSpan.FromMinutes(jwtExpirationMinutes);
});

// ── Controllers + Swagger ─────────────────────────────────────────────────────
builder.Services.AddControllers(options =>
{
    options.Filters.Add<SaasPOS.Api.Filters.TimezoneResponseFilter>();
})
.AddJsonOptions(options =>
{
    // Req 3.3: Opciones restrictivas de System.Text.Json para controllers
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    // Prohibir inferencia de tipos polimórficos no declarados
    options.JsonSerializerOptions.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
    // No permitir trailing commas ni comments — solo JSON estricto
    options.JsonSerializerOptions.ReadCommentHandling = JsonCommentHandling.Disallow;
    options.JsonSerializerOptions.AllowTrailingCommas = false;
    // Profundidad máxima para prevenir ataques de recursión profunda
    options.JsonSerializerOptions.MaxDepth = 32;
    // Conversión de enums como strings para legibilidad
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SaasPOS API",
        Version = "v1",
        Description = "Multi-tenant SaaS POS system API"
    });

    // Add JWT bearer support in Swagger UI
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {token}",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ── Kestrel Body Size Limit (Req 23.12: 1MB default) ──────────────────────────
// NOTE: For the SRI electronic signature upload endpoint, use [RequestSizeLimit(5_242_880)]
// attribute on the controller action to allow up to 5MB.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1_048_576; // 1 MB
});

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Capturar IServiceScopeFactory del container root (singleton, vive toda la vida del host).
// Esto resuelve el Bug 2: evita acceder a RequestServices dentro de Task.Run
// cuando el scope del request ya puede haber sido disposed (Req 2.2).
hostScopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();

// ── Validación de configuración de NVIDIA AI al inicio (Req 1.7, 3.2) ─────────
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var nvAiSettings = config.GetSection(NvidiaAISettings.SectionName).Get<NvidiaAISettings>() ?? new NvidiaAISettings();

    // Advertir si la API key está vacía
    if (string.IsNullOrWhiteSpace(nvAiSettings.ApiKey))
    {
        logger.LogWarning("NvidiaAI: ApiKey no está configurada. El servicio de IA no estará disponible.");
    }

    // Validar rangos numéricos y aplicar defaults con log de error
    if (nvAiSettings.TimeoutSeconds < 5 || nvAiSettings.TimeoutSeconds > 120)
    {
        logger.LogError(
            "NvidiaAI: TimeoutSeconds = {Value} fuera del rango válido [5, 120]. Se usará el valor por defecto: 30.",
            nvAiSettings.TimeoutSeconds);
    }

    if (nvAiSettings.Temperature < 0.0 || nvAiSettings.Temperature > 2.0)
    {
        logger.LogError(
            "NvidiaAI: Temperature = {Value} fuera del rango válido [0.0, 2.0]. Se usará el valor por defecto: 1.0.",
            nvAiSettings.Temperature);
    }

    if (nvAiSettings.TopP < 0.0 || nvAiSettings.TopP > 1.0)
    {
        logger.LogError(
            "NvidiaAI: TopP = {Value} fuera del rango válido [0.0, 1.0]. Se usará el valor por defecto: 0.95.",
            nvAiSettings.TopP);
    }

    if (nvAiSettings.MaxTokens < 1 || nvAiSettings.MaxTokens > 131072)
    {
        logger.LogError(
            "NvidiaAI: MaxTokens = {Value} fuera del rango válido [1, 131072]. Se usará el valor por defecto: 16384.",
            nvAiSettings.MaxTokens);
    }
}

// ── Middleware pipeline (order matters) ───────────────────────────────────────
// Req 18.2/18.3: Global exception handler must be first to catch all errors
app.UseMiddleware<GlobalExceptionMiddleware>();

// Req 23.4/23.5: Security headers on every response
app.UseMiddleware<SecurityHeadersMiddleware>();

// Req 23.7/23.9/23.11/23.12: Input validation (SQL injection, XSS, Content-Type)
app.UseMiddleware<InputValidationMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Req 3.6: Aplicar política de cookies seguras (HttpOnly, Secure, SameSite=Strict)
app.UseCookiePolicy();

app.UseCors("SaaS");

// Req 23.6: IP blocking middleware (DDoS protection) — must be before rate limiter
app.UseMiddleware<IpBlockingMiddleware>();

app.UseRateLimiter();

app.UseAuthentication();

// Headers X-RateLimit-* para tráfico autenticado (Req 2.5)
// Debe ir después de Authentication para que el fallback pueda leer context.User
app.UseMiddleware<RateLimitHeadersMiddleware>();
app.UseMiddleware<JtiValidationMiddleware>();
app.UseMiddleware<RouteAuthorizationMiddleware>();
app.UseMiddleware<TenantContextMiddleware>();
app.UseMiddleware<CrossTenantGuardMiddleware>();
app.UseMiddleware<TimezoneResolutionMiddleware>();
app.UseAuthorization();

app.MapControllers();

// ── Auto-create database schema (Development only) ────────────────────────────
// EF Core creates tables with the exact PascalCase names it expects.
// Remove or replace with migrations for production.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.Run();

// ─────────────────────────────────────────────────────────────────────────────
// Función auxiliar para extraer ComercioId del token JWT (para rate limiting de AI).
// Valida el token y extrae el claim "comercioId". Retorna null si no se puede extraer.
// ─────────────────────────────────────────────────────────────────────────────
static string? ExtraerComercioIdDeToken(HttpContext context, TokenValidationParameters parameters)
{
    var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return null;

    var token = authHeader["Bearer ".Length..].Trim();
    if (string.IsNullOrEmpty(token))
        return null;

    try
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token, parameters, out _);

        // Extraer el claim "comercioId" que identifica al tenant
        var comercioIdClaim = principal.FindFirst("comercioId");
        return comercioIdClaim?.Value;
    }
    catch
    {
        // Token inválido → no se puede extraer ComercioId
        return null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Función auxiliar para auditoría de rechazos por rate limiting (fire-and-forget).
// Registra en la tabla LogsAuditoria con accion="RateLimitExcedido".
// Se ejecuta asíncronamente sin bloquear la respuesta 429 (Req 8.1–8.5).
//
// Fix Bug 2 (Req 2.2): recibe IServiceScopeFactory del host root (singleton) y los
// datos del request ya extraídos SINCRÓNICAMENTE por el llamador, evitando acceder a
// HttpContext/RequestServices dentro de Task.Run cuando el scope ya puede estar disposed.
// ─────────────────────────────────────────────────────────────────────────────
static Task RegistrarAuditoriaRechazoAsync(
    IServiceScopeFactory scopeFactory,
    string ipAddress,
    string path,
    int? usuarioId)
{
    return Task.Run(async () =>
    {
        try
        {
            // Usar scopeFactory singleton capturado del host root — nunca se dispone
            // mientras el proceso esté activo, a diferencia de RequestServices.
            using var scope = scopeFactory.CreateScope();
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

            // Determinar qué política causó el rechazo en base al path ya capturado
            var policy = path.Contains("/auth/login", StringComparison.OrdinalIgnoreCase) ? "Login"
                : path.Contains("/password-recovery", StringComparison.OrdinalIgnoreCase) ? "PasswordRecovery"
                : usuarioId.HasValue ? "Authenticated"
                : "Global";

            await auditService.RegistrarAsync(
                comercioId: 0,
                usuarioId: usuarioId,
                accion: "RateLimitExcedido",
                tablaAfectada: "Seguridad",
                registroId: "0",
                valoresAnteriores: null,
                valoresNuevos: new { IpAddress = ipAddress, Path = path, Policy = policy });
        }
        catch
        {
            // Descartar error silenciosamente — la auditoría nunca debe afectar la respuesta 429
        }
    });
}

// Expose Program for integration tests
public partial class Program { }
