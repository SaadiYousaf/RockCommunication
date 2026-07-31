using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.BackgroundJobs;

/// <summary>
/// Automates what HR otherwise does by hand with the "Fill from clock-ins" button: once an hour it
/// marks Present — for every agency — any employee whose linked login clocked in on a recent day and
/// who has no attendance mark yet. Idempotent and non-destructive: it never marks Absent/NCNS and
/// never overwrites a status HR already set, so a forgotten day no longer silently understates the
/// monthly roll-up that feeds payroll. Mirrors <see cref="Domain.Entities.EmployeeAttendance"/> logic
/// in FillAttendanceFromClockInsCommand, applied agency-wide.
/// </summary>
public class AttendanceAutofillService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<AttendanceAutofillService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    /// <summary>Also backfill the last couple of days, in case the app was down at a day boundary.</summary>
    private const int LookbackDays = 2;

    public AttendanceAutofillService(IServiceProvider sp, ILogger<AttendanceAutofillService> logger)
    {
        _sp = Guard.AgainstNull(sp);
        _logger = Guard.AgainstNull(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Attendance autofill tick failed"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var today = DateTime.UtcNow.Date;
        var total = 0;
        for (var back = 0; back <= LookbackDays; back++)
            total += await FillDayAsync(db, today.AddDays(-back), ct);

        if (total > 0) _logger.LogInformation("Attendance autofill marked {Count} present from clock-ins", total);
    }

    private static async Task<int> FillDayAsync(AppDbContext db, DateTime day, CancellationToken ct)
    {
        var next = day.AddDays(1);

        // No agency context in a background job, so bypass the tenant filter (re-add !IsDeleted by hand).
        var employees = await db.Employees.IgnoreQueryFilters()
            .Where(e => !e.IsDeleted && e.UserId != null)
            .Select(e => new { e.Id, e.AgencyId, UserId = e.UserId!.Value })
            .ToListAsync(ct);
        if (employees.Count == 0) return 0;

        var userIds = employees.Select(e => e.UserId).Distinct().ToList();
        var empIds = employees.Select(e => e.Id).ToList();

        var clockedIn = (await db.AgentSessions.AsNoTracking().IgnoreQueryFilters()
                .Where(s => userIds.Contains(s.UserId) && s.ClockInAt >= day && s.ClockInAt < next)
                .Select(s => s.UserId).ToListAsync(ct))
            .ToHashSet();
        if (clockedIn.Count == 0) return 0;

        var alreadyMarked = (await db.EmployeeAttendances.AsNoTracking().IgnoreQueryFilters()
                .Where(a => a.Date == day && empIds.Contains(a.EmployeeId))
                .Select(a => a.EmployeeId).ToListAsync(ct))
            .ToHashSet();

        var count = 0;
        foreach (var e in employees)
        {
            if (!clockedIn.Contains(e.UserId) || alreadyMarked.Contains(e.Id)) continue;
            db.EmployeeAttendances.Add(new EmployeeAttendance
            { AgencyId = e.AgencyId, EmployeeId = e.Id, Date = day, Status = AttendanceStatus.Present });
            count++;
        }
        if (count > 0) await db.SaveChangesAsync(ct);
        return count;
    }
}
