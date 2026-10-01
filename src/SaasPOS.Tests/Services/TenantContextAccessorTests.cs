using Microsoft.AspNetCore.Http;
using SaasPOS.Api.Services;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Tests.Services;

public class TenantContextAccessorTests
{
    [Fact]
    public void WhenNoOverride_DelegatesToDefaultContext()
    {
        // Arrange: create a TenantContext that reads from HttpContext
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
        var defaultContext = new TenantContext(httpContextAccessor);

        var accessor = new TenantContextAccessor(defaultContext);

        // Act & Assert — no JWT claims → ComercioId is null, IsSuperAdmin is false
        Assert.Null(accessor.ComercioId);
        Assert.False(accessor.IsSuperAdmin);
    }

    [Fact]
    public void WhenOverrideSet_UsesOverrideContext()
    {
        // Arrange
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var defaultContext = new TenantContext(httpContextAccessor);
        var accessor = new TenantContextAccessor(defaultContext);

        // Act — override with AdminTenantContext
        accessor.Override = new AdminTenantContext();

        // Assert
        Assert.Null(accessor.ComercioId);
        Assert.True(accessor.IsSuperAdmin);
    }

    [Fact]
    public void AdminTenantContext_HasExpectedValues()
    {
        var adminContext = new AdminTenantContext();

        Assert.Null(adminContext.ComercioId);
        Assert.True(adminContext.IsSuperAdmin);
    }
}
