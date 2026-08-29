using CRM.Application.Admin;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using CcEntity = CRM.Domain.Entities.CallCenter;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Agencies;

// ─────────────────────────────────────────────────────────────────────────────
// Super-Admin Agency Panel operations + the roster reads the cross-agency
// Submission-Agent approval popup depends on. These are SuperAdmin-scoped (the
// existing agency-scoped call-center/user handlers can't serve a SuperAdmin, who
// has no agency of their own), mirroring the `?agencyId` precedent in OrgTree.
// ─────────────────────────────────────────────────────────────────────────────

public record AgencyOptionDto(Guid Id, string Name, bool IsActive);
public record LicenseAgentDto(Guid Id, string Name, string Email, bool IsActive);
/// <param name="PendingInvite">
/// True until the agent accepts the invitation (they still have a must-change temp password and
/// haven't signed in / enrolled in 2FA). Drives the "Awaiting sign-in" badge and gates Resend.
/// </param>
public record SubmissionAgentDto(Guid Id, string Name, string Email, bool IsActive, bool PendingInvite);

/// <summary>Lightweight agency list for the approval popup's Agency picker (SuperAdmin or a central Submission Agent).</summary>
public record ListAgencyOptionsQuery() : IRequest<IReadOnlyList<AgencyOptionDto>>;

/// <summary>License Agents of one agency — for the panel roster and the approval popup's dependent Agent picker.</summary>
public record ListAgencyLicenseAgentsQuery(Guid AgencyId) : IRequest<IReadOnlyList<LicenseAgentDto>>;

/// <summary>Call centres of one agency — lets a SuperAdmin scope the Call Centers page to a chosen agency.</summary>
public record ListAgencyCallCentersQuery(Guid AgencyId) : IRequest<IReadOnlyList<CallCenterDto>>;

/// <summary>SuperAdmin provisions an agency-level License Agent (reuses the shared invitation service).</summary>
public record CreateLicenseAgentCommand(Guid AgencyId, string Name, string Email) : IRequest<LicenseAgentDto>;

/// <summary>SuperAdmin creates a call center inside a target agency. Optionally invites a Call
/// Center Admin — leave the admin fields blank to just create the centre and assign people later.</summary>
public record CreateCallCenterInAgencyCommand(Guid AgencyId, string Name, string? Code, string? AdminName = null, string? AdminEmail = null)
    : IRequest<CallCenterDto>;

/// <summary>SuperAdmin edits a call center inside a target agency. Disabling it force-logs-out its agents.</summary>
public record UpdateCallCenterInAgencyCommand(Guid AgencyId, Guid CallCenterId, string Name, string? Code, bool IsActive)
    : IRequest<CallCenterDto>;

/// <summary>SuperAdmin lists the central (SMH-level) Submission Agents.</summary>
public record ListSubmissionAgentsQuery() : IRequest<IReadOnlyList<SubmissionAgentDto>>;

/// <summary>SuperAdmin provisions a central (cross-agency) Submission Agent — an SMH-level Validator with no agency.</summary>
public record CreateSubmissionAgentCommand(string Name, string Email) : IRequest<SubmissionAgentDto>;

public class CreateLicenseAgentValidator : AbstractValidator<CreateLicenseAgentCommand>
{
    public CreateLicenseAgentValidator()
    {
        RuleFor(x => x.AgencyId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
    }
}

public class CreateCallCenterInAgencyValidator : AbstractValidator<CreateCallCenterInAgencyCommand>
{
    public CreateCallCenterInAgencyValidator()
    {
        RuleFor(x => x.AgencyId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        // Admin invite is optional; validate the fields only when an email is supplied.
        When(x => !string.IsNullOrWhiteSpace(x.AdminEmail), () =>
        {
            RuleFor(x => x.AdminEmail).EmailAddress().MaximumLength(200);
            RuleFor(x => x.AdminName).MaximumLength(120);
        });
    }
}

public class UpdateCallCenterInAgencyValidator : AbstractValidator<UpdateCallCenterInAgencyCommand>
{
    public UpdateCallCenterInAgencyValidator()
    {
        RuleFor(x => x.AgencyId).NotEmpty();
        RuleFor(x => x.CallCenterId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class CreateSubmissionAgentValidator : AbstractValidator<CreateSubmissionAgentCommand>
{
    public CreateSubmissionAgentValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
    }
}

public class AgencyPanelHandler :
    IRequestHandler<ListAgencyOptionsQuery, IReadOnlyList<AgencyOptionDto>>,
    IRequestHandler<ListAgencyLicenseAgentsQuery, IReadOnlyList<LicenseAgentDto>>,
    IRequestHandler<ListAgencyCallCentersQuery, IReadOnlyList<CallCenterDto>>,
    IRequestHandler<CreateLicenseAgentCommand, LicenseAgentDto>,
    IRequestHandler<CreateCallCenterInAgencyCommand, CallCenterDto>,
    IRequestHandler<UpdateCallCenterInAgencyCommand, CallCenterDto>,
    IRequestHandler<ListSubmissionAgentsQuery, IReadOnlyList<SubmissionAgentDto>>,
    IRequestHandler<CreateSubmissionAgentCommand, SubmissionAgentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;
    private readonly IInvitationService _invitations;
    private readonly IJwtTokenService _jwt;
    private readonly ITenantLifecycleService _tenantLifecycle;

    public AgencyPanelHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity,
        IInvitationService invitations, IJwtTokenService jwt, ITenantLifecycleService tenantLifecycle)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
        _invitations = Guard.AgainstNull(invitations);
        _jwt = Guard.AgainstNull(jwt);
        _tenantLifecycle = Guard.AgainstNull(tenantLifecycle);
    }

