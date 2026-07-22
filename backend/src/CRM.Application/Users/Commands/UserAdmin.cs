using CRM.Application.Auth.Dtos;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using FluentValidation;
using MediatR;

namespace CRM.Application.Users.Commands;

public record UpdateUserRolesCommand(Guid UserId, IReadOnlyList<string> Roles) : IRequest<UserSummaryDto>;
public record SetActiveCommand(Guid UserId, bool IsActive) : IRequest<UserSummaryDto>;
public record ResetPasswordCommand(Guid UserId, string NewPassword) : IRequest<Unit>;
public record SetPreferred2FaCommand(Guid UserId, string Method) : IRequest<UserSummaryDto>;
public record SetUserTeamCommand(Guid UserId, Guid? TeamId) : IRequest<UserSummaryDto>;
public record SetTeamLeadCommand(Guid TeamId, Guid? UserId) : IRequest<Unit>;

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
    IRequestHandler<SetTeamLeadCommand, Unit>
{
    private readonly IUserAdminService _admin;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;

    public UserAdminHandler(IUserAdminService admin, ICurrentUser user, IPermissionService permissions)
    { _admin = Guard.AgainstNull(admin); _user = Guard.AgainstNull(user); _permissions = Guard.AgainstNull(permissions); }

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
        if (_user.Roles.Contains("SuperAdmin")) return;
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
    /// Set the team-lead user for a team. Pass null to unset.
    /// </summary>
    Task SetTeamLeadAsync(Guid teamId, Guid? userId, CancellationToken ct = default);
    /// <summary>
    /// Move a user to a different tenant. SuperAdmin only — destructive: clears the user's
    /// team membership and any team-lead pointers, since teams live inside agencies.
    /// </summary>
    Task<UserSummaryDto> SetAgencyAsync(Guid userId, Guid agencyId, CancellationToken ct = default);
}
