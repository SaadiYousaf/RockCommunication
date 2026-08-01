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
    public override string Name => CommissionRuleNames.CloserFlatRate;
    public override int Priority => 100;
    protected override decimal DefaultAmount => 75m;
    protected override string TargetRole => Roles.Closer;
    protected override string BuildNote(CommissionContext ctx, decimal amount, decimal? threshold) => "Flat closer rate";
}

public class JrCloserSplitRule : ConfigurableCommissionRule
{
    public JrCloserSplitRule(IAgencyCommissionConfigProvider c) : base(c) { }
    public override string Name => CommissionRuleNames.JrCloserSplit;
    public override int Priority => 110;
    protected override decimal DefaultAmount => 30m;
    protected override string TargetRole => Roles.JrCloser;
    protected override string BuildNote(CommissionContext ctx, decimal amount, decimal? threshold) => "Jr closer assist split";
}

public class ValidatorBonusRule : ConfigurableCommissionRule
{
    public ValidatorBonusRule(IAgencyCommissionConfigProvider c) : base(c) { }
    public override string Name => CommissionRuleNames.ValidatorBonus;
    public override int Priority => 120;
    protected override decimal DefaultAmount => 15m;
    protected override string TargetRole => Roles.Validator;

    protected override bool ShouldApply(CommissionContext ctx, decimal? threshold) => !ctx.Sale.IsInternalSale;
    protected override string BuildNote(CommissionContext ctx, decimal amount, decimal? threshold) => "Validation bonus";
}

/// <summary>
/// Pays the agency-level License Agent assigned to a sale by a Submission Agent at approval.
/// Configurable per agency (rule_name = license-agent-approval) exactly like the other rules.
/// </summary>
public class LicenseAgentApprovalRule : ConfigurableCommissionRule
{
    public LicenseAgentApprovalRule(IAgencyCommissionConfigProvider c) : base(c) { }
    public override string Name => CommissionRuleNames.LicenseAgentApproval;
    public override int Priority => 130;
    protected override decimal DefaultAmount => 50m;
    protected override string TargetRole => Roles.LicenseAgent;

    protected override bool ShouldApply(CommissionContext ctx, decimal? threshold) => !ctx.Sale.IsInternalSale;
    protected override string BuildNote(CommissionContext ctx, decimal amount, decimal? threshold) => "License agent approval";
}

public class HighPremiumKickerRule : ConfigurableCommissionRule
{
    public HighPremiumKickerRule(IAgencyCommissionConfigProvider c) : base(c) { }
    public override string Name => CommissionRuleNames.HighPremiumKicker;
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
    public string Name => CommissionRuleNames.TeamLeadOverride;
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

    public async Task<IReadOnlyList<CommissionLine>> CalculateForAgentAsync(Sale sale, Guid agentId, string role, CancellationToken ct = default)
    {
        Guard.AgainstNull(sale);

        var ctx = new CommissionContext(sale, agentId, role, sale.AgencyId);
        var lines = new List<CommissionLine>();
        foreach (var rule in _rules)
            lines.AddRange(await rule.CalculateAsync(ctx, ct));
        return lines;
    }

    private async Task<IReadOnlyList<(Guid agentId, string role)>> GetParticipantsAsync(Sale sale, CancellationToken ct)
    {
        // Tag each participant with the FUNCTION they performed on this sale, not every identity
        // role they happen to hold. The old UserRoles fan-out paid, e.g., a ValidatorBonus to a
        // closer who merely also holds the Validator role (double-count / phantom lines), and
        // dropped a participant who had no UserRoles row at all.
        var participants = new List<(Guid agentId, string role)> { (sale.CloserUserId, Roles.Closer) };
        if (sale.ValidatorUserId is { } vid) participants.Add((vid, Roles.Validator));

        var jrCloser = await _db.LeadActivities.AsNoTracking()
            .Where(a => a.LeadId == sale.LeadId && a.ToStage == WorkflowStage.JrClosed)
            .Select(a => a.UserId).FirstOrDefaultAsync(ct);
        if (jrCloser != Guid.Empty) participants.Add((jrCloser, Roles.JrCloser));

        return participants;
    }
}
