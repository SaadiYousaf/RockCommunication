using CRM.Application.Common.Authorization;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Roles.Dtos;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Identity;

/// <summary>
/// Owns the lifecycle of ApplicationRole + RoleModule join records.
/// Roles are either system templates (AgencyId == null, immutable for tenants) or
/// agency-scoped custom roles (AgencyId set, only that agency may edit). Visibility
/// rules: SuperAdmin sees all; agency users see system templates plus their own roles.
/// </summary>
public class RoleManagementService : IRoleManagementService
{
    private static readonly HashSet<string> ProtectedRoleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        Roles.SuperAdmin,
        Roles.Admin,
        Roles.CEO,
        Roles.QAManager,
        Roles.ProjectManager,
        Roles.TechLead,
        Roles.ProgramManager,
        Roles.TeamLead,
        Roles.Fronter,
        Roles.Verifier,
        Roles.JrCloser,
        Roles.Closer,
        Roles.Validator,
        Roles.SelfValidator,
        Roles.Followups,
        Roles.Correspondence,
        Roles.Winbacks,
    };

    private readonly RoleManager<ApplicationRole> _roles;
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;

    public RoleManagementService(RoleManager<ApplicationRole> roles, AppDbContext db, ICurrentUser user)
    {
        _roles = roles;
        _db = db;
        _user = user;
    }

    private bool IsSuperAdmin => _user.Roles.Contains(Roles.SuperAdmin);
    private Guid? CallerAgencyId => _user.AgencyId is null || _user.AgencyId == Guid.Empty ? null : _user.AgencyId;

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default)
    {
        var q = _db.Roles.AsQueryable();
        if (!IsSuperAdmin)
        {
            // Agency users see system templates + their own custom roles.
            var agencyId = CallerAgencyId;
            q = q.Where(r => r.AgencyId == null || r.AgencyId == agencyId);
        }
        var roles = await q.OrderBy(r => r.Name).ToListAsync(ct);
        var roleIds = roles.Select(r => r.Id).ToList();

        var assignments = await _db.RoleModules
            .Where(rm => roleIds.Contains(rm.RoleId))
            .Join(_db.AppModules, rm => rm.ModuleId, m => m.Id, (rm, m) => new { rm.RoleId, m.Code })
            .ToListAsync(ct);

        var grouped = assignments
            .GroupBy(a => a.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToList());

        return roles.Select(r => new RoleDto(
            r.Id,
            r.Name ?? string.Empty,
            IsProtected(r.Name),
            grouped.TryGetValue(r.Id, out var codes) ? codes : new List<string>(),
            r.AgencyId)).ToList();
    }

    public async Task<RoleDto> GetAsync(Guid roleId, CancellationToken ct = default)
    {
        var role = await _roles.FindByIdAsync(roleId.ToString())
            ?? throw new NotFoundException("Role", roleId);

        EnsureCallerCanView(role);

        var codes = await _db.RoleModules
            .Where(rm => rm.RoleId == roleId)
            .Join(_db.AppModules, rm => rm.ModuleId, m => m.Id, (rm, m) => m.Code)
            .ToListAsync(ct);

        return new RoleDto(role.Id, role.Name ?? string.Empty, IsProtected(role.Name), codes, role.AgencyId);
    }

    public async Task<RoleDto> CreateAsync(string name, IEnumerable<string> moduleCodes, CancellationToken ct = default)
    {
        // Roles created by an agency user are owned by that agency; SuperAdmin creates
        // system templates (AgencyId == null) unless we later expose an explicit param.
        var ownerAgency = IsSuperAdmin ? (Guid?)null : CallerAgencyId
            ?? throw new ForbiddenAccessException();

        var normalized = name.ToUpperInvariant();
        var collides = await _db.Roles.AnyAsync(r =>
            r.NormalizedName == normalized && r.AgencyId == ownerAgency, ct);
        if (collides)
            throw new ConflictException($"Role '{name}' already exists in this scope.");

        var role = new ApplicationRole(name, ownerAgency);
        var result = await _roles.CreateAsync(role);
        if (!result.Succeeded)
            throw new ConflictException(string.Join("; ", result.Errors.Select(e => e.Description)));

        await ReplaceModulesAsync(role.Id, moduleCodes, ct);
        return await GetAsync(role.Id, ct);
    }

    public async Task<RoleDto> RenameAsync(Guid roleId, string newName, CancellationToken ct = default)
    {
        var role = await _roles.FindByIdAsync(roleId.ToString())
            ?? throw new NotFoundException("Role", roleId);

        EnsureCallerCanEdit(role);

        if (IsProtected(role.Name))
            throw new ConflictException("System roles cannot be renamed.");

        role.Name = newName;
        role.NormalizedName = newName.ToUpperInvariant();
        var result = await _roles.UpdateAsync(role);
        if (!result.Succeeded)
            throw new ConflictException(string.Join("; ", result.Errors.Select(e => e.Description)));

        return await GetAsync(role.Id, ct);
    }

    public async Task<RoleDto> SetModulesAsync(Guid roleId, IEnumerable<string> moduleCodes, CancellationToken ct = default)
    {
        var role = await _roles.FindByIdAsync(roleId.ToString())
            ?? throw new NotFoundException("Role", roleId);

        EnsureCallerCanEdit(role);

        await ReplaceModulesAsync(role.Id, moduleCodes, ct);
        return await GetAsync(role.Id, ct);
    }

    public async Task DeleteAsync(Guid roleId, CancellationToken ct = default)
    {
        var role = await _roles.FindByIdAsync(roleId.ToString())
            ?? throw new NotFoundException("Role", roleId);

        EnsureCallerCanEdit(role);

        if (IsProtected(role.Name))
            throw new ConflictException("System roles cannot be deleted.");

        var existingLinks = _db.RoleModules.Where(rm => rm.RoleId == roleId);
        _db.RoleModules.RemoveRange(existingLinks);
        await _db.SaveChangesAsync(ct);

        var result = await _roles.DeleteAsync(role);
        if (!result.Succeeded)
            throw new ConflictException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    private void EnsureCallerCanView(ApplicationRole role)
    {
        if (IsSuperAdmin) return;
        if (role.AgencyId is null) return; // system templates are visible to all
        if (role.AgencyId == CallerAgencyId) return;
        throw new ForbiddenAccessException();
    }

    private void EnsureCallerCanEdit(ApplicationRole role)
    {
        if (IsSuperAdmin) return;
        // Agency users may only edit roles owned by their agency. System templates are
        // immutable for tenants — only SuperAdmin can change them.
        if (role.AgencyId is not null && role.AgencyId == CallerAgencyId) return;
        throw new ForbiddenAccessException();
    }

    private async Task ReplaceModulesAsync(Guid roleId, IEnumerable<string> moduleCodes, CancellationToken ct)
    {
        var requested = moduleCodes
            .Select(c => c?.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var modules = await _db.AppModules
            .Where(m => requested.Contains(m.Code))
            .ToListAsync(ct);

        var unknown = requested.Except(modules.Select(m => m.Code), StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0)
            throw new ConflictException($"Unknown module codes: {string.Join(", ", unknown)}");

        var existing = await _db.RoleModules.Where(rm => rm.RoleId == roleId).ToListAsync(ct);
        _db.RoleModules.RemoveRange(existing);

        foreach (var m in modules)
            _db.RoleModules.Add(new RoleModule { RoleId = roleId, ModuleId = m.Id });

        await _db.SaveChangesAsync(ct);
    }

    private static bool IsProtected(string? name) =>
        !string.IsNullOrEmpty(name) && ProtectedRoleNames.Contains(name);
}
