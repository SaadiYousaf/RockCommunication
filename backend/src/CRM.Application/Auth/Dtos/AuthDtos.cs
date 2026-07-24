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
    bool TwoFactorSetupRequired = false);

public record TwoFactorSetupResponse(string Secret, string QrCodeUri);

public record TwoFactorStatusDto(bool Enabled, string? Method);
