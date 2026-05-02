namespace CRM.Application.Common.Integrations;

public record JornayaVerificationResult(bool Verified, string? Token, DateTime VerifiedAt, IDictionary<string, object>? Raw = null);

public interface IJornayaProvider
{
    Task<JornayaVerificationResult> VerifyAsync(string leadId, string? jornayaLeadId, CancellationToken ct = default);
}
