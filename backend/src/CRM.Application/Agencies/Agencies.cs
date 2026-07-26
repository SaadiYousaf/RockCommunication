using CRM.Application.Common.Exceptions;
using CRM.Domain.Seeding;
using CRM.Application.Common.Interfaces;
using CRM.Application.Users.Commands;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Agencies;

public record AgencyDto(
    Guid Id,
    string Name,
    string? Code,
    bool IsActive,
    Guid? CeoUserId,
    string? CeoUserName,
    int UserCount,
    DateTime CreatedAt);

public record ListAgenciesQuery(bool IncludeInactive = false) : IRequest<IReadOnlyList<AgencyDto>>;
public record GetAgencyQuery(Guid Id) : IRequest<AgencyDto>;
/// <summary>
/// Creates an agency AND provisions its Agency CEO in one step. CEO name + email are
/// mandatory: an agency may never exist without an owner. Additional agency fields can be
/// appended to this record without changing the handler's control flow.
/// </summary>
public record CreateAgencyCommand(string Name, string? Code, string CeoName, string CeoEmail) : IRequest<AgencyDto>;
public record UpdateAgencyCommand(Guid Id, string Name, string? Code, bool IsActive) : IRequest<AgencyDto>;
public record AssignCeoCommand(Guid AgencyId, Guid UserId) : IRequest<AgencyDto>;

public class CreateAgencyValidator : AbstractValidator<CreateAgencyCommand>
{
    public CreateAgencyValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).MaximumLength(40);
        RuleFor(x => x.CeoName).NotEmpty().MaximumLength(120)
            .WithMessage("An Agency CEO name is required.");
        RuleFor(x => x.CeoEmail).NotEmpty().EmailAddress().MaximumLength(200)
            .WithMessage("A valid Agency CEO email is required.");
    }
}

public class UpdateAgencyValidator : AbstractValidator<UpdateAgencyCommand>
{
    public UpdateAgencyValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).MaximumLength(40);
    }
}

