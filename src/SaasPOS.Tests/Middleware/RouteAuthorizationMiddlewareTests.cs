using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Api.Middleware;

namespace SaasPOS.Tests.Middleware;

public class RouteAuthorizationMiddlewareTests
{
    private readonly RouteAuthorizationMiddleware _middleware;
    private bool _nextCalled;

    public RouteAuthorizationMiddlewareTests()
    {
        _nextCalled = false;
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var loggerMock = new Mock<ILogger<RouteAuthorizationMiddleware>>();
        _middleware = new RouteAuthorizationMiddleware(_ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        }, scopeFactoryMock.Object, loggerMock.Object);
    }

    private static HttpContext CreateContext(string path, ClaimsPrincipal? user = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (user != null)
            context.User = user;
        return context;
    }

    private static ClaimsPrincipal CreateUser(string role, bool authenticated = true)
    {
        var claims = new List<Claim> { new("role", role) };
        var identity = new ClaimsIdentity(claims, authenticated ? "TestScheme" : null);
        return new ClaimsPrincipal(identity);
    }

    // ── /api/admin/ route tests ─────────────────────────────────────────────

    [Fact]
    public async Task AdminRoute_SuperAdmin_Passes()
    {
        var context = CreateContext("/api/admin/comercios", CreateUser("SuperAdmin"));

        await _middleware.InvokeAsync(context);

        Assert.True(_nextCalled);
    }

    [Theory]
    [InlineData("Cajero")]
    [InlineData("Gerente")]
    [InlineData("Dueño")]
    [InlineData("Supervisor")]
    [InlineData("Bodeguero")]
    public async Task AdminRoute_TenantRole_Returns403(string role)
    {
        var context = CreateContext("/api/admin/comercios", CreateUser(role));

        await _middleware.InvokeAsync(context);

        Assert.False(_nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task AdminRoute_Unauthenticated_Returns401()
    {
        var context = CreateContext("/api/admin/comercios");

        await _middleware.InvokeAsync(context);

        Assert.False(_nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    // ── /api/tenants/ route tests ───────────────────────────────────────────

    [Theory]
    [InlineData("Cajero")]
    [InlineData("Gerente")]
    [InlineData("Dueño")]
    [InlineData("Supervisor")]
    [InlineData("Bodeguero")]
    public async Task TenantRoute_ValidTenantRole_Passes(string role)
    {
        var context = CreateContext("/api/tenants/productos", CreateUser(role));

        await _middleware.InvokeAsync(context);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task TenantRoute_SuperAdmin_Returns403()
    {
        var context = CreateContext("/api/tenants/productos", CreateUser("SuperAdmin"));

        await _middleware.InvokeAsync(context);

        Assert.False(_nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task TenantRoute_Unauthenticated_Returns401()
    {
        var context = CreateContext("/api/tenants/productos");

        await _middleware.InvokeAsync(context);

        Assert.False(_nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    // ── Non-matching routes pass through ────────────────────────────────────

    [Fact]
    public async Task NonApiRoute_PassesThrough()
    {
        var context = CreateContext("/swagger/index.html");

        await _middleware.InvokeAsync(context);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task AdminRoute_CaseInsensitive()
    {
        var context = CreateContext("/API/ADMIN/comercios", CreateUser("SuperAdmin"));

        await _middleware.InvokeAsync(context);

        Assert.True(_nextCalled);
    }
}
