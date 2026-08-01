using CRM.Application.Profile;

namespace CRM.Application.Common.Interfaces;

/// <summary>
/// Read/write access to the self-service profile fields that live on the Identity user
/// (which sits in the Infrastructure layer). Implemented in <c>CRM.Infrastructure.Identity</c>
/// over <c>UserManager</c> + the DbContext (for org-name resolution).
///
/// Only the PERSONAL fields are writable here: the org-owned fields (display name, username,
/// email, roles, designation, team, call centre, agency) are exposed read-only on the returned
/// <see cref="UserProfileDto"/> and have no setter path anywhere in this contract.
/// </summary>
public interface IUserProfileService
{
    /// <summary>The full profile (read-only org fields + editable personal fields + HasAvatar).</summary>
    Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Persist the caller's editable personal fields (phone / location / bio).</summary>
    Task UpdateProfileAsync(Guid userId, UpdateProfileInput input, CancellationToken ct = default);

    /// <summary>Point the user's avatar at an already-stored file key (or clear it with null).</summary>
    Task SetAvatarAsync(Guid userId, string? key, CancellationToken ct = default);

    /// <summary>The user's avatar storage key, or null when none is set.</summary>
    Task<string?> GetAvatarKeyAsync(Guid userId, CancellationToken ct = default);
}
