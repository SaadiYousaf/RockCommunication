using CRM.Domain.Common;
using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent, additive migration of existing data into the call-center isolation model.
/// For every agency it ensures a default "Main" call center and stamps every pipeline row
/// that predates the feature (CallCenterId == <see cref="Guid.Empty"/>) onto it. Existing
/// users keep a null CallCenterId (agency-level), so behaviour is unchanged until an admin
/// assigns them to a call center — no lockout, no data loss. Safe to run on every startup.
/// </summary>
public static class CallCenterBackfill
{
    public const string DefaultName = "Main";

    public static async Task RunAsync(AppDbContext db, CancellationToken ct = default)
    {
        var agencyIds = await db.Agencies.AsNoTracking()
            .Where(a => a.Id != Guid.Empty)
            .Select(a => a.Id)
            .ToListAsync(ct);

        foreach (var agencyId in agencyIds)
        {
            var main = await db.CallCenters.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.AgencyId == agencyId && c.Name == DefaultName, ct);
            if (main is null)
            {
                main = new CallCenter { AgencyId = agencyId, Name = DefaultName, Code = "MAIN", IsActive = true };
                db.CallCenters.Add(main);
                await db.SaveChangesAsync(ct);
            }

            await StampAsync(db.Leads, agencyId, main.Id, ct);
            await StampAsync(db.Sales, agencyId, main.Id, ct);
            await StampAsync(db.LeadApplications, agencyId, main.Id, ct);
            await StampAsync(db.LeadActivities, agencyId, main.Id, ct);
            await StampAsync(db.ScheduledCallbacks, agencyId, main.Id, ct);
            await StampAsync(db.CallRecords, agencyId, main.Id, ct);
            await StampAsync(db.CommissionEntries, agencyId, main.Id, ct);
        }
    }

    // Direct set-based UPDATE (bypasses the change tracker + interceptor). IgnoreQueryFilters
    // so soft-deleted and any-call-center rows are all covered. Only touches unstamped rows,
    // which makes re-runs a no-op.
    private static Task StampAsync<T>(DbSet<T> set, Guid agencyId, Guid callCenterId, CancellationToken ct)
        where T : CallCenterEntity
        => set.IgnoreQueryFilters()
              .Where(e => e.AgencyId == agencyId && e.CallCenterId == Guid.Empty)
              .ExecuteUpdateAsync(s => s.SetProperty(e => e.CallCenterId, callCenterId), ct);
}
