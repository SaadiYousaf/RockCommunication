namespace CRM.Domain.Common;

/// <summary>
/// Onboarding-invitation lifecycle policy — the single source of truth for how long an invited
/// user has to sign in before their temporary-password link goes stale. After the window, login
/// is refused and an administrator must resend a fresh invitation (which re-arms the clock).
/// </summary>
public static class InvitationPolicy
{
    /// <summary>Days an unaccepted invitation stays valid before it expires.</summary>
    public const int ExpiryDays = 2;

    /// <summary>How long an unaccepted invitation stays valid before it expires.</summary>
    public static readonly TimeSpan ExpiryWindow = TimeSpan.FromDays(ExpiryDays);

    /// <summary>Shown at login when a stale, never-accepted invitation is used.</summary>
    public const string ExpiredMessage =
        "Your invitation link has expired. Please ask your administrator to resend it.";

    /// <summary>
    /// An invitation is expired when it was sent, never accepted, the account is still on its
    /// forced first-login password, and more than <see cref="ExpiryWindow"/> has elapsed.
    /// Established users (who completed their first login) are never affected.
    /// </summary>
    public static bool IsExpired(bool mustChangePassword, DateTime? sentAt, DateTime? acceptedAt, DateTime nowUtc)
        => mustChangePassword
           && acceptedAt is null
           && sentAt is { } s
           && nowUtc - s > ExpiryWindow;
}
