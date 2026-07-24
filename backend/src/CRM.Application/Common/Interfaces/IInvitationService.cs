using CRM.Application.Auth.Dtos;

namespace CRM.Application.Common.Interfaces;

/// <summary>
/// Everything needed to provision an onboarded user. Kept as a record so new onboarding
/// fields (phone, title, locale, …) can be added later without changing the service signature.
/// </summary>
/// <param name="Email">Login + invitation address. Must be unique.</param>
/// <param name="FullName">Display name of the invitee (e.g. the Agency CEO's name).</param>
/// <param name="AgencyId">Agency the user belongs to.</param>
/// <param name="CallCenterId">Call center to pin the user to; null = agency-level.</param>
/// <param name="Roles">Roles to assign (e.g. CEO, CallCenterAdmin).</param>
/// <param name="AgencyName">Optional context rendered in the invitation email.</param>
/// <param name="CallCenterName">Optional context rendered in the invitation email.</param>
public record InvitationRequest(
    string Email,
    string FullName,
    Guid AgencyId,
    Guid? CallCenterId,
    IReadOnlyList<string> Roles,
    string? AgencyName = null,
    string? CallCenterName = null);

/// <summary>Outcome of an invite/resend. The temporary password is never returned.</summary>
public record InvitationResult(Guid UserId, string UserName, string Email, DateTime InvitationSentAt);

/// <summary>
/// Single reusable entry point for the hierarchical onboarding flow (Agency CEO,
/// Call Center Admin, and any future invited role).
///
/// It ORCHESTRATES the existing primitives rather than reimplementing them:
/// user creation + temporary password + forced first-login all come from
/// <see cref="IIdentityService.RegisterAsync"/> (called with a null password), and the
/// email uses the existing auth email template. This service adds only what was missing:
/// invitation status tracking and resend.
/// </summary>
public interface IInvitationService
{
    /// <summary>
    /// Creates the user, assigns roles, generates a temporary password, marks the account
    /// as first-login, and emails the invitation. Throws <c>ConflictException</c> if the
    /// email/username is already taken.
    /// </summary>
    Task<InvitationResult> InviteAsync(InvitationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Re-issues the invitation for a user who hasn't accepted yet: mints a fresh temporary
    /// password, re-arms the forced password change, and re-sends the email.
    /// </summary>
    Task<InvitationResult> ResendAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Marks the invitation accepted. Called when the user completes their forced
    /// first-login password change.
    /// </summary>
    Task MarkAcceptedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Checks whether an email is already taken, so callers can fail fast before creating parents.</summary>
    Task EnsureEmailAvailableAsync(string email, CancellationToken ct = default);
}
