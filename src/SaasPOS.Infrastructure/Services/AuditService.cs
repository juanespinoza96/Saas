using System.Text.Json;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Persists audit log entries to LogsAuditoria table.
/// </summary>
public class AuditService : IAuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db)
    {
        _db = db;
    }

    public async Task RegistrarAsync(int comercioId, int? usuarioId, string accion, string tablaAfectada, string registroId, object? valoresAnteriores, object? valoresNuevos)
    {
        var log = new LogAuditoria
        {
            ComercioId = comercioId,
            UsuarioId = usuarioId,
            Accion = accion,
            FechaHora = DateTime.UtcNow,
            TablaAfectada = tablaAfectada,
            RegistroId = registroId,
            ValoresAnteriores = valoresAnteriores is not null ? JsonSerializer.Serialize(valoresAnteriores) : null,
            ValoresNuevos = valoresNuevos is not null ? JsonSerializer.Serialize(valoresNuevos) : null
        };

        _db.LogsAuditoria.Add(log);
        await _db.SaveChangesAsync();
    }
}
