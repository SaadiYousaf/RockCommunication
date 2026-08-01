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

        // Privileged / cross-agency "central" Submission Agents must enrol 2FA before doing anything else.
        if (enforce2Fa && !user.TwoFactorEnabled &&
            (DomainRoles.TwoFactorMandatory(roles) || DomainRoles.IsCentralSubmissionAgent(user.AgencyId, roles)))
            (extra ??= new())[CustomJwtClaims.TwoFactorSetupRequired] = "true";

        return extra;
    }
}
