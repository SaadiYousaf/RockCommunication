using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Metrics;
using CRM.Domain.Common;
using MediatR;

namespace CRM.Application.Dashboard;

public record DashboardQuery(DateTime From, DateTime To, IReadOnlyList<string>? MetricKeys = null, Guid? UserId = null, Guid? TeamId = null)
    : IRequest<IReadOnlyList<MetricValue>>;

public class DashboardHandler : IRequestHandler<DashboardQuery, IReadOnlyList<MetricValue>>
{
    private readonly IDashboardService _service;
    private readonly ICurrentUser _user;

    public DashboardHandler(IDashboardService service, ICurrentUser user)
    {
        _service = Guard.AgainstNull(service);
        _user = Guard.AgainstNull(user);
    }

    public Task<IReadOnlyList<MetricValue>> Handle(DashboardQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var filter = new MetricFilter(_user.AgencyId.Value, request.From, request.To, request.UserId, request.TeamId);
        return _service.ComputeAsync(filter, request.MetricKeys, ct);
    }
}

public record DashboardMetricCatalogQuery() : IRequest<IReadOnlyList<MetricCatalogItem>>;
public record MetricCatalogItem(string Key, string Label, string? Group);

public class DashboardMetricCatalogHandler : IRequestHandler<DashboardMetricCatalogQuery, IReadOnlyList<MetricCatalogItem>>
{
    private readonly IDashboardService _service;
    public DashboardMetricCatalogHandler(IDashboardService service) => _service = Guard.AgainstNull(service);

    public Task<IReadOnlyList<MetricCatalogItem>> Handle(DashboardMetricCatalogQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        IReadOnlyList<MetricCatalogItem> items = _service.AvailableMetrics
            .Select(m => new MetricCatalogItem(m.Key, m.Label, m.Group)).ToList();
        return Task.FromResult(items);
    }
}
