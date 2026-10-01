namespace SaasPOS.Infrastructure.Configuration;

public class SmtpSettings
{
    public const string SectionName = "SmtpSettings";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = "noreply@saaspos.com";
    public string FromName { get; set; } = "SaasPOS";
    public bool EnableSsl { get; set; } = true;
}
