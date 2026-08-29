using CRM.Application.Queues;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Scoring;
using CRM.Application.Leads.Commands;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Queries;

public record LeadDetailDto(
    Guid Id,
    string FirstName, string LastName, string FullName,
    string PhoneNumber, string? Email,
    string? Address, string? City, string? State, string? PostalCode,
    DateTime? DateOfBirth, int? Age,
    WorkflowStage Stage, LeadDisposition Disposition,
    string? Source, string? JornayaLeadId, bool JornayaVerified,
    string? JornayaVerifiedBy, DateTime? JornayaVerifiedAt,
    Guid? AssignedUserId, string? AssignedUserName,
    Guid? TeamId, Guid? CampaignId, Guid? LeadSourceId, Guid? VerticalId,
    string? RequiredSkillCode, bool ConsentCaptured,
    int Score,
    IReadOnlyList<LeadScoreLineDto> ScoreBreakdown,
    string? Notes,
    DateTime CreatedAt, DateTime? UpdatedAt,
    LeadSaleSummaryDto? Sale,
    int CallCount, int OpenCallbackCount,
    IReadOnlyList<RecentCallDto> RecentCalls,
    IReadOnlyList<RecentCallbackDto> Callbacks);

public record LeadSaleSummaryDto(Guid SaleId, string Carrier, string? PolicyNumber,
    decimal MonthlyPremium, decimal AnnualPremium, DateTime SoldAt,
    DateTime? ValidatedAt, DateTime? FundedAt, bool IsInternalSale);

public record RecentCallDto(Guid Id, string Direction, string Status,
    DateTime InitiatedAt, DateTime? AnsweredAt, DateTime? EndedAt,
    string? RecordingUrl, string? WrapUpCode, string? Notes);

public record RecentCallbackDto(Guid Id, DateTime ScheduledFor, string? Reason,
    Guid AssignedUserId, string? AssignedUserName, bool Completed);

public record GetLeadDetailQuery(Guid Id) : IRequest<LeadDetailDto>;

