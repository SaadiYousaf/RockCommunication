using CRM.Application.Roles.Dtos;

namespace CRM.Application.Common.Authorization;

/// <summary>
/// Read-side service: which modules a given user (or role) can see.
/// Implementation lives in Infrastructure to keep Application persistence-agnostic.
/// </summary>
public interface IModuleAccessService
{
    Task<IReadOnlyList<ModuleDto>> ListAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetCodesForUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetCodesForRoleAsync(Guid roleId, CancellationToken ct = default);
}

/// <summary>
/// Write-side: admin-driven role and module-assignment management.
/// Separating read and write keeps the contracts focused (ISP).
/// </summary>
public interface IRoleManagementService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default);
    Task<RoleDto> GetAsync(Guid roleId, CancellationToken ct = default);
    Task<RoleDto> CreateAsync(string name, IEnumerable<string> moduleCodes, CancellationToken ct = default);
    Task<RoleDto> RenameAsync(Guid roleId, string newName, CancellationToken ct = default);
    Task<RoleDto> SetModulesAsync(Guid roleId, IEnumerable<string> moduleCodes, CancellationToken ct = default);
    Task DeleteAsync(Guid roleId, CancellationToken ct = default);
}
