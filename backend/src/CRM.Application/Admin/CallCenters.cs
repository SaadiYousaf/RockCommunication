using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
// Disambiguate from the CRM.Application.CallCenter (telephony) namespace.
using CcEntity = CRM.Domain.Entities.CallCenter;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Admin;

public record CallCenterDto(Guid Id, string Name, string? Code, bool IsActive, int LeadCount,
    /// <summary>Owning agency — set so a SuperAdmin's cross-agency list can be grouped/filtered.</summary>
    Guid AgencyId = default, string? AgencyName = null,
    // Site details. Optional so existing centres keep working; shown on the detail/edit screens.
    string? Phone = null, string? Address = null, string? City = null,
    string? TimeZone = null, int? SeatCapacity = null);

public record ListCallCentersQuery() : IRequest<IReadOnlyList<CallCenterDto>>;
/// <summary>
/// Creates a call center AND provisions its Call Center Admin in one step — the same
/// onboarding contract as agency creation. Admin name + email are mandatory. Extra call
/// center fields can be appended here without changing the handler's control flow.
/// </summary>
public record CreateCallCenterCommand(
    string Name, string? Code, string AdminName, string AdminEmail,
    string? Phone = null, string? Address = null, string? City = null,
    string? TimeZone = null, int? SeatCapacity = null) : IRequest<CallCenterDto>;
public record UpdateCallCenterCommand(
    Guid Id, string Name, string? Code, bool IsActive,
    string? Phone = null, string? Address = null, string? City = null,
    string? TimeZone = null, int? SeatCapacity = null) : IRequest<CallCenterDto>;

public class CreateCallCenterValidator : AbstractValidator<CreateCallCenterCommand>
{
    public CreateCallCenterValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AdminName).NotEmpty().MaximumLength(120)
            .WithMessage("A Call Center Admin name is required.");
        RuleFor(x => x.AdminEmail).NotEmpty().EmailAddress().MaximumLength(200)
            .WithMessage("A valid Call Center Admin email is required.");
        // Upper bounds only — a blank optional field must stay valid.
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.City).MaximumLength(120);
        RuleFor(x => x.TimeZone).MaximumLength(60);
        RuleFor(x => x.SeatCapacity).GreaterThan(0).When(x => x.SeatCapacity is not null)
            .WithMessage("Seats must be a positive number.");
    }
}

public class UpdateCallCenterValidator : AbstractValidator<UpdateCallCenterCommand>
{
    public UpdateCallCenterValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

/// <summary>
/// CRUD for the call centers within the caller's agency. CallCenter is a <see cref="TenantEntity"/>,
/// so the global query filter already scopes every read to the caller's agency; these handlers add
/// tenant-context guards as a backstop. Only agency-level roles reach here (controller [HasPermission]).
/// </summary>
public class CallCenterHandler :
    IRequestHandler<ListCallCentersQuery, IReadOnlyList<CallCenterDto>>,
    IRequestHandler<CreateCallCenterCommand, CallCenterDto>,
    IRequestHandler<UpdateCallCenterCommand, CallCenterDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IInvitationService _invitations;
    private readonly IJwtTokenService _jwt;

    public CallCenterHandler(IApplicationDbContext db, ICurrentUser user, IInvitationService invitations, IJwtTokenService jwt)
    { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _invitations = Guard.AgainstNull(invitations); _jwt = Guard.AgainstNull(jwt); }

    public async Task<IReadOnlyList<CallCenterDto>> Handle(ListCallCentersQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);

        // A SuperAdmin has no agency of their own. They used to get a 403 here, which silently left
        // every call-centre picker EMPTY for them (User Management showed only "Agency-level"). They
        // legitimately see across tenants, so return every agency's centres, labelled with the owning
        // agency so the UI can scope each picker to the right one.
        var superAdminUnscoped = _user.IsSuperAdmin && (_user.AgencyId is null || _user.AgencyId == Guid.Empty);

        var q = _db.CallCenters.AsQueryable();
        if (!superAdminUnscoped)
        {
            if (_user.AgencyId is null || _user.AgencyId == Guid.Empty) throw new ForbiddenAccessException();
            // Explicit agency scope as a backstop: SuperAdmin bypasses the global tenant filter, so
            // without this an accidental SuperAdmin caller would see every agency's call centers.
            var own = _user.AgencyId.Value;
            q = q.Where(c => c.AgencyId == own);
        }
        else
        {
            q = q.IgnoreQueryFilters().Where(c => !c.IsDeleted);
        }

        var rows = await q.OrderBy(c => c.Name)
            .Select(c => new CallCenterDto(
                c.Id, c.Name, c.Code, c.IsActive,
                _db.Leads.Count(l => l.CallCenterId == c.Id),
                c.AgencyId, null,
                c.Phone, c.Address, c.City, c.TimeZone, c.SeatCapacity))
            .ToListAsync(ct);

