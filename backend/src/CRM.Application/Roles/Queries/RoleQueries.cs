using CRM.Application.Common.Authorization;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Roles.Dtos;
using CRM.Domain.Common;
using MediatR;

namespace CRM.Application.Roles.Queries;

public record ListRolesQuery() : IRequest<IReadOnlyList<RoleDto>>;
public record GetRoleQuery(Guid RoleId) : IRequest<RoleDto>;
public record ListModulesQuery() : IRequest<IReadOnlyList<ModuleDto>>;
public record GetMyModulesQuery() : IRequest<IReadOnlyList<string>>;

public class RoleQueriesHandler :
    IRequestHandler<ListRolesQuery, IReadOnlyList<RoleDto>>,
    IRequestHandler<GetRoleQuery, RoleDto>,
    IRequestHandler<ListModulesQuery, IReadOnlyList<ModuleDto>>,
    IRequestHandler<GetMyModulesQuery, IReadOnlyList<string>>
{
    private readonly IRoleManagementService _roles;
    private readonly IModuleAccessService _modules;
    private readonly ICurrentUser _user;

    public RoleQueriesHandler(IRoleManagementService roles, IModuleAccessService modules, ICurrentUser user)
    {
        _roles = Guard.AgainstNull(roles);
        _modules = Guard.AgainstNull(modules);
        _user = Guard.AgainstNull(user);
    }

    public Task<IReadOnlyList<RoleDto>> Handle(ListRolesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        return _roles.ListAsync(ct);
    }

    public Task<RoleDto> Handle(GetRoleQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        return _roles.GetAsync(request.RoleId, ct);
    }

    public Task<IReadOnlyList<ModuleDto>> Handle(ListModulesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        return _modules.ListAllAsync(ct);
    }

    public Task<IReadOnlyList<string>> Handle(GetMyModulesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();
        return _modules.GetCodesForUserAsync(_user.UserId.Value, ct);
    }
}
