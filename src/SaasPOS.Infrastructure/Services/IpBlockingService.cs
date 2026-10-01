using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SaasPOS.Application.Interfaces;

namespace SaasPOS.Infrastructure.Services;

/// <summary>
/// Tracks requests per IP using a sliding window in IMemoryCache.
/// Blocks an IP for 30 minutes if it exceeds 500 requests in a 5-minute window.
/// Req 23.6, 23.10.
/// </summary>
public class IpBlockingService : IIpBlockingService
{
    private const int MaxRequests = 500;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(30);

    private const string BlockKeyPrefix = "ip_block:";
    private const string TrackKeyPrefix = "ip_track:";

    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IpBlockingService> _logger;

    public IpBlockingService(
        IMemoryCache cache,
        IServiceScopeFactory scopeFactory,
        ILogger<IpBlockingService> logger)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool IsBlocked(string ipAddress)
    {
        return _cache.TryGetValue(BlockKeyPrefix + ipAddress, out _);
    }

    public void TrackRequest(string ipAddress)
    {
        if (IsBlocked(ipAddress))
            return;

        var key = TrackKeyPrefix + ipAddress;
        var timestamps = _cache.GetOrCreate(key, entry =>
        {
            entry.SlidingExpiration = Window;
            return new List<DateTime>();
        })!;

        lock (timestamps)
        {
            var cutoff = DateTime.UtcNow - Window;
            timestamps.RemoveAll(t => t < cutoff);
            timestamps.Add(DateTime.UtcNow);

            if (timestamps.Count > MaxRequests)
            {
                BlockIp(ipAddress, BlockDuration);
                timestamps.Clear();
            }
        }
    }

    public void BlockIp(string ipAddress, TimeSpan duration)
    {
        _cache.Set(BlockKeyPrefix + ipAddress, true, duration);
        _logger.LogWarning("IP {IpAddress} blocked for {Minutes} minutes (exceeded {Max} requests in {Window} min window)",
            ipAddress, duration.TotalMinutes, MaxRequests, Window.TotalMinutes);

        // Log to audit in a background fire-and-forget task using a scoped service
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
                await auditService.RegistrarAsync(
                    comercioId: 0,
                    usuarioId: null,
                    accion: "BloqueoIP",
                    tablaAfectada: "Seguridad",
                    registroId: ipAddress,
                    valoresAnteriores: null,
                    valoresNuevos: new
                    {
                        IpAddress = ipAddress,
                        DurationMinutes = duration.TotalMinutes,
                        Reason = $"Exceeded {MaxRequests} requests in {Window.TotalMinutes} minutes"
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log IP block audit for {IpAddress}", ipAddress);
            }
        });
    }
}
