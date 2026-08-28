using CRM.Application.Auth.Dtos;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using DomainRoles = CRM.Domain.Enums.Roles;
using FluentValidation;
using MediatR;

namespace CRM.Application.Users.Commands;

public record UpdateUserRolesCommand(Guid UserId, IReadOnlyList<string> Roles) : IRequest<UserSummaryDto>;
public record SetActiveCommand(Guid UserId, bool IsActive) : IRequest<UserSummaryDto>;
public record ResetPasswordCommand(Guid UserId, string NewPassword) : IRequest<Unit>;
public record SetPreferred2FaCommand(Guid UserId, string Method) : IRequest<UserSummaryDto>;
public record SetUserTeamCommand(Guid UserId, Guid? TeamId) : IRequest<UserSummaryDto>;
public record SetTeamLeadCommand(Guid TeamId, Guid? UserId) : IRequest<Unit>;
/// <summary>Pin a user to a call center, or pass null to make them agency-level (sees all).</summary>
public record SetUserCallCenterCommand(Guid UserId, Guid? CallCenterId) : IRequest<UserSummaryDto>;
/// <summary>Re-issue the onboarding invitation (fresh temp password + email) for a user who hasn't accepted yet.</summary>
public record ResendInvitationCommand(Guid UserId) : IRequest<Unit>;
/// <summary>
/// Move a user into a different agency. SuperAdmin only — the service clears team membership and
/// team-lead pointers, because teams live inside an agency and would otherwise dangle.
///
/// The capability already existed on IUserAdminService but was never routed, so a SuperAdmin who
/// created a user in the wrong agency had no way to correct it short of deleting and re-inviting.
/// </summary>
public record SetUserAgencyCommand(Guid UserId, Guid AgencyId) : IRequest<UserSummaryDto>;

public class UpdateUserRolesValidator : AbstractValidator<UpdateUserRolesCommand>
{
    public UpdateUserRolesValidator() => RuleFor(x => x.UserId).NotEmpty();
}

public class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NewPassword).MinimumLength(8);
    }
}

