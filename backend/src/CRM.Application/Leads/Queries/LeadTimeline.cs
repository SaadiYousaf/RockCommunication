using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Queries;

public record LeadTimelineEntry(string Type, DateTime At, string? Actor, string? Description, IDictionary<string, object?>? Detail = null);

public record LeadTimelineQuery(Guid LeadId) : IRequest<LeadTimelineDto>;

public record LeadTimelineDto(
    Guid LeadId, string Name, WorkflowStage Stage, LeadDisposition Disposition,
    IReadOnlyList<LeadTimelineEntry> Entries);

public class LeadTimelineHandler : IRequestHandler<LeadTimelineQuery, LeadTimelineDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public LeadTimelineHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<LeadTimelineDto> Handle(LeadTimelineQuery request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        var idStr = lead.Id.ToString();
        var activities = await _db.LeadActivities
            .Where(a => a.LeadId == lead.Id)
            .OrderBy(a => a.OccurredAt)
            .ToListAsync(ct);
        var calls = await _db.CallRecords
            .Where(c => c.LeadId == lead.Id)
            .OrderBy(c => c.InitiatedAt)
            .ToListAsync(ct);
        var callbacks = await _db.ScheduledCallbacks
            .Where(c => c.LeadId == lead.Id)
            .OrderBy(c => c.ScheduledFor)
            .ToListAsync(ct);
        var audits = await _db.AuditEntries
            .Where(a => a.EntityName == nameof(Lead) && a.EntityId == idStr)
            .OrderBy(a => a.OccurredAt)
            .ToListAsync(ct);

        var entries = new List<LeadTimelineEntry>();
        entries.AddRange(activities.Select(a =>
            new LeadTimelineEntry("StageChange", a.OccurredAt, null,
                $"{a.FromStage} → {a.ToStage} ({a.Disposition})",
                new Dictionary<string, object?> { ["from"] = a.FromStage.ToString(), ["to"] = a.ToStage.ToString(), ["disposition"] = a.Disposition.ToString(), ["notes"] = a.Notes })));
        entries.AddRange(calls.Select(c =>
            new LeadTimelineEntry("Call", c.InitiatedAt, null,
                $"{c.Provider} call {c.Status}",
                new Dictionary<string, object?> { ["status"] = c.Status, ["recordingUrl"] = c.RecordingUrl })));
        entries.AddRange(callbacks.Select(c =>
            new LeadTimelineEntry("Callback", c.ScheduledFor, null,
                c.Completed ? "Completed callback" : "Scheduled callback",
                new Dictionary<string, object?> { ["reason"] = c.Reason, ["completed"] = c.Completed })));
        entries.AddRange(audits.Select(a =>
            new LeadTimelineEntry("Audit", a.OccurredAt, a.UserName,
                $"{a.Action} by {a.UserName ?? "system"}",
                new Dictionary<string, object?> { ["changes"] = a.Changes })));

        return new LeadTimelineDto(lead.Id, $"{lead.FirstName} {lead.LastName}", lead.Stage, lead.Disposition,
            entries.OrderBy(e => e.At).ToList());
    }
}
