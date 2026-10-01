using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly AppDbContext _dbContext;

    public EmailService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task EnqueueAsync(int? comercioId, string destinatario, string asunto, string cuerpoHtml)
    {
        var correo = new ColaCorreo
        {
            ComercioId = comercioId,
            Destinatario = destinatario,
            Asunto = asunto,
            CuerpoHtml = cuerpoHtml,
            Intentos = 0,
            MaxIntentos = 3,
            Estado = "Pendiente",
            FechaCreacion = DateTime.UtcNow
        };

        _dbContext.ColaCorreos.Add(correo);
        await _dbContext.SaveChangesAsync();
    }
}
