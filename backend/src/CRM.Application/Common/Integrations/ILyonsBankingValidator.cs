namespace CRM.Application.Common.Integrations;

/// <summary>Outcome of a Lyons bank-account validation, mapped onto the sale banking-code policy.</summary>
public enum BankValidationStatus
{
    /// <summary>Account verified — sale may be submitted (banking code 103).</summary>
    Clear = 0,
    /// <summary>Account usable but flagged — sale may be submitted only with a verification recording (banking code 198).</summary>
    RequiresRecording = 1,
    /// <summary>Account failed validation — sale submission is blocked.</summary>
    Blocked = 2,
}

public record LyonsValidationRequest(
    string RoutingNumber,
    string AccountNumber,
    string? AccountType,
    string? AccountHolderName);

/// <summary>
/// Result of a Lyons banking validation. <see cref="BankingCode"/> is the value
/// stored on the sale (103 clear / 198 needs-recording; a non-submittable value
/// when <see cref="Status"/> is <see cref="BankValidationStatus.Blocked"/>).
/// </summary>
public record LyonsValidationResult(
    BankValidationStatus Status,
    int BankingCode,
    string? BankName,
    string? Reference,
    string? Reason);

/// <summary>
/// Lyons Commercial Data bank-account validation. Verifies a routing/account
/// number pair and returns the banking code that gates sale submission, so the
/// code is derived from a real check rather than entered by hand.
/// </summary>
public interface ILyonsBankingValidator
{
    Task<LyonsValidationResult> ValidateAsync(LyonsValidationRequest request, CancellationToken ct = default);
}
