using CRM.Application.Auth.Dtos;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Org;

public record OrgPersonDto(
    Guid Id,
    string UserName,
    string Email,
    string? DisplayName,
    IReadOnlyList<string> Roles,
    bool IsActive);

public record OrgTeamDto(
    Guid Id,
    string Name,
    string? Vertical,
    OrgPersonDto? Lead,
    IReadOnlyList<OrgPersonDto> Members);

public record OrgTreeDto(
    Guid AgencyId,
    string AgencyName,
    OrgPersonDto? Ceo,
    IReadOnlyList<OrgPersonDto> Leadership,
    IReadOnlyList<OrgTeamDto> Teams,
    IReadOnlyList<OrgPersonDto> Unassigned);

public record GetOrgTreeQuery(Guid? AgencyId = null) : IRequest<OrgTreeDto>;

public class OrgTreeHandler : IRequestHandler<GetOrgTreeQuery, OrgTreeDto>
{
    // Roles considered "leadership" — they sit between the CEO and the team-lead layer.
    private static readonly string[] LeadershipRoles =
    {
        DomainRoles.QAManager,
        DomainRoles.ProjectManager,
        DomainRoles.TechLead,
        DomainRoles.ProgramManager,
    };

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;

    public OrgTreeHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
    }

    public async Task<OrgTreeDto> Handle(GetOrgTreeQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var isSuperAdmin = _user.Roles.Contains(DomainRoles.SuperAdmin);

        // Resolve which agency we're rendering. SuperAdmin can target any; everyone else
        // is implicitly scoped to their own.
        Guid agencyId;
        if (isSuperAdmin && request.AgencyId is { } requested)
            agencyId = requested;
        else if (_user.AgencyId is { } own && own != Guid.Empty)
            agencyId = own;
        else
            throw new ForbiddenAccessException();

        var agency = await _db.Agencies.FirstOrDefaultAsync(a => a.Id == agencyId, ct)
            ?? throw new NotFoundException("Agency", agencyId);

        var users = await _identity.ListUsersAsync(agencyId, ct);

        OrgPersonDto Person(UserSummaryDto u) =>
            new(u.Id, u.UserName, u.Email, null, u.Roles, true);

        var ceo = users.FirstOrDefault(u => u.Roles.Contains(DomainRoles.CEO));

        // Leadership = anyone holding a leadership role and NOT the CEO. We keep the same
        // user even if they wear multiple hats here; the UI groups by user, not by role.
        var leadership = users
            .Where(u => u != ceo && u.Roles.Any(r => LeadershipRoles.Contains(r)))
            .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
            .Select(Person)
            .ToList();
        var leadershipIds = leadership.Select(p => p.Id).ToHashSet();

        var teams = await _db.Teams
            .Where(t => t.AgencyId == agencyId)
            .OrderBy(t => t.Name)
            .Select(t => new { t.Id, t.Name, t.Vertical, t.TeamLeadUserId })
            .ToListAsync(ct);

        // Bucket the rank-and-file by their TeamId (already on the DTO).
        var membersByTeam = users
            .Where(u => u.Id != ceo?.Id && !leadershipIds.Contains(u.Id))
            .GroupBy(u => u.TeamId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.ToList());

        var teamDtos = teams.Select(t =>
        {
            var lead = t.TeamLeadUserId is { } leadId
                ? users.FirstOrDefault(u => u.Id == leadId) is { } l ? Person(l) : null
                : null;
            var members = membersByTeam.TryGetValue(t.Id, out var list)
                ? list
                    .Where(u => u.Id != t.TeamLeadUserId)
                    .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
                    .Select(Person)
                    .ToList()
                : new List<OrgPersonDto>();
            return new OrgTeamDto(t.Id, t.Name, t.Vertical, lead, members);
        }).ToList();

        var unassigned = membersByTeam.TryGetValue(Guid.Empty, out var ungrouped)
            ? ungrouped.OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase).Select(Person).ToList()
            : new List<OrgPersonDto>();

        return new OrgTreeDto(
            agency.Id,
            agency.Name,
            ceo is null ? null : Person(ceo),
            leadership,
            teamDtos,
            unassigned);
    }
}
