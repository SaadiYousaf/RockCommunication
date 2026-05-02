using CRM.Domain.Entities;

namespace CRM.Application.Common.Commission;

public record CommissionContext(Sale Sale, Guid AgentId, string AgentRole, Guid? AgencyId);

public record CommissionLine(string RuleName, Guid AgentId, decimal Amount, string? Note = null);

public interface ICommissionRule
{
    string Name { get; }
    int Priority { get; }
    Task<IReadOnlyList<CommissionLine>> CalculateAsync(CommissionContext ctx, CancellationToken ct = default);
}

public interface ICommissionEngine
{
    Task<IReadOnlyList<CommissionLine>> CalculateForSaleAsync(Sale sale, CancellationToken ct = default);
}
