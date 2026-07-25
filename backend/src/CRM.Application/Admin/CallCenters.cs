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

public record CallCenterDto(Guid Id, string Name, string? Code, bool IsActive, int LeadCount);

public record ListCallCentersQuery() : IRequest<IReadOnlyList<CallCenterDto>>;
/// <summary>
/// Creates a call center AND provisions its Call Center Admin in one step — the same
/// onboarding contract as agency creation. Admin name + email are mandatory. Extra call
/// center fields can be appended here without changing the handler's control flow.
/// </summary>
public record CreateCallCenterCommand(string Name, string? Code, string AdminName, string AdminEmail) : IRequest<CallCenterDto>;
public record UpdateCallCenterCommand(Guid Id, string Name, string? Code, bool IsActive) : IRequest<CallCenterDto>;

public class CreateCallCenterValidator : AbstractValidator<CreateCallCenterCommand>
{
    public CreateCallCenterValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AdminName).NotEmpty().MaximumLength(120)
            .WithMessage("A Call Center Admin name is required.");
        RuleFor(x => x.AdminEmail).NotEmpty().EmailAddress().MaximumLength(200)
            .WithMessage("A valid Call Center Admin email is required.");
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

    public CallCenterHandler(IApplicationDbContext db, ICurrentUser user, IInvitationService invitations)
    { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _invitations = Guard.AgainstNull(invitations); }

    public async Task<IReadOnlyList<CallCenterDto>> Handle(ListCallCentersQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        // A SuperAdmin has no agency (Guid.Empty); this agency-scoped page can't create/list
        // for them without a real agency. Fail clearly instead of hitting a FK constraint.
        if (_user.AgencyId is null || _user.AgencyId == Guid.Empty)
            throw new ForbiddenAccessException("Super Admins manage call centres from the Agency panel — open an agency (Agencies) and use \"New call centre\".");
        // Explicit agency scope as a backstop: SuperAdmin bypasses the global tenant filter, so
        // without this an accidental SuperAdmin caller would see every agency's call centers.
        return await _db.CallCenters
            .Where(c => c.AgencyId == _user.AgencyId.Value)
            .OrderBy(c => c.Name)
            .Select(c => new CallCenterDto(
                c.Id, c.Name, c.Code, c.IsActive,
                _db.Leads.Count(l => l.CallCenterId == c.Id)))
            .ToListAsync(ct);
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
            IsActive = true
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

        return new CallCenterDto(cc.Id, cc.Name, cc.Code, cc.IsActive, 0);
    }

    public async Task<CallCenterDto> Handle(UpdateCallCenterCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        // A SuperAdmin has no agency (Guid.Empty); this agency-scoped page can't create/list
        // for them without a real agency. Fail clearly instead of hitting a FK constraint.
        if (_user.AgencyId is null || _user.AgencyId == Guid.Empty)
            throw new ForbiddenAccessException("Super Admins manage call centres from the Agency panel — open an agency (Agencies) and use \"New call centre\".");
        var cc = await _db.CallCenters.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException("CallCenter", request.Id);
        cc.Name = request.Name.Trim();
        cc.Code = request.Code?.Trim();
        cc.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        var leads = await _db.Leads.CountAsync(l => l.CallCenterId == cc.Id, ct);
        return new CallCenterDto(cc.Id, cc.Name, cc.Code, cc.IsActive, leads);
    }
}