public class GetLeadDetailHandler : IRequestHandler<GetLeadDetailQuery, LeadDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ILeadScorer _scorer;
    private readonly IIdentityService _identity;

    public GetLeadDetailHandler(IApplicationDbContext db, ICurrentUser user,
        ILeadScorer scorer, IIdentityService identity)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _scorer = Guard.AgainstNull(scorer); _identity = Guard.AgainstNull(identity);
    }

    /// <summary>Mine, or unclaimed in a pool my role works.</summary>
    private bool CanReach(Lead lead)
    {
        if (lead.AssignedUserId == _user.UserId) return true;
        if (lead.AssignedUserId is not null) return false;
        return LeadStagePolicy.QueueOwnerRole(lead.Stage) is { } owner
            && LeadQueuePredicates.PoolsFor(_user.Roles, seesEverything: false).Contains(lead.Stage)
            && owner is not null;
    }

    public async Task<LeadDetailDto> Handle(GetLeadDetailQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        // A SuperAdmin who hasn't picked a working context has no agency of their own, so comparing
        // AgencyId to theirs matched nothing and every lead came back 404. ConfinedTo returns null
        // for that caller (they legitimately span agencies) and throws for a non-SuperAdmin with a
        // malformed token, so this stays fail-closed for everyone else.
        var scope = TenantScope.ConfinedTo(_user);

        var lead = await _db.Leads
            .FirstOrDefaultAsync(l => l.Id == request.Id && (scope == null || l.AgencyId == scope), ct)
            ?? throw new NotFoundException(nameof(Lead), request.Id);

        // Front-line agents may open a lead that is theirs, OR one sitting unclaimed in a queue
        // their role works — otherwise clicking a name in Available Leads would report the lead as
        // not found, which is exactly the confusion this whole change exists to remove. Managers see
        // any lead in their call centre. Still NotFound rather than Forbidden, so we never confirm
        // the existence of a lead the caller has no business knowing about.
        if (!AccessScope.SeesAllRecords(_user.Roles) && !CanReach(lead))
            throw new NotFoundException(nameof(Lead), request.Id);

        var sale = await _db.Sales.AsNoTracking()
            .FirstOrDefaultAsync(s => s.LeadId == lead.Id, ct);

        var recentCalls = await _db.CallRecords.AsNoTracking()
            .Where(c => c.LeadId == lead.Id)
            .OrderByDescending(c => c.InitiatedAt).Take(10)
            .Select(c => new RecentCallDto(c.Id, c.Direction, c.Status,
                c.InitiatedAt, c.AnsweredAt, c.EndedAt,
                c.RecordingUrl, c.WrapUpCode, c.Notes))
            .ToListAsync(ct);

        var callCount = await _db.CallRecords.AsNoTracking().CountAsync(c => c.LeadId == lead.Id, ct);

        var callbacks = await _db.ScheduledCallbacks.AsNoTracking()
            .Where(cb => cb.LeadId == lead.Id)
            .OrderBy(cb => cb.ScheduledFor)
            .Select(cb => new { cb.Id, cb.ScheduledFor, cb.Reason, cb.AssignedUserId, cb.Completed })
            .ToListAsync(ct);
        var openCallbacks = callbacks.Count(cb => !cb.Completed);

        var users = await _identity.ListUsersAsync(lead.AgencyId, ct);
        var byId = users.ToDictionary(u => u.Id);
        string? AssignedName(Guid? id) => id is null ? null
            : byId.TryGetValue(id.Value, out var u) ? u.UserName : null;

        var scoring = await _scorer.ScoreAsync(lead, ct);
        var breakdown = scoring.Breakdown
            .Select(b => new LeadScoreLineDto(b.Rule, b.Points, b.Note)).ToList();

        // Persist the freshly-computed score if the stored (denormalized) value has drifted — e.g.
        // a scoring input changed via a path that didn't rescore. This self-heals the value the
        // leads list/queue read, and guarantees the header score below always matches its breakdown.
        if (scoring.Score != lead.Score)
        {
            lead.Score = scoring.Score;
            await _db.SaveChangesAsync(ct);
        }

        // Exact calendar age — DayOfYear comparison is off by a day across leap-year boundaries.
        int? age = null;
        if (lead.DateOfBirth is { } dob)
        {
            var today = DateTime.UtcNow.Date;
            age = today.Year - dob.Date.Year;
            if (dob.Date > today.AddYears(-age.Value)) age--;
        }

        return new LeadDetailDto(
            lead.Id,
            lead.FirstName, lead.LastName, $"{lead.FirstName} {lead.LastName}".Trim(),
            lead.PhoneNumber, lead.Email,
            lead.Address, lead.City, lead.State, lead.PostalCode,
            lead.DateOfBirth, age,
            lead.Stage, lead.Disposition,
            lead.Source, lead.JornayaLeadId, lead.JornayaVerified,
            lead.JornayaVerifiedBy, lead.JornayaVerifiedAt,
            lead.AssignedUserId, AssignedName(lead.AssignedUserId),
            lead.TeamId, lead.CampaignId, lead.LeadSourceId, lead.VerticalId,
            lead.RequiredSkillCode, lead.ConsentCaptured,
            lead.Score,
            breakdown,
            lead.Notes,
            lead.CreatedAt, lead.UpdatedAt,
            sale is null ? null : new LeadSaleSummaryDto(
                sale.Id, sale.Carrier, sale.PolicyNumber,
                sale.MonthlyPremium, sale.AnnualPremium, sale.SoldAt,
                sale.ValidatedAt, sale.FundedAt, sale.IsInternalSale),
            callCount, openCallbacks,
            recentCalls,
            callbacks.Select(cb => new RecentCallbackDto(
                cb.Id, cb.ScheduledFor, cb.Reason, cb.AssignedUserId,
                AssignedName(cb.AssignedUserId), cb.Completed)).ToList());
    }
}

public record UpdateLeadNotesCommand(Guid Id, string? Notes) : IRequest<Unit>;

public class UpdateLeadNotesHandler : IRequestHandler<UpdateLeadNotesCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public UpdateLeadNotesHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<Unit> Handle(UpdateLeadNotesCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var scope = TenantScope.ConfinedTo(_user);
        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.Id && (scope == null || l.AgencyId == scope), ct)
            ?? throw new NotFoundException(nameof(Lead), request.Id);
        lead.Notes = request.Notes;
        lead.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