    public async Task<IReadOnlyList<AgencyOptionDto>> Handle(ListAgencyOptionsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        // The Agency picker is used by SuperAdmin (panel/Team/Call Centers), central Submission
        // Agents (approval popup, any agency), and — since the popup is also available to agency
        // Admins/Validators approving their own sales — a plain agency user. The latter can't
        // reassign a sale across tenants, so return just their own agency (never throw here, or
        // the approval popup's Agency picker 403s for a legitimate agency validator).
        if (!IsSuperAdmin && !IsCentralValidator)
        {
            if (_user.AgencyId is not { } uaid || uaid == Guid.Empty)
                return Array.Empty<AgencyOptionDto>();
            return await _db.Agencies.AsNoTracking().IgnoreQueryFilters()
                .Where(a => a.Id == uaid && !a.IsDeleted)
                .Select(a => new AgencyOptionDto(a.Id, a.Name, a.IsActive))
                .ToListAsync(ct);
        }
        var q = _db.Agencies.AsNoTracking().IgnoreQueryFilters().Where(a => !a.IsDeleted);
        // SuperAdmin manages every agency, including deactivated ones (so they can view
        // or re-activate them). A central Submission Agent may only assign sales to a
        // currently-active agency, so they never see inactive ones.
        if (!IsSuperAdmin) q = q.Where(a => a.IsActive);
        return await q
            .OrderBy(a => a.IsActive ? 0 : 1)   // active first, inactive sink to the bottom
            .ThenBy(a => a.Name)
            .Select(a => new AgencyOptionDto(a.Id, a.Name, a.IsActive))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LicenseAgentDto>> Handle(ListAgencyLicenseAgentsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        // SuperAdmin (any agency), a central Submission Agent (any agency, for the popup), or a
        // member of the agency itself may read its License-Agent roster.
        if (!IsSuperAdmin && !IsCentralValidator && _user.AgencyId != request.AgencyId)
            throw new ForbiddenAccessException();

        var users = await _identity.ListUsersAsync(request.AgencyId, ct);
        return users
            .Where(u => u.Roles.Contains(DomainRoles.LicenseAgent, StringComparer.OrdinalIgnoreCase))
            .OrderBy(u => u.UserName)
            .Select(u => new LicenseAgentDto(u.Id, u.UserName, u.Email, u.IsActive))
            .ToList();
    }

    public async Task<IReadOnlyList<CallCenterDto>> Handle(ListAgencyCallCentersQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (!IsSuperAdmin && _user.AgencyId != request.AgencyId) throw new ForbiddenAccessException();
        return await _db.CallCenters.AsNoTracking().IgnoreQueryFilters()
            .Where(c => c.AgencyId == request.AgencyId && !c.IsDeleted)
            .OrderBy(c => c.Name)
            .Select(c => new CallCenterDto(c.Id, c.Name, c.Code, c.IsActive,
                _db.Leads.IgnoreQueryFilters().Count(l => l.CallCenterId == c.Id && !l.IsDeleted)))
            .ToListAsync(ct);
    }

    public async Task<LicenseAgentDto> Handle(CreateLicenseAgentCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdmin();
        var agency = await _db.Agencies.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == request.AgencyId && !a.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(Agency), request.AgencyId);

        await _invitations.EnsureEmailAvailableAsync(request.Email.Trim(), ct);
        var result = await _invitations.InviteAsync(new InvitationRequest(
            Email: request.Email.Trim(),
            FullName: request.Name.Trim(),
            AgencyId: agency.Id,
            CallCenterId: null,                          // agency-level: assignable across all call centres
            Roles: new[] { DomainRoles.LicenseAgent },
            AgencyName: agency.Name), ct);

        return new LicenseAgentDto(result.UserId, request.Name.Trim(), result.Email, IsActive: true);
    }

    public async Task<CallCenterDto> Handle(CreateCallCenterInAgencyCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdmin();
        var agency = await _db.Agencies.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == request.AgencyId && !a.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(Agency), request.AgencyId);

