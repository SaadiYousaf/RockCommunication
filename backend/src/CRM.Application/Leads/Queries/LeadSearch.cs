using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Leads.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Queries;

public record LeadSearchQuery(string? Phone, string? Email, string? Name, int Take = 50)
    : IRequest<IReadOnlyList<LeadDto>>;

public class LeadSearchHandler : IRequestHandler<LeadSearchQuery, IReadOnlyList<LeadDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public LeadSearchHandler(IApplicationDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<LeadDto>> Handle(LeadSearchQuery request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var q = _db.Leads.Where(l => l.AgencyId == _user.AgencyId);

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var normalized = new string(request.Phone.Where(char.IsDigit).ToArray());
            q = q.Where(l => l.PhoneNumber.Contains(normalized));
        }
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var em = request.Email.Trim().ToLower();
            q = q.Where(l => l.Email != null && l.Email.ToLower().Contains(em));
        }
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var n = request.Name.Trim().ToLower();
            q = q.Where(l => l.FirstName.ToLower().Contains(n) || l.LastName.ToLower().Contains(n));
        }

        return await q.OrderByDescending(l => l.CreatedAt)
            .Take(Math.Min(request.Take, 200))
            .Select(l => new LeadDto(l.Id, l.FirstName, l.LastName, l.PhoneNumber,
                l.Email, l.State, l.Stage, l.Disposition, l.AssignedUserId, l.TeamId, l.JornayaVerified, l.CreatedAt))
            .ToListAsync(ct);
    }
}

public record DuplicateGroup(string Key, IReadOnlyList<LeadDto> Leads);
public record DuplicateScanQuery() : IRequest<IReadOnlyList<DuplicateGroup>>;

public class DuplicateScanHandler : IRequestHandler<DuplicateScanQuery, IReadOnlyList<DuplicateGroup>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public DuplicateScanHandler(IApplicationDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<DuplicateGroup>> Handle(DuplicateScanQuery request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var leads = await _db.Leads
            .Where(l => l.AgencyId == _user.AgencyId)
            .Select(l => new LeadDto(l.Id, l.FirstName, l.LastName, l.PhoneNumber, l.Email, l.State,
                l.Stage, l.Disposition, l.AssignedUserId, l.TeamId, l.JornayaVerified, l.CreatedAt))
            .ToListAsync(ct);

        var groups = leads
            .Where(l => !string.IsNullOrEmpty(l.PhoneNumber))
            .GroupBy(l => l.PhoneNumber)
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroup($"phone:{g.Key}", g.ToList()))
            .ToList();

        return groups;
    }
}
