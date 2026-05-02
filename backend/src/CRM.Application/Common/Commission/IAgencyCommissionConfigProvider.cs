namespace CRM.Application.Common.Commission;

public record AgencyCommissionRule(string RuleName, decimal? Amount, decimal? Threshold, bool Enabled);

public interface IAgencyCommissionConfigProvider
{
    Task<AgencyCommissionRule?> GetAsync(Guid agencyId, string ruleName, CancellationToken ct = default);
    Task<IReadOnlyList<AgencyCommissionRule>> GetAllAsync(Guid agencyId, CancellationToken ct = default);
    Task UpsertAsync(Guid agencyId, AgencyCommissionRule rule, CancellationToken ct = default);
}
