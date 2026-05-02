using CRM.Application.Common.Assignment;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Entities;
using CRM.Infrastructure.Identity;
using CRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Assignment;

public class RoundRobinStrategy : IAssignmentStrategy
{
    public string Name => "round-robin";
    private readonly IApplicationDbContext _db;

    public RoundRobinStrategy(IApplicationDbContext db) => _db = db;

    public async Task<Guid?> PickAsync(AssignmentContext context, CancellationToken ct = default)
    {
        if (context.EligibleUserIds.Count == 0) return null;

        var counts = await _db.Leads
            .Where(l => l.AgencyId == context.AgencyId && l.AssignedUserId != null && context.EligibleUserIds.Contains(l.AssignedUserId!.Value))
            .GroupBy(l => l.AssignedUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        return context.EligibleUserIds
            .OrderBy(uid => counts.GetValueOrDefault(uid, 0))
            .FirstOrDefault();
    }
}

public class LeastBusyStrategy : IAssignmentStrategy
{
    public string Name => "least-busy";
    private readonly IApplicationDbContext _db;

    public LeastBusyStrategy(IApplicationDbContext db) => _db = db;

    public async Task<Guid?> PickAsync(AssignmentContext context, CancellationToken ct = default)
    {
        if (context.EligibleUserIds.Count == 0) return null;

        var openCounts = await _db.Leads
            .Where(l => l.AgencyId == context.AgencyId
                && l.AssignedUserId != null
                && context.EligibleUserIds.Contains(l.AssignedUserId!.Value)
                && l.Stage != Domain.Enums.WorkflowStage.Lost
                && l.Stage != Domain.Enums.WorkflowStage.Funded)
            .GroupBy(l => l.AssignedUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        return context.EligibleUserIds
            .OrderBy(uid => openCounts.GetValueOrDefault(uid, 0))
            .FirstOrDefault();
    }
}

public class ManualStrategy : IAssignmentStrategy
{
    public string Name => "manual";
    public Task<Guid?> PickAsync(AssignmentContext context, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
}

public class AssignmentStrategyRegistry : IAssignmentStrategyRegistry
{
    private readonly Dictionary<string, IAssignmentStrategy> _byName;

    public AssignmentStrategyRegistry(IEnumerable<IAssignmentStrategy> strategies)
    {
        _byName = strategies.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IAssignmentStrategy Get(string name) =>
        _byName.TryGetValue(name, out var s) ? s
            : throw new InvalidOperationException($"Unknown assignment strategy '{name}'.");

    public IReadOnlyList<string> AvailableStrategies => _byName.Keys.ToList();
}

public class AssignmentService : IAssignmentService
{
    private readonly IAssignmentStrategyRegistry _registry;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;

    public AssignmentService(IAssignmentStrategyRegistry registry, UserManager<ApplicationUser> users, AppDbContext db)
    {
        _registry = registry;
        _users = users;
        _db = db;
    }

    public async Task<Guid?> AssignAsync(Lead lead, string targetRole, string strategyName, CancellationToken ct = default)
    {
        var roleUsers = await (from ur in _db.UserRoles
                               join r in _db.Roles on ur.RoleId equals r.Id
                               join u in _db.Users on ur.UserId equals u.Id
                               where r.Name == targetRole && u.AgencyId == lead.AgencyId && u.IsActive
                               select u.Id).ToListAsync(ct);

        var strategy = _registry.Get(strategyName);
        var pick = await strategy.PickAsync(new AssignmentContext(lead, lead.AgencyId, targetRole, roleUsers), ct);
        if (pick is { } uid) lead.AssignedUserId = uid;
        return pick;
    }
}
