using FsCheck;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;
using System.Net;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for rate limiting middleware.
/// **Validates: Requirements 23.1, 23.2**
///
/// Property 22: Rate limiting blocks excessive requests.
/// "For any IP address that sends more than 100 requests within a 1-minute window,
/// the 101st request SHALL receive HTTP 429. For any IP that sends more than 5 login
/// requests within 1 minute, the 6th login request SHALL receive HTTP 429."
/// </summary>
public class RateLimitingPropertyTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimitingPropertyTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureServices(services =>
                {
                    // Replace PostgreSQL with InMemory database to avoid external dependency
                    var dbDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (dbDescriptor != null)
                        services.Remove(dbDescriptor);

                    services.AddDbContext<AppDbContext>((sp, options) =>
                    {
                        var tenantContext = sp.GetRequiredService<ITenantContext>();
                        options.UseInMemoryDatabase($"RateLimitTest_{Guid.NewGuid()}");
                    });
                });
            });
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    /// <summary>
    /// For any request count N > 100 from the same IP within a 1-minute window,
    /// at least one response among request 101..N SHALL be HTTP 429.
    /// This verifies the global rate limiter (Req 23.1).
    /// **Validates: Requirements 23.1**
    /// </summary>
    [Property(MaxTest = 5)]
    public Property GlobalRateLimit_BlocksAfterExcessiveRequests()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(101, 110)),
            requestCount =>
            {
                using var client = _factory.CreateClient();
                var got429 = false;

                for (var i = 1; i <= requestCount; i++)
                {
                    var response = client.GetAsync("/api/tenants/productos").GetAwaiter().GetResult();
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        got429 = true;
                        break;
                    }
                }

                return got429.Label($"Expected 429 after {requestCount} requests");
            });
    }

    /// <summary>
    /// For any request count N > 5 login attempts from the same IP within 1 minute,
    /// at least one response among request 6..N SHALL be HTTP 429.
    /// This verifies the login-specific rate limiter (Req 23.2).
    /// **Validates: Requirements 23.2**
    /// </summary>
    [Property(MaxTest = 5)]
    public Property LoginRateLimit_BlocksAfterExcessiveAttempts()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(6, 10)),
            requestCount =>
            {
                using var client = _factory.CreateClient();
                var got429 = false;
                var loginPayload = new StringContent(
                    """{"email":"test@example.com","password":"wrongpass"}""",
                    System.Text.Encoding.UTF8,
                    "application/json");

                for (var i = 1; i <= requestCount; i++)
                {
                    // Each iteration needs a fresh content object
                    var content = new StringContent(
                        """{"email":"test@example.com","password":"wrongpass"}""",
                        System.Text.Encoding.UTF8,
                        "application/json");

                    var response = client.PostAsync("/api/tenants/auth/login", content)
                        .GetAwaiter().GetResult();

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        got429 = true;
                        break;
                    }
                }

                return got429.Label($"Expected 429 after {requestCount} login requests");
            });
    }

    /// <summary>
    /// For any request count N ≤ 100 from the same IP within a 1-minute window,
    /// none of the responses SHALL be HTTP 429.
    /// This verifies that the global rate limiter does not trigger prematurely.
    /// **Validates: Requirements 23.1**
    /// </summary>
    [Property(MaxTest = 5)]
    public Property RequestsBelowGlobalLimit_AreNotRateLimited()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(1, 100)),
            requestCount =>
            {
                // Each test iteration needs its own factory to reset the rate limiter state
                using var freshFactory = new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(builder =>
                    {
                        builder.UseEnvironment("Testing");
                        builder.ConfigureServices(services =>
                        {
                            var dbDescriptor = services.SingleOrDefault(
                                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                            if (dbDescriptor != null)
                                services.Remove(dbDescriptor);

                            services.AddDbContext<AppDbContext>((sp, options) =>
                            {
                                var tenantContext = sp.GetRequiredService<ITenantContext>();
                                options.UseInMemoryDatabase($"RateLimitBelowTest_{Guid.NewGuid()}");
                            });
                        });
                    });

                using var client = freshFactory.CreateClient();
                var got429 = false;

                for (var i = 1; i <= requestCount; i++)
                {
                    var response = client.GetAsync("/api/tenants/productos").GetAwaiter().GetResult();
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        got429 = true;
                        break;
                    }
                }

                return (!got429).Label($"No 429 expected for {requestCount} requests (≤100)");
            });
    }
}
