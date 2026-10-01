using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SaasPOS.Tests.Integration.Fixtures;

/// <summary>
/// Static helper for generating JWT tokens in integration tests.
/// Uses the same constants as IntegrationTestBase for consistency.
/// </summary>
public static class JwtTestHelper
{
    private const string SecretKey = "IntegrationTestSecretKeyThatIsAtLeast32Characters!";
    private const string Issuer = "saas-pos-api";
    private const string Audience = "saas-pos-clients";

    /// <summary>
    /// TokenValidationParameters matching the test key, issuer, and audience.
    /// Can be used to configure JWT authentication in test fixtures.
    /// </summary>
    public static TokenValidationParameters TestParameters => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey)),
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = "role",
        NameClaimType = "sub"
    };

    /// <summary>
    /// Generates a valid JWT token for the given user context.
    /// </summary>
    public static string GenerateToken(
        int usuarioId,
        int comercioId,
        string role,
        int? sucursalId = null,
        DateTime? expires = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("sub", usuarioId.ToString()),
            new("comercio_id", comercioId.ToString()),
            new("role", role),
            new("jti", Guid.NewGuid().ToString())
        };

        if (sucursalId.HasValue)
            claims.Add(new Claim("sucursal_id", sucursalId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Sets the Authorization header on the given HttpClient with a valid JWT.
    /// </summary>
    public static void AuthenticateClient(HttpClient client, int usuarioId, int comercioId, string role)
    {
        var token = GenerateToken(usuarioId, comercioId, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
