using CRM.Application.Common.Authorization;
using CRM.Application.Roles.Dtos;
using FluentValidation;
using MediatR;

namespace CRM.Application.Roles.Commands;

public record CreateRoleCommand(string Name, IReadOnlyList<string> ModuleCodes) : IRequest<RoleDto>;
public record RenameRoleCommand(Guid RoleId, string Name) : IRequest<RoleDto>;
public record SetRoleModulesCommand(Guid RoleId, IReadOnlyList<string> ModuleCodes) : IRequest<RoleDto>;
public record DeleteRoleCommand(Guid RoleId) : IRequest<Unit>;

public class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80).Matches("^[A-Za-z0-9 _\\-]+$")
            .WithMessage("Role name may contain letters, digits, spaces, dashes and underscores only.");
        RuleFor(x => x.ModuleCodes).NotNull();
    }
}

public class RenameRoleValidator : AbstractValidator<RenameRoleCommand>
{
    public RenameRoleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public class SetRoleModulesValidator : AbstractValidator<SetRoleModulesCommand>
{
    public SetRoleModulesValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.ModuleCodes).NotNull();
    }
}

public class RoleCommandsHandler :
    IRequestHandler<CreateRoleCommand, RoleDto>,
    IRequestHandler<RenameRoleCommand, RoleDto>,
    IRequestHandler<SetRoleModulesCommand, RoleDto>,
    IRequestHandler<DeleteRoleCommand, Unit>
{
    private readonly IRoleManagementService _roles;

    public RoleCommandsHandler(IRoleManagementService roles)
    {
        _roles = roles;
    }

    public Task<RoleDto> Handle(CreateRoleCommand request, CancellationToken ct)
        => _roles.CreateAsync(request.Name, request.ModuleCodes, ct);

    public Task<RoleDto> Handle(RenameRoleCommand request, CancellationToken ct)
        => _roles.RenameAsync(request.RoleId, request.Name, ct);

    public Task<RoleDto> Handle(SetRoleModulesCommand request, CancellationToken ct)
        => _roles.SetModulesAsync(request.RoleId, request.ModuleCodes, ct);

    public async Task<Unit> Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        await _roles.DeleteAsync(request.RoleId, ct);
        return Unit.Value;
    }
}