public class UserAdminHandler :
    IRequestHandler<UpdateUserRolesCommand, UserSummaryDto>,
    IRequestHandler<SetActiveCommand, UserSummaryDto>,
    IRequestHandler<ResetPasswordCommand, Unit>,
    IRequestHandler<SetPreferred2FaCommand, UserSummaryDto>,
    IRequestHandler<SetUserTeamCommand, UserSummaryDto>,
    IRequestHandler<SetUserCallCenterCommand, UserSummaryDto>,
    IRequestHandler<SetUserAgencyCommand, UserSummaryDto>,
    IRequestHandler<SetTeamLeadCommand, Unit>,
    IRequestHandler<ResendInvitationCommand, Unit>
{
    private readonly IUserAdminService _admin;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IInvitationService _invitations;

    public UserAdminHandler(IUserAdminService admin, ICurrentUser user, IPermissionService permissions, IInvitationService invitations)
    { _admin = Guard.AgainstNull(admin); _user = Guard.AgainstNull(user); _permissions = Guard.AgainstNull(permissions); _invitations = Guard.AgainstNull(invitations); }

    public async Task<UserSummaryDto> Handle(UpdateUserRolesCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        await EnsurePermissionAsync(Permissions.UsersManage, ct);
        return await _admin.UpdateRolesAsync(request.UserId, request.Roles, ct);
    }

    public async Task<UserSummaryDto> Handle(SetActiveCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        await EnsurePermissionAsync(Permissions.UsersManage, ct);
        return await _admin.SetActiveAsync(request.UserId, request.IsActive, ct);
    }

    public async Task<Unit> Handle(ResetPasswordCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        await EnsurePermissionAsync(Permissions.UsersManage, ct);
        await _admin.ResetPasswordAsync(request.UserId, request.NewPassword, ct);
        return Unit.Value;
    }

    public async Task<UserSummaryDto> Handle(SetPreferred2FaCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();
        // A user can always change their own 2FA; admins can change anyone's.
        if (_user.UserId != request.UserId)
            await EnsurePermissionAsync(Permissions.UsersManage, ct);
        return await _admin.SetPreferred2FaAsync(request.UserId, request.Method, ct);
    }

    public async Task<UserSummaryDto> Handle(SetUserTeamCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        await EnsurePermissionAsync(Permissions.TeamWrite, ct);
        return await _admin.SetTeamAsync(request.UserId, request.TeamId, ct);
    }

    public async Task<UserSummaryDto> Handle(SetUserCallCenterCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        await EnsurePermissionAsync(Permissions.UsersManage, ct);
        return await _admin.SetCallCenterAsync(request.UserId, request.CallCenterId, ct);
    }

    public async Task<UserSummaryDto> Handle(SetUserAgencyCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        // Moving a user BETWEEN tenants is strictly a platform operation: an agency admin must never
        // be able to push one of their users into someone else's agency, or pull one out of it.
        // UsersManage alone is not enough here.
        if (!_user.Roles.Contains(DomainRoles.SuperAdmin)) throw new ForbiddenAccessException();
        return await _admin.SetAgencyAsync(request.UserId, request.AgencyId, ct);
    }

    public async Task<Unit> Handle(ResendInvitationCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        // Only a user-manager may resend, and only for a user they're allowed to manage
        // (the invitation service loads the user; the same-agency/call-center guard applies
        // through UserAdminService when the caller later manages them). Gate on UsersManage.
        await EnsurePermissionAsync(Permissions.UsersManage, ct);
        await _admin.EnsureCanManageAsync(request.UserId, ct);
        await _invitations.ResendAsync(request.UserId, ct);
        return Unit.Value;
    }

    public async Task<Unit> Handle(SetTeamLeadCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        await EnsurePermissionAsync(Permissions.TeamWrite, ct);
        await _admin.SetTeamLeadAsync(request.TeamId, request.UserId, ct);
        return Unit.Value;
    }

    // Centralised permission gate — SuperAdmin always passes; everyone else
    // needs the explicit grant via their roles.
    private async Task EnsurePermissionAsync(string code, CancellationToken ct)
    {
        if (_user.UserId is null) throw new ForbiddenAccessException();
        if (_user.Roles.Contains(DomainRoles.SuperAdmin)) return;
        if (!await _permissions.HasAsync(_user.UserId.Value, code, ct))
            throw new ForbiddenAccessException();
    }
}

public interface IUserAdminService
{
    Task<UserSummaryDto> UpdateRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken ct = default);
    Task<UserSummaryDto> SetActiveAsync(Guid userId, bool isActive, CancellationToken ct = default);
    Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default);
    Task<UserSummaryDto> SetPreferred2FaAsync(Guid userId, string method, CancellationToken ct = default);
    /// <summary>
    /// Move a user onto a team (or off — pass null). Validates the team belongs to the
    /// caller's agency to prevent cross-tenant moves.
    /// </summary>
    Task<UserSummaryDto> SetTeamAsync(Guid userId, Guid? teamId, CancellationToken ct = default);
    /// <summary>
    /// Pin a user to a call center (or pass null for agency-level). Validates the call center
    /// belongs to the caller's agency to prevent cross-tenant assignment.
    /// </summary>
    Task<UserSummaryDto> SetCallCenterAsync(Guid userId, Guid? callCenterId, CancellationToken ct = default);
    /// <summary>
    /// Set the team-lead user for a team. Pass null to unset.
    /// </summary>
    Task SetTeamLeadAsync(Guid teamId, Guid? userId, CancellationToken ct = default);
    /// <summary>
    /// Move a user to a different tenant. SuperAdmin only — destructive: clears the user's
    /// team membership and any team-lead pointers, since teams live inside agencies.
    /// </summary>
    Task<UserSummaryDto> SetAgencyAsync(Guid userId, Guid agencyId, CancellationToken ct = default);
    /// <summary>
    /// Throws if the caller may not manage the target user (cross-agency / cross-call-center /
    /// SuperAdmin target). Reuses the same rule as every other user-admin operation.
    /// </summary>
    Task EnsureCanManageAsync(Guid userId, CancellationToken ct = default);
}
