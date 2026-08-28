using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Metrics;
using CRM.Domain.Common;
using MediatR;

namespace CRM.Application.Dashboard;

// AgencyId is honored ONLY for SuperAdmin (who has no agency of their own and must target one to
// see KPIs); tenant users are always scoped to their own agency and the value is ignored.
public record DashboardQuery(DateTime From, DateTime To, IReadOnlyList<string>? MetricKeys = null, Guid? UserId = null, Guid? TeamId = null, Guid? AgencyId = null)
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
        // SuperAdmin has no agency of their own, so they must target one explicitly to see its KPIs.
        Guid agencyId;
        if (_user.IsSuperAdmin)
        {
            // Prefer an explicit ?agencyId, but fall back to the agency this session is scoped to
            // (POST /api/auth/context) before refusing — otherwise a SuperAdmin who has already
            // picked a working context gets a 403 the page can only render as empty KPIs.
            var target =
                request.AgencyId is { } aid && aid != Guid.Empty ? aid
                : _user.AgencyId is { } scoped && scoped != Guid.Empty ? scoped
                : Guid.Empty;
            if (target == Guid.Empty)
                throw new ForbiddenAccessException("Select an agency to view its KPIs.");
            agencyId = target;
        }
        else
        {
            if (_user.AgencyId is not { } uaid || uaid == Guid.Empty) throw new ForbiddenAccessException();
            agencyId = uaid;
        }
        var filter = new MetricFilter(agencyId, request.From, request.To, request.UserId, request.TeamId);
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
