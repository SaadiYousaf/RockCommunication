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

    /// <summary>
    /// Runs the rule pipeline for a single explicit participant — used when an agent is attached
    /// to a sale after it closed (e.g. a License Agent assigned by a Submission Agent at approval).
    /// Only rules whose TargetRole matches <paramref name="role"/> emit lines.
    /// </summary>
    Task<IReadOnlyList<CommissionLine>> CalculateForAgentAsync(Sale sale, Guid agentId, string role, CancellationToken ct = default);
}
