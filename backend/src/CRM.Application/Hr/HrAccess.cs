using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Hr;

/// <summary>Single source of truth for who may use the HR module: HR / Admin / SuperAdmin.</summary>
public static class HrAccess
{
    public static bool IsHr(ICurrentUser user)
    {
        Guard.AgainstNull(user);
        return user.IsSuperAdmin
            || user.Roles.Contains(DomainRoles.Admin)
            || user.Roles.Contains(DomainRoles.HR);
    }

    public static void EnsureHr(ICurrentUser user)
    {
        if (!IsHr(user)) throw new ForbiddenAccessException();
    }
}
