using CRM.Application.Common.Compliance;
using CRM.Application.Common.Scoring;
using CRM.Domain.Entities;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Scoring;

public class JornayaVerifiedRule : IScoringRule
{
    public string Name => "jornaya-verified";
    public int Priority => 10;
    public Task<(int, string?)> ScoreAsync(ScoringContext ctx, CancellationToken ct = default) =>
        Task.FromResult(ctx.Lead.JornayaVerified ? (20, "Jornaya verified") : (0, (string?)null));
}

public class HasEmailRule : IScoringRule
{
    public string Name => "has-email";
    public int Priority => 20;
    public Task<(int, string?)> ScoreAsync(ScoringContext ctx, CancellationToken ct = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(ctx.Lead.Email) ? (10, "Email captured") : (0, (string?)null));
}

public class ConsentCapturedRule : IScoringRule
{
    public string Name => "consent-captured";
    public int Priority => 30;
    public Task<(int, string?)> ScoreAsync(ScoringContext ctx, CancellationToken ct = default) =>
        Task.FromResult(ctx.Lead.ConsentCaptured ? (15, "TCPA consent captured") : (0, (string?)null));
}

public class HighValueStateRule : IScoringRule
{
    private static readonly HashSet<string> Premium = new(StringComparer.OrdinalIgnoreCase)
    {
        "TX", "FL", "GA", "NC", "AZ", "NV", "TN"
    };
    public string Name => "high-value-state";
    public int Priority => 40;
    public Task<(int, string?)> ScoreAsync(ScoringContext ctx, CancellationToken ct = default) =>
        Task.FromResult(ctx.Lead.State is not null && Premium.Contains(ctx.Lead.State)
            ? (10, $"Premium state {ctx.Lead.State}") : (0, (string?)null));
}

public class DncDeductionRule : IScoringRule
{
    private readonly IDncChecker _dnc;
    public DncDeductionRule(IDncChecker dnc) => _dnc = dnc;
    public string Name => "dnc-deduction";
    public int Priority => 1;

    public async Task<(int, string?)> ScoreAsync(ScoringContext ctx, CancellationToken ct = default)
    {
        var blocked = await _dnc.IsBlockedAsync(ctx.Lead.AgencyId, ctx.Lead.PhoneNumber, ct);
        return blocked ? (-100, "On DNC list") : (0, null);
    }
}

public class CustomScoringRule : IScoringRule
{
    public string Name => "custom-config";
    public int Priority => 100;
    public Task<(int, string?)> ScoreAsync(ScoringContext ctx, CancellationToken ct = default) => Task.FromResult((0, (string?)null));
}

public class LeadScorer : ILeadScorer
{
    private readonly IEnumerable<IScoringRule> _rules;
    private readonly AppDbContext _db;

    public LeadScorer(IEnumerable<IScoringRule> rules, AppDbContext db)
    {
        _rules = rules.OrderBy(r => r.Priority).ToList();
        _db = db;
    }

    public async Task<ScoringResult> ScoreAsync(Lead lead, CancellationToken ct = default)
    {
        var ctx = new ScoringContext(lead, BuildFacts(lead));
        var breakdown = new List<(string, int, string?)>();
        var total = 0;
        foreach (var rule in _rules)
        {
            var (pts, note) = await rule.ScoreAsync(ctx, ct);
            if (pts != 0) breakdown.Add((rule.Name, pts, note));
            total += pts;
        }

        var customRules = await _db.LeadScoringRules
            .AsNoTracking()
            .Where(r => r.AgencyId == lead.AgencyId && r.IsActive)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        foreach (var cr in customRules)
        {
            if (!ctx.Facts.TryGetValue(cr.FactKey, out var actual)) continue;
            if (Match(cr.ComparisonOp, actual, cr.CompareValue))
            {
                breakdown.Add((cr.Name, cr.Points, $"{cr.FactKey} {cr.ComparisonOp} {cr.CompareValue}"));
                total += cr.Points;
            }
        }

        return new ScoringResult(Math.Max(0, total), breakdown);
    }

    private static IReadOnlyDictionary<string, object?> BuildFacts(Lead lead) => new Dictionary<string, object?>
    {
        ["state"] = lead.State, ["source"] = lead.Source,
        ["jornayaVerified"] = lead.JornayaVerified,
        ["consentCaptured"] = lead.ConsentCaptured,
        ["hasEmail"] = !string.IsNullOrEmpty(lead.Email),
        ["campaignId"] = lead.CampaignId,
        ["leadSourceId"] = lead.LeadSourceId,
    };

    private static bool Match(string op, object? actual, string? expected)
    {
        var a = actual?.ToString() ?? "";
        var e = expected ?? "";
        return op.ToLowerInvariant() switch
        {
            "eq" => string.Equals(a, e, StringComparison.OrdinalIgnoreCase),
            "ne" => !string.Equals(a, e, StringComparison.OrdinalIgnoreCase),
            "contains" => a.Contains(e, StringComparison.OrdinalIgnoreCase),
            "true" => actual is bool b && b,
            "false" => actual is bool b2 && !b2,
            _ => false
        };
    }
}
