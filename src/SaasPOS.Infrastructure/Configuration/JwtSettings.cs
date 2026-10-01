namespace SaasPOS.Infrastructure.Configuration;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "saas-pos-api";
    public string Audience { get; set; } = "saas-pos-clients";
    public int ExpirationMinutes { get; set; } = 60;
}
