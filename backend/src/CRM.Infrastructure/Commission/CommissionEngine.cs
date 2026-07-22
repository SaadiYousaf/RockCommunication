using CRM.Application.Common.Commission;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Commission;

public abstract class ConfigurableCommissionRule : ICommissionRule
{
    private readonly IAgencyCommissionConfigProvider _config;
    protected ConfigurableCommissionRule(IAgencyCommissionConfigProvider config) => _config = Guard.AgainstNull(config);

    public abstract string Name { get; }
    public abstract int Priority { get; }
    protected abstract decimal DefaultAmount { get; }
    protected virtual decimal? DefaultThreshold => null;
    protected virtual string TargetRole => string.Empty;

    public async Task<IReadOnlyList<CommissionLine>> CalculateAsync(CommissionContext ctx, CancellationToken ct = default)
    {
        Guard.AgainstNull(ctx);

        if (!string.IsNullOrEmpty(TargetRole) && ctx.AgentRole != TargetRole)
            return Array.Empty<CommissionLine>();

        AgencyCommissionRule? cfg = null;
        if (ctx.AgencyId is { } aid)
            cfg = await _config.GetAsync(aid, Name, ct);

        if (cfg is { Enabled: false }) return Array.Empty<CommissionLine>();

        var amount = cfg?.Amount ?? DefaultAmount;
        var threshold = cfg?.Threshold ?? DefaultThreshold;

        if (!ShouldApply(ctx, threshold)) return Array.Empty<CommissionLine>();

        return new[] { new CommissionLine(Name, ctx.AgentId, amount, BuildNote(ctx, amount, threshold)) };
    }

    protected virtual bool ShouldApply(CommissionContext ctx, decimal? threshold) => true;
    protected virtual string BuildNote(CommissionContext ctx, decimal amount, decimal? threshold) => Name;
}

public class CloserFlatRateRule : ConfigurableCommissionRule
{
    public CloserFlatRateRule(IAgencyCommissionConfigProvider c) : base(c) { }
    public override string Name => "closer-flat-rate";
    public override int Priority => 100;
    protected override decimal DefaultAmount => 75m;
    protected override string TargetRole => Roles.Closer;
    protected override string BuildNote(CommissionContext ctx, decimal amount, decimal? threshold) => "Flat closer rate";
}

public class JrCloserSplitRule : ConfigurableCommissionRule
{
    public JrCloserSplitRule(IAgencyCommissionConfigProvider c) : base(c) { }
    public override string Name => "jr-closer-split";
    public override int Priority => 110;
    protected override decimal DefaultAmount => 30m;
    protected override string TargetRole => Roles.JrCloser;
    protected override string BuildNote(CommissionContext ctx, decimal amount, decimal? threshold) => "Jr closer assist split";
}

public class ValidatorBonusRule : ConfigurableCommissionRule
{
    public ValidatorBonusRule(IAgencyCommissionConfigProvider c) : base(c) { }
    public override string Name => "validator-bonus";
    public override int Priority => 120;
    protected override decimal DefaultAmount => 15m;
    protected override string TargetRole => Roles.Validator;

    protected override bool ShouldApply(CommissionContext ctx, decimal? threshold) => !ctx.Sale.IsInternalSale;
    protected override string BuildNote(CommissionContext ctx, decimal amount, decimal? threshold) => "Validation bonus";
}

public class HighPremiumKickerRule : ConfigurableCommissionRule
{
    public HighPremiumKickerRule(IAgencyCommissionConfigProvider c) : base(c) { }
    public override string Name => "high-premium-kicker";
    public override int Priority => 200;
    protected override decimal DefaultAmount => 25m;
    protected override decimal? DefaultThreshold => 200m;
    protected override string TargetRole => Roles.Closer;

    protected override bool ShouldApply(CommissionContext ctx, decimal? threshold) =>
        ctx.Sale.MonthlyPremium >= (threshold ?? DefaultThreshold ?? decimal.MaxValue);
    protected override string BuildNote(CommissionContext ctx, decimal amount, decimal? threshold) =>
        $"Premium kicker for ${ctx.Sale.MonthlyPremium}/mo";
}

/// <summary>
/// Pays a flat override to the team lead of the closer's team for every closed sale.
/// Configurable per agency via the same `agency_commission_config` table (rule_name = team-lead-override).
/// </summary>
public class TeamLeadOverrideRule : ICommissionRule
{
    public string Name => "team-lead-override";
    public int Priority => 300;
    private readonly IAgencyCommissionConfigProvider _config;
    private readonly AppDbContext _db;

    public TeamLeadOverrideRule(IAgencyCommissionConfigProvider config, AppDbContext db)
    {
        _config = Guard.AgainstNull(config); _db = Guard.AgainstNull(db);
    }

    public async Task<IReadOnlyList<CommissionLine>> CalculateAsync(CommissionContext ctx, CancellationToken ct = default)
    {
        Guard.AgainstNull(ctx);

        if (ctx.AgentRole != Roles.Closer) return Array.Empty<CommissionLine>();

        var closer = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == ctx.AgentId, ct);
        if (closer?.TeamId is null) return Array.Empty<CommissionLine>();

        var team = await _db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == closer.TeamId, ct);
        if (team?.TeamLeadUserId is null) return Array.Empty<CommissionLine>();

        var cfg = ctx.AgencyId is { } aid ? await _config.GetAsync(aid, Name, ct) : null;
        if (cfg is { Enabled: false }) return Array.Empty<CommissionLine>();
        var amount = cfg?.Amount ?? 10m;

        return new[]
        {
            new CommissionLine(Name, team.TeamLeadUserId.Value, amount,
                $"Team-lead override for sale by {closer.UserName}")
        };
    }
}

public class CommissionEngine : ICommissionEngine
{
    private readonly IEnumerable<ICommissionRule> _rules;
    private readonly AppDbContext _db;

    public CommissionEngine(IEnumerable<ICommissionRule> rules, AppDbContext db)
    {
        _rules = Guard.AgainstNull(rules).OrderBy(r => r.Priority).ToList();
        _db = Guard.AgainstNull(db);
    }

    public async Task<IReadOnlyList<CommissionLine>> CalculateForSaleAsync(Sale sale, CancellationToken ct = default)
    {
        Guard.AgainstNull(sale);

        var lines = new List<CommissionLine>();
        var participants = await GetParticipantsAsync(sale, ct);

        foreach (var (agentId, role) in participants)
        {
            var ctx = new CommissionContext(sale, agentId, role, sale.AgencyId);
            foreach (var rule in _rules)
                lines.AddRange(await rule.CalculateAsync(ctx, ct));
        }

        return lines;
    }

    private async Task<IReadOnlyList<(Guid agentId, string role)>> GetParticipantsAsync(Sale sale, CancellationToken ct)
    {
        var ids = new List<Guid> { sale.CloserUserId };
        if (sale.ValidatorUserId is { } vid) ids.Add(vid);

        var jrCloser = await _db.LeadActivities.AsNoTracking()
            .Where(a => a.LeadId == sale.LeadId && a.ToStage == WorkflowStage.JrClosed)
            .Select(a => a.UserId).FirstOrDefaultAsync(ct);
        if (jrCloser != Guid.Empty) ids.Add(jrCloser);

        var roleByUser = await (from ur in _db.UserRoles
                                join r in _db.Roles on ur.RoleId equals r.Id
                                where ids.Contains(ur.UserId)
                                select new { ur.UserId, RoleName = r.Name! })
                                .ToListAsync(ct);

        return roleByUser.Select(x => (x.UserId, x.RoleName)).ToList();
    }
}
