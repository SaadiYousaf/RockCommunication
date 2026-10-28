using CRM.Application.Common.Interfaces;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Infrastructure.Identity;

/// <summary>
/// Single source of truth for the JWT "enforcement" claims that CONFINE a session — forced
/// password change and mandatory-2FA setup. Computed identically at initial login AND on token
/// refresh so a refresh can never silently strip the confinement (which would let a temporary-
/// password or un-enrolled-2FA session escalate to full access). See the gate middleware:
/// PasswordChangeRequiredMiddleware / TwoFactorSetupRequiredMiddleware.
/// </summary>
internal static class AuthEnforcementClaims
{
    public static Dictionary<string, string>? Build(ApplicationUser user, IReadOnlyList<string> roles, bool enforce2Fa)
    {
        Dictionary<string, string>? extra = null;

        if (user.MustChangePassword)
            (extra ??= new())[CustomJwtClaims.PasswordChangeRequired] = "true";

        // Privileged roles must enrol 2FA before doing anything else.
        //
        // Central Submission Agents USED to be forced here too, on the reasoning that they read
        // PII across every agency. Removed at the owner's instruction (28 Oct 2026): these are
        // floor staff who sign in from shared machines, and the enrolment was blocking the work.
        // They remain covered by password policy, the IP allowlist and the audit log, and any
        // agent can still turn 2FA on themselves. Putting it back is one clause.
        if (enforce2Fa && !user.TwoFactorEnabled && DomainRoles.TwoFactorMandatory(roles))
            (extra ??= new())[CustomJwtClaims.TwoFactorSetupRequired] = "true";

        return extra;
    }
}
