using CRM.Application.Auth.Dtos;

namespace CRM.Application.Common.Interfaces;

/// <summary>
/// Well-known custom JWT claim names. Kept here so the issuer (JwtTokenService) and
/// the consumer (authorization filters) can't drift.
/// </summary>
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
    Task<UserSummaryDto> RegisterAsync(string email, string userName, string? password, Guid agencyId, IEnumerable<string> roles, CancellationToken ct = default);
    Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default);
    Task<LoginResponse> LoginAsync(string userNameOrEmail, string password, CancellationToken ct = default);
    Task<LoginResponse> VerifyTwoFactorAsync(string twoFactorToken, string code, CancellationToken ct = default);
    Task<TwoFactorSetupResponse> SetupTwoFactorAsync(Guid userId, CancellationToken ct = default);
    Task EnableTwoFactorAsync(Guid userId, string code, CancellationToken ct = default);
    Task DisableTwoFactorAsync(Guid userId, CancellationToken ct = default);
    Task<TwoFactorStatusDto> GetTwoFactorStatusAsync(Guid userId, CancellationToken ct = default);
    Task<TokenResult?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
    Task<UserSummaryDto?> GetUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserSummaryDto>> ListUsersAsync(Guid? agencyId, CancellationToken ct = default);

    Task SendEmailConfirmationAsync(string email, CancellationToken ct = default);
    Task ConfirmEmailAsync(Guid userId, string token, CancellationToken ct = default);
    Task ForgotPasswordAsync(string email, CancellationToken ct = default);
    Task ResetPasswordAsync(string email, string token, string newPassword, CancellationToken ct = default);
}
