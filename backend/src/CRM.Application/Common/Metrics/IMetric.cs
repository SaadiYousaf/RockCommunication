namespace CRM.Application.Common.Metrics;

public record MetricFilter(Guid AgencyId, DateTime From, DateTime To, Guid? UserId = null, Guid? TeamId = null);

public record MetricValue(string Key, string Label, decimal Value, string? Unit = null, string? Group = null);

public interface IMetric
{
    string Key { get; }
    string Label { get; }
    string? Group { get; }
    Task<MetricValue> CalculateAsync(MetricFilter filter, CancellationToken ct = default);
}

public interface IDashboardService
{
    Task<IReadOnlyList<MetricValue>> ComputeAsync(MetricFilter filter, IEnumerable<string>? metricKeys = null, CancellationToken ct = default);
    IReadOnlyList<(string Key, string Label, string? Group)> AvailableMetrics { get; }
}
