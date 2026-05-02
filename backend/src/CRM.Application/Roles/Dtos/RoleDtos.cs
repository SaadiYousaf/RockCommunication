namespace CRM.Application.Roles.Dtos;

public record ModuleDto(
    Guid Id,
    string Code,
    string Name,
    string Group,
    string? RoutePath,
    string? Icon,
    int SortOrder,
    bool IsSystem);

public record RoleDto(
    Guid Id,
    string Name,
    bool IsSystem,
    IReadOnlyList<string> Modules,
    Guid? AgencyId = null);
