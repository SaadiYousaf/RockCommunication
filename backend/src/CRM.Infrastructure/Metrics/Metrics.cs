using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Metrics;
using CRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Metrics;

public abstract class MetricBase : IMetric
{
    protected readonly IApplicationDbContext Db;
    protected MetricBase(IApplicationDbContext db) => Db = db;
    public abstract string Key { get; }
    public abstract string Label { get; }
    public virtual string? Group => null;
    public abstract Task<MetricValue> CalculateAsync(MetricFilter filter, CancellationToken ct = default);
    protected MetricValue Result(decimal value, string? unit = null) => new(Key, Label, value, unit, Group);
}

public class TotalLeadsMetric : MetricBase
{
    public TotalLeadsMetric(IApplicationDbContext db) : base(db) { }
    public override string Key => "leads.total";
    public override string Label => "Total leads";
    public override string? Group => "Leads";

    public override async Task<MetricValue> CalculateAsync(MetricFilter f, CancellationToken ct = default)
    {
        var count = await Db.Leads
            .Where(l => l.AgencyId == f.AgencyId && l.CreatedAt >= f.From && l.CreatedAt < f.To)
            .Where(l => f.UserId == null || l.AssignedUserId == f.UserId)
            .Where(l => f.TeamId == null || l.TeamId == f.TeamId)
            .CountAsync(ct);
        return Result(count);
    }
}

public class FrontedLeadsMetric : MetricBase
{
    public FrontedLeadsMetric(IApplicationDbContext db) : base(db) { }
    public override string Key => "leads.fronted";
    public override string Label => "Fronted leads";
    public override string? Group => "Leads";

    public override async Task<MetricValue> CalculateAsync(MetricFilter f, CancellationToken ct = default)
    {
        var count = await Db.LeadActivities
            .Where(a => a.AgencyId == f.AgencyId && a.OccurredAt >= f.From && a.OccurredAt < f.To)
            .Where(a => a.ToStage == WorkflowStage.Fronted)
            .Where(a => f.UserId == null || a.UserId == f.UserId)
            .CountAsync(ct);
        return Result(count);
    }
}

public class ClosedSalesMetric : MetricBase
{
    public ClosedSalesMetric(IApplicationDbContext db) : base(db) { }
    public override string Key => "sales.closed";
    public override string Label => "Closed sales";
    public override string? Group => "Sales";

    public override async Task<MetricValue> CalculateAsync(MetricFilter f, CancellationToken ct = default)
    {
        var count = await Db.Sales
            .Where(s => s.AgencyId == f.AgencyId && s.SoldAt >= f.From && s.SoldAt < f.To)
            .Where(s => f.UserId == null || s.CloserUserId == f.UserId)
            .CountAsync(ct);
        return Result(count);
    }
}

public class TotalPremiumMetric : MetricBase
{
    public TotalPremiumMetric(IApplicationDbContext db) : base(db) { }
    public override string Key => "sales.premium.monthly";
    public override string Label => "Total monthly premium";
    public override string? Group => "Sales";

    public override async Task<MetricValue> CalculateAsync(MetricFilter f, CancellationToken ct = default)
    {
        var sum = await Db.Sales
            .Where(s => s.AgencyId == f.AgencyId && s.SoldAt >= f.From && s.SoldAt < f.To)
            .Where(s => f.UserId == null || s.CloserUserId == f.UserId)
            .SumAsync(s => (decimal?)s.MonthlyPremium, ct) ?? 0m;
        return Result(sum, "USD");
    }
}

public class ConversionRateMetric : MetricBase
{
    public ConversionRateMetric(IApplicationDbContext db) : base(db) { }
    public override string Key => "leads.conversion-rate";
    public override string Label => "Lead → sale conversion %";
    public override string? Group => "Leads";

    public override async Task<MetricValue> CalculateAsync(MetricFilter f, CancellationToken ct = default)
    {
        var totalLeads = await Db.Leads
            .Where(l => l.AgencyId == f.AgencyId && l.CreatedAt >= f.From && l.CreatedAt < f.To)
            .CountAsync(ct);
        if (totalLeads == 0) return Result(0, "%");
        var sales = await Db.Sales
            .Where(s => s.AgencyId == f.AgencyId && s.SoldAt >= f.From && s.SoldAt < f.To)
            .CountAsync(ct);
        return Result(Math.Round((decimal)sales / totalLeads * 100m, 2), "%");
    }
}

public class FundedSalesMetric : MetricBase
{
    public FundedSalesMetric(IApplicationDbContext db) : base(db) { }
    public override string Key => "sales.funded";
    public override string Label => "Funded sales";
    public override string? Group => "Sales";

    public override async Task<MetricValue> CalculateAsync(MetricFilter f, CancellationToken ct = default)
    {
        var count = await Db.Sales
            .Where(s => s.AgencyId == f.AgencyId && s.FundedAt != null && s.FundedAt >= f.From && s.FundedAt < f.To)
            .CountAsync(ct);
        return Result(count);
    }
}

public class DashboardService : IDashboardService
{
    private readonly IReadOnlyList<IMetric> _metrics;
    public DashboardService(IEnumerable<IMetric> metrics) => _metrics = metrics.ToList();

    public IReadOnlyList<(string Key, string Label, string? Group)> AvailableMetrics =>
        _metrics.Select(m => (m.Key, m.Label, m.Group)).ToList();

    public async Task<IReadOnlyList<MetricValue>> ComputeAsync(MetricFilter filter, IEnumerable<string>? metricKeys = null, CancellationToken ct = default)
    {
        var keys = metricKeys?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pickedMetrics = keys is null ? _metrics : _metrics.Where(m => keys.Contains(m.Key));
        var results = new List<MetricValue>();
        foreach (var m in pickedMetrics)
            results.Add(await m.CalculateAsync(filter, ct));
        return results;
    }
}
