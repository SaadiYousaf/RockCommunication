using CRM.Domain.Entities;

namespace CRM.Application.Common.Assignment;

public record AssignmentContext(Lead Lead, Guid AgencyId, string TargetRole, IReadOnlyList<Guid> EligibleUserIds);

public interface IAssignmentStrategy
{
    string Name { get; }
    Task<Guid?> PickAsync(AssignmentContext context, CancellationToken ct = default);
}

public interface IAssignmentService
{
    Task<Guid?> AssignAsync(Lead lead, string targetRole, string strategyName, CancellationToken ct = default);
}

public interface IAssignmentStrategyRegistry
{
    IAssignmentStrategy Get(string name);
    IReadOnlyList<string> AvailableStrategies { get; }
}
