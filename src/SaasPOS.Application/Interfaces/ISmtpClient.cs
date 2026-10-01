namespace SaasPOS.Application.Interfaces;

public interface ISmtpClient
{
    Task SendAsync(string destinatario, string asunto, string cuerpoHtml);
}
