using CRM.Api.Authorization;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Metrics;
using CRM.Application.Dashboard;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using CRM.Infrastructure.Identity;
using CRM.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[HasPermission(Permissions.DashboardView)]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly AppDbContext _db;
    public DashboardController(IMediator mediator, AppDbContext db) { _mediator = Guard.AgainstNull(mediator); _db = Guard.AgainstNull(db); }

    public record StageBucket(string Stage, int Count);
    public record ActivityItem(Guid LeadId, string LeadName, string FromStage, string ToStage, string? Notes, string Disposition, DateTime OccurredAt, string? UserName);
    public record SummaryResponse(
        int ActiveLeads,
        int OpenCallbacks,
        decimal SalesThisWeek,
        decimal ConversionRate,
        int LeadsLast7Days,
        int LeadsPrior7Days,
        decimal SalesPrior7Days,
        IReadOnlyList<StageBucket> Pipeline,
        IReadOnlyList<ActivityItem> RecentActivity);

    [HttpGet("summary")]
    public async Task<ActionResult<SummaryResponse>> Summary(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var weekAgo  = now.AddDays(-7);
        var twoWksAgo = now.AddDays(-14);

        var activeStages = new[] { WorkflowStage.New, WorkflowStage.Fronted, WorkflowStage.Verified,
                                   WorkflowStage.JrClosed, WorkflowStage.Closed, WorkflowStage.Validated,
                                   WorkflowStage.Followup };

        var activeLeads   = await _db.Leads.CountAsync(l => activeStages.Contains(l.Stage), ct);
        var openCallbacks = await _db.ScheduledCallbacks.CountAsync(c => !c.Completed, ct);

        // SQLite stores `decimal` as TEXT, and a SQL-side SUM over that column throws — which was
        // 500-ing the whole dashboard for any tenant that had sales. Pull the values and sum in
        // memory (the row counts here are small; mirrors the same guard used in ListSales).
        var salesThisWeek  = (await _db.Sales.Where(s => s.SoldAt >= weekAgo)
            .Select(s => s.MonthlyPremium).ToListAsync(ct)).Sum();
        var salesPriorWeek = (await _db.Sales.Where(s => s.SoldAt >= twoWksAgo && s.SoldAt < weekAgo)
            .Select(s => s.MonthlyPremium).ToListAsync(ct)).Sum();

        var leadsLast7  = await _db.Leads.CountAsync(l => l.CreatedAt >= weekAgo, ct);
        var leadsPrior7 = await _db.Leads.CountAsync(l => l.CreatedAt >= twoWksAgo && l.CreatedAt < weekAgo, ct);

        var totalLeads = await _db.Leads.CountAsync(ct);
        var soldLeads  = await _db.Leads.CountAsync(l => l.Stage == WorkflowStage.Funded || l.Stage == WorkflowStage.Validated || l.Stage == WorkflowStage.Closed, ct);
        var conversion = totalLeads == 0 ? 0m : Math.Round((decimal)soldLeads * 100m / totalLeads, 1);

        var pipelineRaw = await _db.Leads.GroupBy(l => l.Stage)
            .Select(g => new { Stage = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var pipeline = pipelineRaw.Select(x => new StageBucket(x.Stage.ToString(), x.Count))
            .OrderBy(x => Array.IndexOf(Enum.GetValues<WorkflowStage>(), Enum.Parse<WorkflowStage>(x.Stage)))
            .ToList();

        var activitiesRaw = await (
            from a in _db.LeadActivities
            join l in _db.Leads on a.LeadId equals l.Id
            orderby a.OccurredAt descending
            select new { a.LeadId, a.UserId, l.FirstName, l.LastName, a.FromStage, a.ToStage, a.Notes, a.Disposition, a.OccurredAt }
        ).Take(10).ToListAsync(ct);

        var userIds = activitiesRaw.Select(x => x.UserId).Distinct().ToList();
        var userMap = await _db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.DisplayName ?? u.UserName! })
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var activity = activitiesRaw.Select(x => new ActivityItem(
            x.LeadId, $"{x.FirstName} {x.LastName}",
            x.FromStage.ToString(), x.ToStage.ToString(),
            x.Notes, x.Disposition.ToString(), x.OccurredAt,
            userMap.TryGetValue(x.UserId, out var uname) ? uname : null
        )).ToList();

        return Ok(new SummaryResponse(
            activeLeads, openCallbacks, salesThisWeek, conversion,
            leadsLast7, leadsPrior7, salesPriorWeek, pipeline, activity));
    }

    [HttpGet("metrics")]
    public async Task<ActionResult<IReadOnlyList<MetricCatalogItem>>> Catalog(CancellationToken ct)
        => Ok(await _mediator.Send(new DashboardMetricCatalogQuery(), ct));

    /// <summary>Upcoming events for the "what's coming up" widget (own callbacks + HR birthdays/trainings).</summary>
    [HttpGet("upcoming-events")]
    public async Task<ActionResult<IReadOnlyList<UpcomingEventDto>>> UpcomingEvents([FromQuery] int days = 14, CancellationToken ct = default)
        => Ok(await _mediator.Send(new UpcomingEventsQuery(days), ct));

    /// <summary>Team-status widget: every employee in the viewer's call centre with today's attendance
    /// and live work state. The handler gates to HR + supervisory/management and scopes per role.</summary>
    [HttpGet("team-status")]
    public async Task<ActionResult<IReadOnlyList<TeamStatusRowDto>>> TeamStatus([FromQuery] Guid? callCenterId = null, CancellationToken ct = default)
        => Ok(await _mediator.Send(new TeamStatusQuery(callCenterId), ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MetricValue>>> Compute(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string[]? metrics, [FromQuery] Guid? userId, [FromQuery] Guid? teamId,
        [FromQuery] Guid? agencyId = null,   // SuperAdmin only; ignored for tenant-scoped callers
        CancellationToken ct = default)
    {
        var f = from ?? DateTime.UtcNow.AddDays(-30);
        var t = to ?? DateTime.UtcNow.AddDays(1);
        var keys = metrics is { Length: > 0 } ? metrics : null;
        return Ok(await _mediator.Send(new DashboardQuery(f, t, keys, userId, teamId, agencyId), ct));
    }
}