public class AgenciesHandler :
    IRequestHandler<ListAgenciesQuery, IReadOnlyList<AgencyDto>>,
    IRequestHandler<GetAgencyQuery, AgencyDto>,
    IRequestHandler<CreateAgencyCommand, AgencyDto>,
    IRequestHandler<UpdateAgencyCommand, AgencyDto>,
    IRequestHandler<AssignCeoCommand, AgencyDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;
    private readonly IUserAdminService _userAdmin;
    private readonly IInvitationService _invitations;
    private readonly IJwtTokenService _jwt;

    public AgenciesHandler(
        IApplicationDbContext db,
        ICurrentUser user,
        IIdentityService identity,
        IUserAdminService userAdmin,
        IInvitationService invitations,
        IJwtTokenService jwt)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
        _userAdmin = Guard.AgainstNull(userAdmin);
        _invitations = Guard.AgainstNull(invitations);
        _jwt = Guard.AgainstNull(jwt);
    }

    public async Task<IReadOnlyList<AgencyDto>> Handle(ListAgenciesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdmin();
        var q = _db.Agencies.AsQueryable();
        if (!request.IncludeInactive) q = q.Where(a => a.IsActive);
        var agencies = await q.OrderBy(a => a.Name).ToListAsync(ct);

        var result = new List<AgencyDto>(agencies.Count);
        foreach (var a in agencies) result.Add(await ToDtoAsync(a, ct));
        return result;
    }

    public async Task<AgencyDto> Handle(GetAgencyQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdminOrSameAgency(request.Id);
        var a = await _db.Agencies.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Agency), request.Id);
        return await ToDtoAsync(a, ct);
    }

    public async Task<AgencyDto> Handle(CreateAgencyCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdmin();
        var name = request.Name.Trim();
        if (await _db.Agencies.AnyAsync(a => a.Name == name, ct))
            throw new ConflictException($"Agency '{name}' already exists.");
        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            var code = request.Code!.Trim();
            if (await _db.Agencies.AnyAsync(a => a.Code == code, ct))
                throw new ConflictException($"Agency code '{code}' already exists.");
        }

        // Fail fast BEFORE inserting the agency: a duplicate CEO email must not leave an
        // owner-less agency behind.
        await _invitations.EnsureEmailAvailableAsync(request.CeoEmail.Trim(), ct);

        var agency = new Agency
        {
            Name = name,
            Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code!.Trim(),
            IsActive = true
        };
        _db.Agencies.Add(agency);
        // Every agency needs baseline dispositions or its agents can't wrap up calls.
        _db.WrapUpCodes.AddRange(DefaultWrapUpCodes.For(agency.Id));
        await _db.SaveChangesAsync(ct);

        // Provision the Agency CEO through the shared onboarding service: temporary
        // password, forced first-login, CEO role and invitation email are all reused.
        await _invitations.InviteAsync(new InvitationRequest(
            Email: request.CeoEmail.Trim(),
            FullName: request.CeoName.Trim(),
            AgencyId: agency.Id,
            CallCenterId: null,                       // Agency CEO is agency-level, not pinned
            Roles: new[] { DomainRoles.CEO },
            AgencyName: agency.Name), ct);

        return await ToDtoAsync(agency, ct);
    }

    public async Task<AgencyDto> Handle(UpdateAgencyCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdmin();
        var agency = await _db.Agencies.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Agency), request.Id);

        var wasActive = agency.IsActive;
        agency.Name = request.Name.Trim();
        agency.Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code!.Trim();
        agency.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);

        // Disabling an agency is a kill switch: force-logout every user underneath it so they
        // can't keep working on a still-valid access token. Login/refresh are already blocked
        // (TenantLoginGate), so they stay locked out until the agency is re-enabled.
        if (wasActive && !request.IsActive)
            await _jwt.RevokeAllForAgencyAsync(agency.Id, ct);

        return await ToDtoAsync(agency, ct);
    }

    public async Task<AgencyDto> Handle(AssignCeoCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureSuperAdmin();
        var agency = await _db.Agencies.FirstOrDefaultAsync(x => x.Id == request.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Agency), request.AgencyId);

        var target = await _identity.GetUserAsync(request.UserId, ct)
            ?? throw new NotFoundException("User", request.UserId);

        // Cross-tenant move: if the target user lives in a different agency, relocate them
        // first. We strip the CEO role from their old agency on the way out so the previous
        // tenant doesn't end up with two CEOs of record. SuperAdmin is the only caller that
        // can reach this command, so the relocation is an authorised operation.
        if (target.AgencyId != request.AgencyId)
        {
            var sourceAgencyId = target.AgencyId;

            // If this user holds CEO in their source agency, strip it before the move so
            // we don't carry stale tenant-scoped semantics across the boundary.
            if (target.Roles.Contains(DomainRoles.CEO))
            {
                var trimmed = target.Roles
                    .Where(r => !string.Equals(r, DomainRoles.CEO, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                await _userAdmin.UpdateRolesAsync(request.UserId, trimmed, ct);
            }

            await _userAdmin.SetAgencyAsync(request.UserId, request.AgencyId, ct);

            // Refresh the snapshot — the user's roles & agencyId have changed.
            target = await _identity.GetUserAsync(request.UserId, ct)
                ?? throw new NotFoundException("User", request.UserId);

            _ = sourceAgencyId; // (placeholder if we ever want to audit the move from→to)
        }

        // Strip CEO from any other user in this agency so there is exactly one CEO.
        var existing = await _identity.ListUsersAsync(request.AgencyId, ct);
        foreach (var u in existing)
        {
            if (u.Id == request.UserId) continue;
            if (!u.Roles.Contains(DomainRoles.CEO)) continue;
            var trimmed = u.Roles.Where(r => !string.Equals(r, DomainRoles.CEO, StringComparison.OrdinalIgnoreCase)).ToList();
            await _userAdmin.UpdateRolesAsync(u.Id, trimmed, ct);
        }

        var newRoles = target.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        newRoles.Add(DomainRoles.CEO);
        await _userAdmin.UpdateRolesAsync(request.UserId, newRoles.ToList(), ct);

        return await ToDtoAsync(agency, ct);
    }

    private async Task<AgencyDto> ToDtoAsync(Agency a, CancellationToken ct)
    {
        var users = await _identity.ListUsersAsync(a.Id, ct);
        var ceo = users.FirstOrDefault(u => u.Roles.Contains(DomainRoles.CEO));
        return new AgencyDto(
            a.Id, a.Name, a.Code, a.IsActive,
            ceo?.Id, ceo?.UserName,
            users.Count, a.CreatedAt);
    }

    private void EnsureSuperAdmin()
    {
        if (!_user.Roles.Contains(DomainRoles.SuperAdmin))
            throw new ForbiddenAccessException();
    }

    private void EnsureSuperAdminOrSameAgency(Guid agencyId)
    {
        if (_user.Roles.Contains(DomainRoles.SuperAdmin)) return;
        if (_user.AgencyId == agencyId) return;
        throw new ForbiddenAccessException();
    }
}
