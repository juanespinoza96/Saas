namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Manages account lockout state for brute-force protection (Req 23.8).
/// Blocks an account for 30 minutes after 10 consecutive failed login attempts within 30 minutes.
/// </summary>
public interface IAccountLockoutService
{
    /// <summary>
    /// Checks whether the given email is currently locked out.
    /// </summary>
    bool IsLockedOut(string email);

    /// <summary>
    /// Records a failed login attempt for the given email.
    /// After 10 consecutive failures within 30 minutes, the account becomes locked.
    /// </summary>
    void RecordFailedAttempt(string email);

    /// <summary>
    /// Resets the failed attempt counter for the given email (e.g., on successful login).
    /// </summary>
    void ResetAttempts(string email);
}
