namespace SaasPOS.Application.Interfaces;

public interface IEmailService
{
    Task EnqueueAsync(int? comercioId, string destinatario, string asunto, string cuerpoHtml);
}
