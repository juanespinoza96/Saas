namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Manages a JTI (JWT Token ID) blocklist for immediate token revocation.
/// Blocked JTIs result in 401 Unauthorized responses (Req 3.4).
/// </summary>
public interface IJtiBlocklist
{
    /// <summary>
    /// Adds a specific JTI to the blocklist with an expiration matching the token's own expiry.
    /// </summary>
    void AddToBlocklist(string jti, DateTime expiration);

    /// <summary>
    /// Checks whether a JTI has been blocked.
    /// </summary>
    bool IsBlocked(string jti);

    /// <summary>
    /// Blocks all active sessions for a specific user.
    /// </summary>
    void BlockAllForUser(int userId);

    /// <summary>
    /// Blocks all active sessions for a commerce (Req 2.3 — suspension invalidates sessions).
    /// </summary>
    void BlockAllForComercio(int comercioId);
}