        // Resolve agency names only for the cross-agency case, where the UI needs to disambiguate
        // two centres that share a name.
        if (!superAdminUnscoped) return rows;

        var agencyIds = rows.Select(r => r.AgencyId).Distinct().ToList();
        var names = await _db.Agencies.AsNoTracking().IgnoreQueryFilters()
            .Where(a => agencyIds.Contains(a.Id) && !a.IsDeleted)
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        return rows.Select(r => r with { AgencyName = names.TryGetValue(r.AgencyId, out var n) ? n : null }).ToList();
    }

    public async Task<CallCenterDto> Handle(CreateCallCenterCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        // A SuperAdmin has no agency (Guid.Empty); this agency-scoped page can't create/list
        // for them without a real agency. Fail clearly instead of hitting a FK constraint.
        if (_user.AgencyId is null || _user.AgencyId == Guid.Empty)
            throw new ForbiddenAccessException("Super Admins manage call centres from the Agency panel — open an agency (Agencies) and use \"New call centre\".");
        var name = request.Name.Trim();
        // Name is unique WITHIN the agency — the query filter already scopes this check.
        if (await _db.CallCenters.AnyAsync(c => c.Name == name, ct))
            throw new ConflictException($"A call center named \"{name}\" already exists.");

        // Fail fast before inserting: a duplicate admin email must not leave an
        // admin-less call center behind.
        await _invitations.EnsureEmailAvailableAsync(request.AdminEmail.Trim(), ct);

        var cc = new CcEntity
        {
            AgencyId = _user.AgencyId.Value,
            Name = name,
            Code = request.Code?.Trim(),
            IsActive = true,
            Phone = Blank(request.Phone),
            Address = Blank(request.Address),
            City = Blank(request.City),
            TimeZone = Blank(request.TimeZone),
            SeatCapacity = request.SeatCapacity,
        };
        _db.CallCenters.Add(cc);
        await _db.SaveChangesAsync(ct);

        // Same onboarding contract as the Agency CEO — one shared service, no duplicate logic.
        // The admin is pinned to this call center, which is what scopes everything they can see.
        var agencyName = await _db.Agencies.Where(a => a.Id == cc.AgencyId)
            .Select(a => a.Name).FirstOrDefaultAsync(ct);
        await _invitations.InviteAsync(new InvitationRequest(
            Email: request.AdminEmail.Trim(),
            FullName: request.AdminName.Trim(),
            AgencyId: cc.AgencyId,
            CallCenterId: cc.Id,
            Roles: new[] { DomainRoles.CallCenterAdmin },
            AgencyName: agencyName,
            CallCenterName: cc.Name), ct);

        return new CallCenterDto(cc.Id, cc.Name, cc.Code, cc.IsActive, 0, cc.AgencyId, agencyName,
            cc.Phone, cc.Address, cc.City, cc.TimeZone, cc.SeatCapacity);
    }

    /// <summary>Treat an all-whitespace optional field as "not provided" rather than storing "  ".</summary>
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public async Task<CallCenterDto> Handle(UpdateCallCenterCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        // A SuperAdmin has no agency (Guid.Empty); this agency-scoped page can't create/list
        // for them without a real agency. Fail clearly instead of hitting a FK constraint.
        if (_user.AgencyId is null || _user.AgencyId == Guid.Empty)
            throw new ForbiddenAccessException("Super Admins manage call centres from the Agency panel — open an agency (Agencies) and use \"New call centre\".");
        var cc = await _db.CallCenters.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException("CallCenter", request.Id);
        var wasActive = cc.IsActive;
        cc.Name = request.Name.Trim();
        cc.Code = request.Code?.Trim();
        cc.IsActive = request.IsActive;
        cc.Phone = Blank(request.Phone);
        cc.Address = Blank(request.Address);
        cc.City = Blank(request.City);
        cc.TimeZone = Blank(request.TimeZone);
        cc.SeatCapacity = request.SeatCapacity;
        await _db.SaveChangesAsync(ct);

        // Disabling a call center force-logs-out every agent pinned to it (login/refresh are
        // already blocked by TenantLoginGate), so they're locked out until it's re-enabled.
        if (wasActive && !request.IsActive)
            await _jwt.RevokeAllForCallCenterAsync(cc.Id, ct);

        var leads = await _db.Leads.CountAsync(l => l.CallCenterId == cc.Id, ct);
        return new CallCenterDto(cc.Id, cc.Name, cc.Code, cc.IsActive, leads, cc.AgencyId, null,
            cc.Phone, cc.Address, cc.City, cc.TimeZone, cc.SeatCapacity);
    }
}
