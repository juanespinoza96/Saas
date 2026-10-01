using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Configuration;

namespace SaasPOS.Infrastructure.Services;

public class SmtpEmailClient : ISmtpClient
{
    private readonly SmtpSettings _settings;

    public SmtpEmailClient(IOptions<SmtpSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task SendAsync(string destinatario, string asunto, string cuerpoHtml)
    {
        using var client = new System.Net.Mail.SmtpClient(_settings.Host, _settings.Port)
        {
            Credentials = new NetworkCredential(_settings.Username, _settings.Password),
            EnableSsl = _settings.EnableSsl
        };

        var message = new MailMessage
        {
            From = new MailAddress(_settings.FromEmail, _settings.FromName),
            Subject = asunto,
            Body = cuerpoHtml,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(destinatario));

        await client.SendMailAsync(message);
    }
}
