using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CRM.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Defence-in-depth for multi-tenant data isolation. On every insert of a
/// <see cref="TenantEntity"/>:
///   * if the entity has no AgencyId, stamp it with the caller's AgencyId
///   * if the entity already has an AgencyId different from the caller's, reject the save
/// SuperAdmin (and contexts without a current user — seeder, jobs) bypass this check.
/// </summary>
public class TenantInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _user;
    public TenantInterceptor(ICurrentUser user) => _user = Guard.AgainstNull(user);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext ctx)
    {
        // No current user → seeder / design-time / hosted services. Skip enforcement.
        if (_user.UserId is null) return;
        // SuperAdmin operates across tenants and may legitimately set any AgencyId.
        if (_user.Roles.Contains(Roles.SuperAdmin)) return;

        var callerAgency = _user.AgencyId;
        if (callerAgency is null || callerAgency == Guid.Empty) return;

        foreach (var entry in ctx.ChangeTracker.Entries<TenantEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.AgencyId == Guid.Empty)
                    entry.Entity.AgencyId = callerAgency.Value;
                else if (entry.Entity.AgencyId != callerAgency.Value)
                    throw new ForbiddenAccessException();
            }
            else if (entry.State == EntityState.Modified)
            {
                // Block tenant-id tampering on update.
                var orig = entry.OriginalValues[nameof(TenantEntity.AgencyId)];
                if (orig is Guid o && o != entry.Entity.AgencyId)
                    throw new ForbiddenAccessException();
            }
        }
    }
}
