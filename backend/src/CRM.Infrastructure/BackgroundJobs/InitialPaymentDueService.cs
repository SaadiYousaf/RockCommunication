using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.BackgroundJobs;

/// <summary>
/// Moves an approved sale to <see cref="ValidatorStatus.PendingInitialPayment"/> once its initial
/// draft date has passed.
///
/// An approved sale is not money until the first premium is collected. Before this, a draft that
/// never landed simply sat in Approved: it counted towards the month's approved premium, nobody
/// was told, and it surfaced only when somebody happened to compare a carrier statement against
/// the queue. The date is already recorded on the application — this just acts on it.
///
/// Deliberately conservative:
///   * only Approved sales move. A sale already marked Active Paid, declined, charged back or
///     anything else has an outcome somebody recorded by hand, and a background job must not
///     overrule a person.
///   * the draft date must be fully PAST — a draft due today is not late today.
///   * it moves each sale exactly once, because the move itself takes it out of Approved.
/// A submission agent can move it straight back once payment clears.
/// </summary>
public class InitialPaymentDueService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<InitialPaymentDueService> _logger;

    /// <summary>Hourly. The trigger is a calendar date, so minutes do not matter.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>A bound per tick, so a first run over a long backlog cannot monopolise the database.</summary>
    private const int BatchSize = 200;

    public InitialPaymentDueService(IServiceProvider sp, ILogger<InitialPaymentDueService> logger)
    {
        _sp = Guard.AgainstNull(sp);
        _logger = Guard.AgainstNull(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait first, so this never contends with startup and seeding for a connection.
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try { await TickAsync(stoppingToken); }
            catch (Exception ex)
            {
                // Never let a bad tick kill the service — it would stop silently and sales would
                // quietly stay in Approved again, which is the very thing this exists to prevent.
                _logger.LogError(ex, "Initial-payment-due sweep failed");
            }
        }
    }

    /// <summary>One sweep. Public so a test can run it directly rather than waiting on the timer.</summary>
    public async Task<int> TickAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notify = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        // No authenticated user in a background scope, so the tenant filter steps aside and this
        // sweeps every agency — which is what it must do.
        //
        // "Past" means the whole day is over: a draft due today is not late until tomorrow.
        var overdue = DateTime.UtcNow.Date;

        var due = await (
            from sale in db.Sales
            join app in db.LeadApplications on sale.LeadId equals app.LeadId
            where sale.ValidatorStatus == ValidatorStatus.Approved
                  && app.InitialDraftDate != null
                  && app.InitialDraftDate < overdue
            select sale)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0) return 0;

        foreach (var sale in due) sale.ValidatorStatus = ValidatorStatus.PendingInitialPayment;
        await db.SaveChangesAsync(ct);

        // Tell the closer. Their sale has stopped counting as approved money and somebody has to
        // chase the payment; oversight sees the same thing on the queue's status chips.
        //
        // The closer only, not every administrator: this fires on a schedule rather than on
        // somebody's action, and a nightly fan-out to every admin is a notification people learn
        // to ignore. Each sale moves exactly once, so nobody is told twice.
        foreach (var sale in due)
        {
            if (sale.CloserUserId == Guid.Empty) continue;
            try
            {
                await notify.DispatchAsync(
                    new NotificationPayload(
                        sale.AgencyId, sale.CloserUserId,
                        "First payment hasn't arrived",
                        $"Sale #{sale.SaleNumber} was approved, but the first premium was due and has not been collected. "
                        + "It is now marked Pending initial payment.",
                        $"/sales/{sale.Id}"),
                    new[] { NotificationChannelType.InApp }, ct);
            }
            catch { /* the status change is what matters; a failed bell must not undo it */ }
        }

        _logger.LogInformation(
            "Moved {Count} approved sale(s) to Pending initial payment — their initial draft date had passed.",
            due.Count);
        return due.Count;
    }
}
