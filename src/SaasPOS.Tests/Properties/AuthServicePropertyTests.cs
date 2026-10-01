using System.IdentityModel.Tokens.Jwt;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Configuration;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for AuthService JWT claims correctness.
/// **Validates: Requirements 3.1**
/// </summary>
public class AuthServicePropertyTests
{
    private static readonly string[] ValidRoles =
        ["SuperAdmin", "Dueño", "Gerente", "Supervisor", "Bodeguero", "Cajero"];

    private static readonly JwtSettings TestJwtSettings = new()
    {
        SecretKey = "ThisIsATestSecretKeyThatIsLongEnoughForHmacSha256Algorithm!!",
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpirationMinutes = 60
    };

    /// <summary>
    /// Property 3: For any active user with valid credentials (any role, any commerce),
    /// the JWT issued by AuthService SHALL contain:
    /// - A `sub` claim matching the user's Id
    /// - A `comercio_id` claim matching the user's ComercioId
    /// - A `role` claim matching the user's Rol
    /// - A `jti` claim (non-empty GUID)
    /// - An `exp` claim (expiration in the future)
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool JwtClaimsMatchAuthenticatedUserRoleAndComerce(
        PositiveInt comercioIdGen,
        NonEmptyString emailPrefixGen)
    {
        // Derive test data from generated values
        var comercioId = (comercioIdGen.Get % 1000) + 1; // 1-1000 range
        var rolIndex = Math.Abs(comercioIdGen.Get) % ValidRoles.Length;
        var rol = ValidRoles[rolIndex];
        var email = SanitizeEmail(emailPrefixGen.Get);
        var plainPassword = "TestPassword123!";

        // Arrange
        var hashedPassword = AuthService.HashPassword(plainPassword);

        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new AppDbContext(options, tenantContextMock.Object);

        var user = new Usuario
        {
            ComercioId = comercioId,
            Nombre = "Test User",
            Email = email,
            PasswordHash = hashedPassword,
            Rol = rol,
            Activo = true
        };

        dbContext.Usuarios.Add(user);
        dbContext.SaveChanges();

        var jwtOptions = Options.Create(TestJwtSettings);
        var jtiBlocklistMock = new Mock<IJtiBlocklist>();
        var passwordEncryptionMock = new Mock<IPasswordEncryptionService>();
        var auditServiceMock = new Mock<IAuditService>();
        var authService = new AuthService(
            dbContext,
            jwtOptions,
            jtiBlocklistMock.Object,
            passwordEncryptionMock.Object,
            auditServiceMock.Object);

        // Act
        var result = authService.LoginAsync(email, plainPassword).GetAwaiter().GetResult();

        // Assert
        if (!result.Success || string.IsNullOrEmpty(result.Token))
            return false;

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(result.Token);

        var subClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        var comercioIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "comercio_id")?.Value;
        var roleClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "role")?.Value;
        var jtiClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "jti")?.Value;
        var expiration = jwtToken.ValidTo;

        var subMatches = subClaim == user.Id.ToString();
        var comercioMatches = comercioIdClaim == comercioId.ToString();
        var roleMatches = roleClaim == rol;
        var jtiIsValidGuid = Guid.TryParse(jtiClaim, out var parsedGuid) && parsedGuid != Guid.Empty;
        var expIsInFuture = expiration > DateTime.UtcNow;

        return subMatches && comercioMatches && roleMatches && jtiIsValidGuid && expIsInFuture;
    }

    /// <summary>
    /// Sanitizes generated string to create a valid email address.
    /// </summary>
    private static string SanitizeEmail(string prefix)
    {
        var sanitized = new string(prefix.Where(c => char.IsLetterOrDigit(c)).ToArray());
        if (string.IsNullOrEmpty(sanitized))
            sanitized = "user";
        return $"{sanitized}@test.com";
    }
}
