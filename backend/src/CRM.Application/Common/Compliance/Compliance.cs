namespace CRM.Application.Common.Compliance;

public record ComplianceCheck(bool Allowed, string? BlockReason, IReadOnlyList<string> Warnings);

public interface IPhoneNormalizer
{
    string Normalize(string raw);
}

public interface IDncChecker
{
    Task<bool> IsBlockedAsync(Guid agencyId, string phone, CancellationToken ct = default);
}

public interface ITcpaWindowChecker
{
    /// <summary>
    /// Checks whether the local time at the given US state is within TCPA-permitted calling hours
    /// (8 AM – 9 PM local). Returns true if call is permitted now.
    /// </summary>
    bool IsWithinPermittedWindow(string? state, DateTime utcNow);
}

public interface IComplianceGuard
{
    Task<ComplianceCheck> CheckOutboundDialAsync(Guid agencyId, string phone, string? state, CancellationToken ct = default);
}
