using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Scoring;
using CRM.Application.Leads.Commands;
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
        _db = db; _user = user; _scorer = scorer; _identity = identity;
    }

    public async Task<LeadDetailDto> Handle(GetLeadDetailQuery request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var lead = await _db.Leads
            .FirstOrDefaultAsync(l => l.Id == request.Id && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.Id);

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

        var users = await _identity.ListUsersAsync(_user.AgencyId, ct);
        var byId = users.ToDictionary(u => u.Id);
        string? AssignedName(Guid? id) => id is null ? null
            : byId.TryGetValue(id.Value, out var u) ? u.UserName : null;

        var scoring = await _scorer.ScoreAsync(lead, ct);
        var breakdown = scoring.Breakdown
            .Select(b => new LeadScoreLineDto(b.Rule, b.Points, b.Note)).ToList();

        var age = lead.DateOfBirth is null ? (int?)null
            : (DateTime.UtcNow.Year - lead.DateOfBirth.Value.Year
               - (DateTime.UtcNow.DayOfYear < lead.DateOfBirth.Value.DayOfYear ? 1 : 0));

        return new LeadDetailDto(
            lead.Id,
            lead.FirstName, lead.LastName, $"{lead.FirstName} {lead.LastName}".Trim(),
            lead.PhoneNumber, lead.Email,
            lead.Address, lead.City, lead.State, lead.PostalCode,
            lead.DateOfBirth, age,
            lead.Stage, lead.Disposition,
            lead.Source, lead.JornayaLeadId, lead.JornayaVerified,
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
    public UpdateLeadNotesHandler(IApplicationDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<Unit> Handle(UpdateLeadNotesCommand request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.Id && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.Id);
        lead.Notes = request.Notes;
        lead.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
