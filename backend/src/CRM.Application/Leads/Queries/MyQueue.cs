using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Leads.Dtos;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Queries;

public record MyQueueQuery(WorkflowStage? Stage = null, int Take = 50) : IRequest<IReadOnlyList<LeadDto>>;

public class MyQueueHandler : IRequestHandler<MyQueueQuery, IReadOnlyList<LeadDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public MyQueueHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    public async Task<IReadOnlyList<LeadDto>> Handle(MyQueueQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();

        var q = _db.Leads.Where(l => l.AgencyId == _user.AgencyId && l.AssignedUserId == _user.UserId);
        if (request.Stage is { } s) q = q.Where(l => l.Stage == s);

        return await q.OrderBy(l => l.CreatedAt)
            .Take(Math.Min(request.Take, 200))
            .Select(l => new LeadDto(l.Id, l.FirstName, l.LastName, l.PhoneNumber, l.Email, l.State,
                l.Stage, l.Disposition, l.AssignedUserId, l.TeamId, l.JornayaVerified, l.CreatedAt))
            .ToListAsync(ct);
    }
}