        var name = request.Name.Trim();
        // Name is unique within the target agency (SuperAdmin bypasses the tenant filter, so scope explicitly).
        if (await _db.CallCenters.IgnoreQueryFilters().AnyAsync(c => c.AgencyId == agency.Id && !c.IsDeleted && c.Name == name, ct))
            throw new ConflictException($"A call center named \"{name}\" already exists in this agency.");

        // Inviting a Call Center Admin is OPTIONAL — the centre can be created on its own and staffed
        // later (see "Assign people" on the agency panel). Only validate/invite when an email is given.
        var adminEmail = request.AdminEmail?.Trim();
        var inviteAdmin = !string.IsNullOrWhiteSpace(adminEmail);
        if (inviteAdmin)
            await _invitations.EnsureEmailAvailableAsync(adminEmail!, ct);

        var cc = new CcEntity { AgencyId = agency.Id, Name = name, Code = request.Code?.Trim(), IsActive = true };
        _db.CallCenters.Add(cc);
        await _db.SaveChangesAsync(ct);

        if (inviteAdmin)
            await _invitations.InviteAsync(new InvitationRequest(
                Email: adminEmail!,
                FullName: string.IsNullOrWhiteSpace(request.AdminName) ? adminEmail! : request.AdminName!.Trim(),
                AgencyId: agency.Id,
                CallCenterId: cc.Id,
                Roles: new[] { DomainRoles.CallCenterAdmin },
                AgencyName: agency.Name,
                CallCenterName: cc.Name), ct);

        return new CallCenterDto(cc.Id, cc.Name, cc.Code, cc.IsActive, 0);
    }

    public async Task<CallCenterDto> Handle(UpdateCallCenterInAgencyCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdmin();
        // Look up within the target agency (SuperAdmin bypasses the tenant filter, so scope explicitly).
        var cc = await _db.CallCenters.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == request.CallCenterId && c.AgencyId == request.AgencyId && !c.IsDeleted, ct)
            ?? throw new NotFoundException("CallCenter", request.CallCenterId);

        var name = request.Name.Trim();
        if (await _db.CallCenters.IgnoreQueryFilters()
                .AnyAsync(c => c.AgencyId == request.AgencyId && c.Id != cc.Id && !c.IsDeleted && c.Name == name, ct))
            throw new ConflictException($"A call center named \"{name}\" already exists in this agency.");

        var wasActive = cc.IsActive;
        cc.Name = name;
        cc.Code = request.Code?.Trim();
        await _db.SaveChangesAsync(ct);

        // Same lifecycle service the agency-scoped screen uses, so both paths cascade to the agents
        // pinned to this centre identically instead of drifting apart.
        if (wasActive != request.IsActive)
            await _tenantLifecycle.SetCallCenterActiveAsync(cc.Id, request.IsActive, ct);

        var leads = await _db.Leads.IgnoreQueryFilters().CountAsync(l => l.CallCenterId == cc.Id && !l.IsDeleted, ct);
        return new CallCenterDto(cc.Id, cc.Name, cc.Code, cc.IsActive, leads);
    }

    public async Task<IReadOnlyList<SubmissionAgentDto>> Handle(ListSubmissionAgentsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdmin();
        // Central Submission Agents are SMH-level: no agency (Guid.Empty) + the Validator role,
        // excluding the SuperAdmin(s) who also sit at that level.
        var users = await _identity.ListUsersAsync(Guid.Empty, ct);
        return users
            .Where(u => u.Roles.Contains(DomainRoles.Validator, StringComparer.OrdinalIgnoreCase)
                     && !u.Roles.Contains(DomainRoles.SuperAdmin, StringComparer.OrdinalIgnoreCase))
            .OrderBy(u => u.UserName)
            // MustChangePassword is our "hasn't accepted the invite yet" signal: it's set at
            // invite time and cleared the moment the agent chooses their own password on first login.
            .Select(u => new SubmissionAgentDto(u.Id, u.UserName, u.Email, u.IsActive, PendingInvite: u.MustChangePassword))
            .ToList();
    }

    public async Task<SubmissionAgentDto> Handle(CreateSubmissionAgentCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdmin();
        await _invitations.EnsureEmailAvailableAsync(request.Email.Trim(), ct);
        var result = await _invitations.InviteAsync(new InvitationRequest(
            Email: request.Email.Trim(),
            FullName: request.Name.Trim(),
            AgencyId: Guid.Empty,                        // SMH-level: cross-agency, bound to no tenant
            CallCenterId: null,
            Roles: new[] { DomainRoles.Validator }), ct);

        return new SubmissionAgentDto(result.UserId, request.Name.Trim(), result.Email, IsActive: true, PendingInvite: true);
    }

    private bool IsSuperAdmin => _user.Roles.Contains(DomainRoles.SuperAdmin);
    private bool IsCentralValidator => DomainRoles.IsCentralSubmissionAgent(_user.AgencyId, _user.Roles);

    private void EnsureSuperAdmin()
    {
        if (!IsSuperAdmin) throw new ForbiddenAccessException();
    }
}
