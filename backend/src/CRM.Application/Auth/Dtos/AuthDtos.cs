namespace CRM.Application.Auth.Dtos;

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    bool RequiresTwoFactor,
    string? TwoFactorToken,
    UserSummaryDto? User);

public record UserSummaryDto(
    Guid Id,
    string UserName,
    string Email,
    Guid AgencyId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Modules,
    bool MustChangePassword = false,
    Guid? TeamId = null,
    bool IsActive = true,
    Guid? CallCenterId = null,
    /// <summary>True when a privileged user must enrol in 2FA before using the app.</summary>
    bool TwoFactorSetupRequired = false,
    /// <summary>The user's agency name (null for SuperAdmin / central users with no agency),
    /// so the UI can show which agency the signed-in user belongs to.</summary>
    string? AgencyName = null,
    /// <summary>The user's call-center name (null when they're agency-level / not pinned to a
    /// call center), so the UI can show it under the agency in the sidebar.</summary>
    string? CallCenterName = null,
    /// <summary>True when this user's onboarding invitation lapsed (never accepted within the
    /// policy window). The UI shows "Expired" instead of "Pending" and prompts an admin resend.</summary>
    bool InvitationExpired = false,
    /// <summary>Currency the UI formats sale/commission money in ("USD" / "PKR") — an agency setting.</summary>
    string DisplayCurrency = "USD",
    /// <summary>Units of DisplayCurrency per 1 USD (1 = show stored USD figures unchanged).</summary>
    decimal ExchangeRate = 1m);

public record TwoFactorSetupResponse(string Secret, string QrCodeUri);

public record TwoFactorStatusDto(bool Enabled, string? Method);
