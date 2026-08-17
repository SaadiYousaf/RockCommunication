using CRM.Application.Auth.Dtos;

namespace CRM.Application.Common.Interfaces;

/// <summary>
/// Well-known custom JWT claim names. Kept here so the issuer (JwtTokenService) and
/// the consumer (authorization filters) can't drift.
/// </summary>
/// <summary>
/// Optional context rendered into the onboarding invitation email. Extensible: add fields
/// here rather than widening the RegisterAsync signature.
/// </summary>
public record InviteContext(string? DisplayName = null, string? AgencyName = null, string? CallCenterName = null);

public static class CustomJwtClaims
{
    /// <summary>"true" when the user must change their password before doing anything else.</summary>
    public const string PasswordChangeRequired = "pwd_change";
    /// <summary>The user's agency (tenant) id.</summary>
    public const string Agency = "agency";
    /// <summary>The user's call center id (absent = agency-level).</summary>
    public const string CallCenter = "callcenter";
    /// <summary>Subject (user id) — mirrors the standard "sub" claim read by hubs/services.</summary>
    public const string Subject = "sub";
    /// <summary>
    /// "true" when a privileged user (SuperAdmin/Admin/CEO) must enrol in two-factor auth
    /// before using the app. Gated by TwoFactorSetupRequiredMiddleware, exactly like the
    /// forced password change.
    /// </summary>
    public const string TwoFactorSetupRequired = "twofa_setup";
}

public interface IIdentityService
{
    /// <summary>
    /// Registers a new user. When <paramref name="password"/> is null/empty the service
    /// generates a strong temporary password, sets <c>MustChangePassword</c>, and emails
    /// the credentials to the new user. The plaintext temporary password is never echoed
    /// back through the API — the invite email is the only carrier — so a compromised
    /// admin session can't pivot to all freshly-invited users.
    /// </summary>
    /// <param name="callCenterId">
    /// Optional call center to pin the new user to (null = agency-level, sees every call
    /// center in the agency). Used by the onboarding flow to bind a Call Center Admin to
    /// the call center being created.
    /// </param>
    /// <param name="invite">
    /// Optional onboarding context (display name + org names) rendered into the invitation
    /// email. A record so future onboarding fields need no signature change.
    /// </param>
    Task<UserSummaryDto> RegisterAsync(string email, string userName, string? password, Guid agencyId, IEnumerable<string> roles, Guid? callCenterId = null, InviteContext? invite = null, CancellationToken ct = default);
    Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default);
    Task<LoginResponse> LoginAsync(string userNameOrEmail, string password, CancellationToken ct = default);
    Task<LoginResponse> VerifyTwoFactorAsync(string twoFactorToken, string code, CancellationToken ct = default);
    Task<TwoFactorSetupResponse> SetupTwoFactorAsync(Guid userId, CancellationToken ct = default);
    Task EnableTwoFactorAsync(Guid userId, string code, CancellationToken ct = default);
    Task DisableTwoFactorAsync(Guid userId, CancellationToken ct = default);
    Task<TwoFactorStatusDto> GetTwoFactorStatusAsync(Guid userId, CancellationToken ct = default);
    Task<TokenResult?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// Re-scopes the caller's session to a chosen agency/call-center (the admin context picker).
    /// Validates the choice against the caller's own reach — a SuperAdmin may pick any agency and a
    /// call center within it; an agency admin is confined to their own agency; a call-center admin to
    /// their own center. Re-mints access+refresh tokens carrying the scope (durable across refresh)
    /// and returns a LoginResponse whose UserSummary reflects the CHOSEN scope. null agency/center = "all".
    /// </summary>
    Task<LoginResponse> SetContextAsync(Guid userId, Guid? agencyId, Guid? callCenterId, CancellationToken ct = default);
    Task<UserSummaryDto?> GetUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserSummaryDto>> ListUsersAsync(Guid? agencyId, CancellationToken ct = default);

    /// <summary>
    /// Lightweight id→username lookup for hot polling paths (wallboards, live boards). Unlike
    /// <see cref="ListUsersAsync"/> it issues a single projection query and skips the per-user
    /// role/module round-trips, so it must only be used where names are all that's needed.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> ListUserNamesAsync(Guid? agencyId, CancellationToken ct = default);

    Task SendEmailConfirmationAsync(string email, CancellationToken ct = default);
    Task ConfirmEmailAsync(Guid userId, string token, CancellationToken ct = default);
    Task ForgotPasswordAsync(string email, CancellationToken ct = default);
    Task ResetPasswordAsync(string email, string token, string newPassword, CancellationToken ct = default);
}
