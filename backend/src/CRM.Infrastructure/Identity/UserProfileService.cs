using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Profile;
using CRM.Domain.Common;
using CRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Identity;

/// <summary>
/// Reads and writes the self-service profile fields on <see cref="ApplicationUser"/>. The
/// org-owned fields (name, username, email, roles, designation, team, call centre, agency) are
/// resolved read-only for display; the write methods only ever touch the personal fields, so
/// there is no code path here that lets a user mutate an org-managed value.
/// </summary>
public class UserProfileService : IUserProfileService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;

    public UserProfileService(UserManager<ApplicationUser> users, AppDbContext db)
    {
        _users = Guard.AgainstNull(users);
        _db = Guard.AgainstNull(db);
    }

    public async Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);

        var roles = (await _users.GetRolesAsync(user)).ToList();

        return new UserProfileDto(
            user.Id,
            user.UserName ?? string.Empty,
            user.DisplayName,
            user.Email ?? string.Empty,
            roles,
            user.AgencyId,
            await ResolveAgencyNameAsync(user.AgencyId, ct),
            user.CallCenterId,
            await ResolveCallCenterNameAsync(user.CallCenterId, ct),
            user.TeamId,
            await ResolveTeamNameAsync(user.TeamId, ct),
            await ResolveDesignationAsync(user.Id, ct),
            user.PhoneNumber,
            user.Location,
            user.Bio,
            HasAvatar: !string.IsNullOrEmpty(user.AvatarKey));
    }

    public async Task UpdateProfileAsync(Guid userId, UpdateProfileInput input, CancellationToken ct = default)
    {
        Guard.AgainstNull(input);
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);

        // Only PERSONAL fields are touched — never DisplayName / UserName / Email / roles / org links.
        user.PhoneNumber = Clean(input.PhoneNumber);
        user.Location = Clean(input.Location);
        user.Bio = Clean(input.Bio);

        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
            throw new ConflictException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task SetAvatarAsync(Guid userId, string? key, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);

        user.AvatarKey = string.IsNullOrWhiteSpace(key) ? null : key;

        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
            throw new ConflictException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public Task<string?> GetAvatarKeyAsync(Guid userId, CancellationToken ct = default)
        => _users.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.AvatarKey)
            .FirstOrDefaultAsync(ct);

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>Agency display name, or null for SuperAdmin / central users (Guid.Empty).</summary>
    private Task<string?> ResolveAgencyNameAsync(Guid agencyId, CancellationToken ct)
        => agencyId == Guid.Empty
            ? Task.FromResult<string?>(null)
            : _db.Agencies.IgnoreQueryFilters().Where(a => a.Id == agencyId).Select(a => a.Name).FirstOrDefaultAsync(ct);

    /// <summary>Call-center display name, or null when the user is agency-level (no call center).</summary>
    private Task<string?> ResolveCallCenterNameAsync(Guid? callCenterId, CancellationToken ct)
        => callCenterId is not { } ccId || ccId == Guid.Empty
            ? Task.FromResult<string?>(null)
            : _db.CallCenters.IgnoreQueryFilters().Where(c => c.Id == ccId).Select(c => c.Name).FirstOrDefaultAsync(ct);

    /// <summary>Team display name, or null when the user isn't assigned to a team.</summary>
    private Task<string?> ResolveTeamNameAsync(Guid? teamId, CancellationToken ct)
        => teamId is not { } tId || tId == Guid.Empty
            ? Task.FromResult<string?>(null)
            : _db.Teams.IgnoreQueryFilters().Where(t => t.Id == tId).Select(t => t.Name).FirstOrDefaultAsync(ct);

    /// <summary>The user's designation from their HR employee record (null when unlinked).</summary>
    private async Task<string?> ResolveDesignationAsync(Guid userId, CancellationToken ct)
    {
        var designation = await _db.Employees.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.UserId == userId && !e.IsDeleted)
            .Select(e => (Domain.Enums.EmployeeDesignation?)e.Designation)
            .FirstOrDefaultAsync(ct);
        return designation?.ToString();
    }
}
