using Microsoft.AspNetCore.Identity;

namespace CRM.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public Guid AgencyId { get; set; }
    public Guid? TeamId { get; set; }

    /// <summary>
    /// The call center this user operates in. Null = agency-level user (Admin / managers /
    /// CEO) who sees every call center in the agency; non-null = pinned to one call center,
    /// so their reads of pipeline data are isolated to it. Flows into the JWT "callcenter" claim.
    /// </summary>
    public Guid? CallCenterId { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>
    /// Opaque <see cref="Application.Common.Interfaces.IFileStorage"/> key for the user's
    /// self-uploaded profile photo (container "avatars"), or null for none. A PERSONAL field the
    /// user edits themselves — never set by admins/HR. Bytes are only ever served through the
    /// authorized ProfileController avatar endpoint.
    /// </summary>
    public string? AvatarKey { get; set; }

    /// <summary>Free-text "about me" the user maintains themselves (PERSONAL, self-editable).</summary>
    public string? Bio { get; set; }

    /// <summary>The user's city / location (PERSONAL, self-editable). PhoneNumber is inherited.</summary>
    public string? Location { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string PreferredTwoFactorMethod { get; set; } = "Totp";

    /// <summary>
    /// True for users created via admin registration with a temporary password.
    /// They are forced to change their password before they can use the rest of the app.
    /// Doubles as the "first login" flag for the onboarding flow.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// When the onboarding invitation (temporary password email) was last sent.
    /// Null = never invited. Updated again on every resend.
    /// </summary>
    public DateTime? InvitationSentAt { get; set; }

    /// <summary>
    /// When the invitation was accepted — set the moment the user completes their forced
    /// first-login password change. Null while the invitation is still outstanding.
    /// </summary>
    public DateTime? InvitationAcceptedAt { get; set; }
}

public class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>
    /// Null = system role template (visible to all agencies, immutable for tenants).
    /// Non-null = agency-scoped custom role created by that agency's CEO; only that agency
    /// can list/edit/assign it. Enforced in <c>RoleManagementService</c>.
    /// </summary>
    public Guid? AgencyId { get; set; }

    public ApplicationRole() { }
    public ApplicationRole(string name) : base(name) { NormalizedName = name.ToUpperInvariant(); }
    public ApplicationRole(string name, Guid? agencyId) : base(name)
    {
        NormalizedName = name.ToUpperInvariant();
        AgencyId = agencyId;
    }
}

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByHash { get; set; }
    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;

    /// <summary>
    /// The agency/call-center this SESSION is scoped to after an admin chose a working context
    /// (POST /api/auth/context). Null = the user's home scope. Persisted here so the choice
    /// survives access-token refresh (which otherwise re-derives scope from the user record).
    /// Only honored for a SuperAdmin's agency; for other users only the call-center narrows and
    /// only within their own agency (see JwtTokenService.RefreshAsync).
    /// </summary>
    public Guid? ScopeAgencyId { get; set; }
    public Guid? ScopeCallCenterId { get; set; }
}

public class TwoFactorPendingToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Number of failed verify attempts on this token. After
    /// <c>IdentityService.MaxTwoFactorAttempts</c> the row is deleted and the user must
    /// re-authenticate from /login.
    /// </summary>
    public int Attempts { get; set; }
}
