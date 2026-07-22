using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CRM.Infrastructure.Persistence.Interceptors;

public class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _user;

    public AuditInterceptor(ICurrentUser user) => _user = Guard.AgainstNull(user);

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
        var now = DateTime.UtcNow;
        var userName = _user.UserName ?? "system";
        var userId = _user.UserId?.ToString();

        var audits = new List<AuditEntry>();

        foreach (var entry in ctx.ChangeTracker.Entries<BaseEntity>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = userName;
                    audits.Add(BuildAudit(entry, "Created", userId, userName));
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userName;
                    audits.Add(BuildAudit(entry, "Updated", userId, userName));
                    break;
                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userName;
                    audits.Add(BuildAudit(entry, "Deleted", userId, userName));
                    break;
            }
        }

        // ApplicationRole isn't a BaseEntity (it inherits from IdentityRole) so it skips
        // the loop above. Track its lifecycle separately so role create/rename/delete is
        // auditable for compliance.
        foreach (var entry in ctx.ChangeTracker.Entries<ApplicationRole>().ToList())
        {
            string? action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                EntityState.Deleted => "Deleted",
                _ => null
            };
            if (action is null) continue;

            var changes = entry.State == EntityState.Added
                ? null
                : entry.Properties
                    .Where(p => p.IsModified)
                    .ToDictionary(p => p.Metadata.Name, p => new { Old = p.OriginalValue, New = p.CurrentValue });

            audits.Add(new AuditEntry
            {
                EntityName = nameof(ApplicationRole),
                EntityId = entry.Entity.Id.ToString(),
                Action = action,
                UserId = userId,
                UserName = userName,
                Changes = changes is null ? null : JsonSerializer.Serialize(changes),
                IpAddress = _user.IpAddress
            });
        }

        if (audits.Count > 0) ctx.Set<AuditEntry>().AddRange(audits);
    }

    private AuditEntry BuildAudit(EntityEntry<BaseEntity> entry, string action, string? userId, string userName)
    {
        var changes = entry.State == EntityState.Added
            ? null
            : entry.Properties
                .Where(p => p.IsModified && p.Metadata.Name != nameof(BaseEntity.UpdatedAt))
                .ToDictionary(p => p.Metadata.Name, p => new { Old = p.OriginalValue, New = p.CurrentValue });

        return new AuditEntry
        {
            EntityName = entry.Entity.GetType().Name,
            EntityId = entry.Entity.Id.ToString(),
            Action = action,
            UserId = userId,
            UserName = userName,
            Changes = changes is null ? null : JsonSerializer.Serialize(changes),
            IpAddress = _user.IpAddress
        };
    }
}
