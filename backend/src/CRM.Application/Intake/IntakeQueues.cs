using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Intake;

/// <summary>A lead as shown in the Verifier / Closer work queues.</summary>
public record IntakeQueueItem(
    Guid Id,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string? Email,
    string? State,
    string? City,
    string? MaritalStatus,
    int? AgeYears,
    WorkflowStage Stage,
    VerifierStatus VerifierStatus,
    bool HasApplication,
    DateTime CreatedAt);

/// <summary>Leads awaiting verification (fronted). Shown in the Verifier queue.</summary>
public record VerifierQueueQuery(int Take = 100) : IRequest<IReadOnlyList<IntakeQueueItem>>;

/// <summary>Leads verified and awaiting a closer. Shown in the Closer queue.</summary>
public record CloserQueueQuery(int Take = 100) : IRequest<IReadOnlyList<IntakeQueueItem>>;

public class IntakeQueueHandler :
    IRequestHandler<VerifierQueueQuery, IReadOnlyList<IntakeQueueItem>>,
    IRequestHandler<CloserQueueQuery, IReadOnlyList<IntakeQueueItem>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public IntakeQueueHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public Task<IReadOnlyList<IntakeQueueItem>> Handle(VerifierQueueQuery request, CancellationToken ct)
        => QueueAsync(WorkflowStage.Fronted, request.Take, ct);

    public Task<IReadOnlyList<IntakeQueueItem>> Handle(CloserQueueQuery request, CancellationToken ct)
        => QueueAsync(WorkflowStage.Verified, request.Take, ct);

    private async Task<IReadOnlyList<IntakeQueueItem>> QueueAsync(WorkflowStage stage, int take, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        return await _db.Leads
            .Where(l => l.AgencyId == _user.AgencyId && l.Stage == stage)
            .OrderBy(l => l.CreatedAt)
            .Take(Math.Min(take, 200))
            .Select(l => new IntakeQueueItem(
                l.Id, l.FirstName, l.LastName, l.PhoneNumber, l.Email, l.State, l.City,
                l.MaritalStatus, l.AgeYears, l.Stage, l.VerifierStatus,
                _db.LeadApplications.Any(a => a.LeadId == l.Id), l.CreatedAt))
            .ToListAsync(ct);
    }
}
