using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Metrics;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Metrics;

public class AverageHandleTimeMetric : MetricBase
{
    public AverageHandleTimeMetric(IApplicationDbContext db) : base(db) { }
    public override string Key => "calls.aht-seconds";
    public override string Label => "Average handle time";
    public override string? Group => "Calls";

    public override async Task<MetricValue> CalculateAsync(MetricFilter f, CancellationToken ct = default)
    {
        var calls = await Db.CallRecords
            .Where(c => c.AgencyId == f.AgencyId && c.AnsweredAt != null && c.EndedAt != null
                && c.InitiatedAt >= f.From && c.InitiatedAt < f.To)
            .Where(c => f.UserId == null || c.AgentUserId == f.UserId)
            .Select(c => new { c.AnsweredAt, c.EndedAt })
            .ToListAsync(ct);
        if (calls.Count == 0) return Result(0, "s");
        var avgSeconds = calls.Average(c => (c.EndedAt!.Value - c.AnsweredAt!.Value).TotalSeconds);
        return Result((decimal)Math.Round(avgSeconds, 1), "s");
    }
}

public class AnswerRateMetric : MetricBase
{
    public AnswerRateMetric(IApplicationDbContext db) : base(db) { }
    public override string Key => "calls.answer-rate";
    public override string Label => "Answer rate";
    public override string? Group => "Calls";

    public override async Task<MetricValue> CalculateAsync(MetricFilter f, CancellationToken ct = default)
    {
        var total = await Db.CallRecords
            .Where(c => c.AgencyId == f.AgencyId && c.InitiatedAt >= f.From && c.InitiatedAt < f.To)
            .Where(c => f.UserId == null || c.AgentUserId == f.UserId)
            .CountAsync(ct);
        if (total == 0) return Result(0, "%");
        var answered = await Db.CallRecords
            .Where(c => c.AgencyId == f.AgencyId && c.AnsweredAt != null
                && c.InitiatedAt >= f.From && c.InitiatedAt < f.To)
            .Where(c => f.UserId == null || c.AgentUserId == f.UserId)
            .CountAsync(ct);
        return Result(Math.Round((decimal)answered / total * 100m, 2), "%");
    }
}

public class AbandonRateMetric : MetricBase
{
    public AbandonRateMetric(IApplicationDbContext db) : base(db) { }
    public override string Key => "calls.abandon-rate";
    public override string Label => "Abandon rate";
    public override string? Group => "Calls";

    public override async Task<MetricValue> CalculateAsync(MetricFilter f, CancellationToken ct = default)
    {
        var inbound = await Db.CallRecords
            .Where(c => c.AgencyId == f.AgencyId && c.Direction == "Inbound"
                && c.InitiatedAt >= f.From && c.InitiatedAt < f.To)
            .CountAsync(ct);
        if (inbound == 0) return Result(0, "%");
        var abandoned = await Db.CallRecords
            .Where(c => c.AgencyId == f.AgencyId && c.Direction == "Inbound"
                && c.Status == "abandoned"
                && c.InitiatedAt >= f.From && c.InitiatedAt < f.To)
            .CountAsync(ct);
        return Result(Math.Round((decimal)abandoned / inbound * 100m, 2), "%");
    }
}

public class ServiceLevelMetric : MetricBase
{
    private const int ThresholdSeconds = 20;

    public ServiceLevelMetric(IApplicationDbContext db) : base(db) { }
    public override string Key => "calls.sl-20s";
    public override string Label => "Answered ≤ 20s";
    public override string? Group => "Calls";

    public override async Task<MetricValue> CalculateAsync(MetricFilter f, CancellationToken ct = default)
    {
        var calls = await Db.CallRecords
            .Where(c => c.AgencyId == f.AgencyId && c.Direction == "Inbound" && c.AnsweredAt != null
                && c.InitiatedAt >= f.From && c.InitiatedAt < f.To)
            .Select(c => new { c.InitiatedAt, c.AnsweredAt })
            .ToListAsync(ct);
        if (calls.Count == 0) return Result(0, "%");
        var fast = calls.Count(c => (c.AnsweredAt!.Value - c.InitiatedAt).TotalSeconds <= ThresholdSeconds);
        return Result(Math.Round((decimal)fast / calls.Count * 100m, 2), "%");
    }
}

public class OccupancyMetric : MetricBase
{
    public OccupancyMetric(IApplicationDbContext db) : base(db) { }
    public override string Key => "agents.occupancy";
    public override string Label => "Agent occupancy (on-call ÷ available)";
    public override string? Group => "Agents";

    public override async Task<MetricValue> CalculateAsync(MetricFilter f, CancellationToken ct = default)
    {
        var sessions = await Db.AgentSessions
            .Where(s => s.AgencyId == f.AgencyId && s.ClockInAt < f.To
                && (s.ClockOutAt ?? DateTime.UtcNow) >= f.From)
            .Where(s => f.UserId == null || s.UserId == f.UserId)
            .Select(s => new { s.TotalAvailable, s.TotalOnCall })
            .ToListAsync(ct);
        if (sessions.Count == 0) return Result(0, "%");
        var available = sessions.Sum(s => s.TotalAvailable.TotalSeconds);
        var onCall = sessions.Sum(s => s.TotalOnCall.TotalSeconds);
        if (available + onCall == 0) return Result(0, "%");
        return Result((decimal)Math.Round(onCall / (available + onCall) * 100, 2), "%");
    }
}

public class CostPerLeadMetric : MetricBase
{
    public CostPerLeadMetric(IApplicationDbContext db) : base(db) { }
    public override string Key => "leads.cost-per-lead";
    public override string Label => "Cost per lead (avg)";
    public override string? Group => "Leads";

    public override async Task<MetricValue> CalculateAsync(MetricFilter f, CancellationToken ct = default)
    {
        var rows = await (from l in Db.Leads
                          join s in Db.LeadSources on l.LeadSourceId equals s.Id
                          where l.AgencyId == f.AgencyId && l.CreatedAt >= f.From && l.CreatedAt < f.To
                          select s.CostPerLead).ToListAsync(ct);
        if (rows.Count == 0) return Result(0, "USD");
        return Result(Math.Round(rows.Average(), 2), "USD");
    }
}
