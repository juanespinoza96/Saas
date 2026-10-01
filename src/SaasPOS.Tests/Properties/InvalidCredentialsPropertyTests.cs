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
/// **Validates: Requirements 3.2**
/// Property 4: Invalid credentials always produce the same generic response.
/// Regardless of whether the email doesn't exist, the password is wrong, or the
/// account is inactive, the error message SHALL always be "Credenciales inválidas".
/// </summary>
public class InvalidCredentialsPropertyTests : IDisposable
{
    private const string ExpectedErrorMessage = "Credenciales inválidas";
    private const string KnownPassword = "KnownPassword123!";

    private readonly AppDbContext _dbContext;
    private readonly AuthService _authService;

    public InvalidCredentialsPropertyTests()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new AppDbContext(options, tenantContextMock.Object);

        var jwtSettings = Options.Create(new JwtSettings
        {
            SecretKey = "SuperSecretKeyForTestingPurposesOnly_AtLeast32Chars!",
            Issuer = "test-issuer",
            Audience = "test-audience",
            ExpirationMinutes = 60
        });

        var jtiBlocklistMock = new Mock<IJtiBlocklist>();
        var passwordEncryptionMock = new Mock<IPasswordEncryptionService>();
        var auditServiceMock = new Mock<IAuditService>();

        _authService = new AuthService(
            _dbContext,
            jwtSettings,
            jtiBlocklistMock.Object,
            passwordEncryptionMock.Object,
            auditServiceMock.Object);

        // Seed an active user and an inactive user for testing
        var activeUser = new Usuario
        {
            Id = 1,
            ComercioId = 1,
            Nombre = "Active User",
            Email = "active@test.com",
            PasswordHash = AuthService.HashPassword(KnownPassword),
            Rol = "Cajero",
            Activo = true
        };

        var inactiveUser = new Usuario
        {
            Id = 2,
            ComercioId = 1,
            Nombre = "Inactive User",
            Email = "inactive@test.com",
            PasswordHash = AuthService.HashPassword(KnownPassword),
            Rol = "Cajero",
            Activo = false
        };

        _dbContext.Usuarios.AddRange(activeUser, inactiveUser);
        _dbContext.SaveChanges();
    }

    /// <summary>
    /// For any random email that doesn't exist in the system,
    /// login always fails with the generic "Credenciales inválidas" message.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NonExistentEmail_AlwaysReturnsGenericError(NonEmptyString randomEmail, NonEmptyString randomPassword)
    {
        // Ensure the generated email doesn't match our seeded users
        var email = randomEmail.Get + "@random.xyz";

        var result = _authService.LoginAsync(email, randomPassword.Get).GetAwaiter().GetResult();

        return result.Success == false
            && result.ErrorMessage == ExpectedErrorMessage
            && result.Token == null;
    }

    /// <summary>
    /// For an existing active user with any random wrong password,
    /// login always fails with the generic "Credenciales inválidas" message.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool WrongPassword_AlwaysReturnsGenericError(NonEmptyString randomPassword)
    {
        // Ensure the random password is never the correct one
        var wrongPassword = randomPassword.Get == KnownPassword
            ? randomPassword.Get + "_wrong"
            : randomPassword.Get;

        var result = _authService.LoginAsync("active@test.com", wrongPassword).GetAwaiter().GetResult();

        return result.Success == false
            && result.ErrorMessage == ExpectedErrorMessage
            && result.Token == null;
    }

    /// <summary>
    /// For an inactive user with any password (correct or incorrect),
    /// login always fails with the generic "Credenciales inválidas" message.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool InactiveAccount_AlwaysReturnsGenericError(NonEmptyString randomPassword)
    {
        // Test with random passwords — all should give same error for inactive account
        var result = _authService.LoginAsync("inactive@test.com", randomPassword.Get).GetAwaiter().GetResult();

        return result.Success == false
            && result.ErrorMessage == ExpectedErrorMessage
            && result.Token == null;
    }

    /// <summary>
    /// The key property: all three failure scenarios produce the exact same error message.
    /// Given any random string, the error message never varies from "Credenciales inválidas".
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AllFailureScenarios_ProduceSameErrorMessage(NonEmptyString randomStr)
    {
        var suffix = randomStr.Get;

        // Scenario 1: Non-existent email
        var nonExistentResult = _authService.LoginAsync(
            $"nonexistent_{suffix}@nowhere.com", "anypass").GetAwaiter().GetResult();

        // Scenario 2: Wrong password for active user
        var wrongPasswordResult = _authService.LoginAsync(
            "active@test.com", $"wrong_{suffix}").GetAwaiter().GetResult();

        // Scenario 3: Inactive account (correct password)
        var inactiveResult = _authService.LoginAsync(
            "inactive@test.com", KnownPassword).GetAwaiter().GetResult();

        // All three must have the EXACT same error message
        return nonExistentResult.ErrorMessage == ExpectedErrorMessage
            && wrongPasswordResult.ErrorMessage == ExpectedErrorMessage
            && inactiveResult.ErrorMessage == ExpectedErrorMessage
            && nonExistentResult.ErrorMessage == wrongPasswordResult.ErrorMessage
            && wrongPasswordResult.ErrorMessage == inactiveResult.ErrorMessage;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
