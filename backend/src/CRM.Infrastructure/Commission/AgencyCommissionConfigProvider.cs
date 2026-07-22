using CRM.Application.Common.Commission;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Commission;

public class AgencyCommissionConfigProvider : IAgencyCommissionConfigProvider
{
    private readonly AppDbContext _db;
    public AgencyCommissionConfigProvider(AppDbContext db) => _db = Guard.AgainstNull(db);

    public async Task<AgencyCommissionRule?> GetAsync(Guid agencyId, string ruleName, CancellationToken ct = default)
    {
        var entry = await _db.AgencyCommissionConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.AgencyId == agencyId && c.RuleName == ruleName, ct);
        return entry is null ? null : new(entry.RuleName, entry.Amount, entry.Threshold, entry.Enabled);
    }

    public async Task<IReadOnlyList<AgencyCommissionRule>> GetAllAsync(Guid agencyId, CancellationToken ct = default)
    {
        return await _db.AgencyCommissionConfigs.AsNoTracking()
            .Where(c => c.AgencyId == agencyId)
            .Select(c => new AgencyCommissionRule(c.RuleName, c.Amount, c.Threshold, c.Enabled))
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(Guid agencyId, AgencyCommissionRule rule, CancellationToken ct = default)
    {
        Guard.AgainstNull(rule);

        var existing = await _db.AgencyCommissionConfigs
            .FirstOrDefaultAsync(c => c.AgencyId == agencyId && c.RuleName == rule.RuleName, ct);

        if (existing is null)
        {
            _db.AgencyCommissionConfigs.Add(new AgencyCommissionConfig
            {
                AgencyId = agencyId,
                RuleName = rule.RuleName,
                Amount = rule.Amount,
                Threshold = rule.Threshold,
                Enabled = rule.Enabled
            });
        }
        else
        {
            existing.Amount = rule.Amount;
            existing.Threshold = rule.Threshold;
            existing.Enabled = rule.Enabled;
        }
        await _db.SaveChangesAsync(ct);
    }
}
