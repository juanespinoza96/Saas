namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Service for tracking and blocking IP addresses that exceed request thresholds (DDoS protection).
/// Req 23.6: Block IP for 30 minutes if it exceeds 500 requests in 5 minutes.
/// Req 23.10: Log blocking events in LogsAuditoria with TablaAfectada = 'Seguridad'.
/// </summary>
public interface IIpBlockingService
{
    /// <summary>
    /// Checks whether an IP address is currently blocked.
    /// </summary>
    bool IsBlocked(string ipAddress);

    /// <summary>
    /// Tracks a request from the given IP address. If the threshold is exceeded,
    /// the IP is automatically blocked.
    /// </summary>
    void TrackRequest(string ipAddress);

    /// <summary>
    /// Explicitly blocks an IP address for the specified duration.
    /// </summary>
    void BlockIp(string ipAddress, TimeSpan duration);
}
