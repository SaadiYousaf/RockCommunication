using CRM.Application.Common.Authorization;
using CRM.Application.Roles.Dtos;
using CRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Identity;

public class ModuleAccessService : IModuleAccessService
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<ApplicationRole> _roles;

    public ModuleAccessService(AppDbContext db, UserManager<ApplicationUser> users, RoleManager<ApplicationRole> roles)
    {
        _db = db;
        _users = users;
        _roles = roles;
    }

    public async Task<IReadOnlyList<ModuleDto>> ListAllAsync(CancellationToken ct = default)
    {
        var modules = await _db.AppModules.OrderBy(m => m.SortOrder).ThenBy(m => m.Name).ToListAsync(ct);
        return modules.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<string>> GetCodesForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return Array.Empty<string>();

        var roleNames = await _users.GetRolesAsync(user);
        if (roleNames.Count == 0) return Array.Empty<string>();

        var roleIds = await _db.Roles
            .Where(r => roleNames.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync(ct);

        var codes = await _db.RoleModules
            .Where(rm => roleIds.Contains(rm.RoleId))
            .Join(_db.AppModules, rm => rm.ModuleId, m => m.Id, (rm, m) => m.Code)
            .Distinct()
            .ToListAsync(ct);

        return codes;
    }

    public async Task<IReadOnlyList<string>> GetCodesForRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        return await _db.RoleModules
            .Where(rm => rm.RoleId == roleId)
            .Join(_db.AppModules, rm => rm.ModuleId, m => m.Id, (rm, m) => m.Code)
            .ToListAsync(ct);
    }

    internal static ModuleDto ToDto(Domain.Entities.AppModule m)
        => new(m.Id, m.Code, m.Name, m.Group, m.RoutePath, m.Icon, m.SortOrder, m.IsSystem);
}
