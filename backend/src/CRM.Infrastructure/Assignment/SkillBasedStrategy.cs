using CRM.Application.Common.Assignment;
using CRM.Domain.Common;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Assignment;

public class SkillBasedStrategy : IAssignmentStrategy
{
    public string Name => "skill-based";
    private readonly AppDbContext _db;

    public SkillBasedStrategy(AppDbContext db) => _db = Guard.AgainstNull(db);

    public async Task<Guid?> PickAsync(AssignmentContext context, CancellationToken ct = default)
    {
        Guard.AgainstNull(context);

        if (context.EligibleUserIds.Count == 0) return null;

        var requiredSkill = context.Lead.RequiredSkillCode;
        if (string.IsNullOrWhiteSpace(requiredSkill))
        {
            return context.EligibleUserIds.First();
        }

        var skill = await _db.Skills
            .FirstOrDefaultAsync(s => s.AgencyId == context.AgencyId && s.Code == requiredSkill, ct);
        if (skill is null)
            return context.EligibleUserIds.First();

        var skilled = await _db.AgentSkills
            .Where(a => a.SkillId == skill.Id && context.EligibleUserIds.Contains(a.UserId))
            .OrderByDescending(a => a.Proficiency)
            .Select(a => a.UserId)
            .ToListAsync(ct);

        if (skilled.Count == 0) return context.EligibleUserIds.First();

        var openCounts = await _db.Leads
            .Where(l => l.AgencyId == context.AgencyId
                && l.AssignedUserId != null
                && skilled.Contains(l.AssignedUserId!.Value)
                && l.Stage != Domain.Enums.WorkflowStage.Lost
                && l.Stage != Domain.Enums.WorkflowStage.Funded)
            .GroupBy(l => l.AssignedUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        return skilled.OrderBy(uid => openCounts.GetValueOrDefault(uid, 0)).First();
    }
}
