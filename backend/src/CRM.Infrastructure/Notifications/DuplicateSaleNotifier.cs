using CRM.Application.Common.Notifications;
using CRM.Application.Sales.Notifications;
using CRM.Domain.Common;
using CRM.Infrastructure.Identity;
using CRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Infrastructure.Notifications;

/// <summary>
/// Resolves the oversight recipients for a rejected duplicate-sale attempt (call-centre admin(s),
/// every Super Admin, and the closer's team lead) and sends each an in-app alert naming who tried.
/// Lives here (not in the Application handler) because recipient resolution needs Identity roles —
/// which the Application layer deliberately cannot see. Wholly best-effort: any failure is swallowed
/// so a clean 409 (duplicate rejected) never degrades into a 500.
/// </summary>
public class DuplicateSaleNotifier : IDuplicateSaleNotifier
{
    // Alert copy — kept as a constant and deliberately free of internal codes; the body names the
    // person and lead in plain language (see the interpolation below).
    private const string AlertTitle = "Duplicate sale attempt";

    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _notify;

    public DuplicateSaleNotifier(UserManager<ApplicationUser> users, AppDbContext db, INotificationDispatcher notify)
    {
        _users = Guard.AgainstNull(users);
        _db = Guard.AgainstNull(db);
        _notify = Guard.AgainstNull(notify);
    }

    public async Task NotifyAsync(DuplicateSaleAttempt attempt, CancellationToken ct = default)
    {
        Guard.AgainstNull(attempt);
        try
        {
            var closer = await _users.FindByIdAsync(attempt.CloserUserId.ToString());
            var closerName = FirstNonEmpty(closer?.DisplayName, closer?.UserName) ?? "An agent";

            var recipients = new HashSet<Guid>();

            // Call-centre admin(s) for the sale's centre — skipped when the lead has no centre.
            if (attempt.CallCenterId is { } cc)
                foreach (var u in await _users.GetUsersInRoleAsync(DomainRoles.CallCenterAdmin))
                    if (u.AgencyId == attempt.AgencyId && u.CallCenterId == cc)
                        recipients.Add(u.Id);

            // Every Super Admin — platform oversight, so they carry no agency of their own.
            foreach (var u in await _users.GetUsersInRoleAsync(DomainRoles.SuperAdmin))
                recipients.Add(u.Id);

            // The attempting closer's own team lead.
            if (closer?.TeamId is { } teamId)
            {
                var teamLeadUserId = await _db.Teams
                    .Where(t => t.Id == teamId && t.AgencyId == attempt.AgencyId)
                    .Select(t => t.TeamLeadUserId)
                    .FirstOrDefaultAsync(ct);
                if (teamLeadUserId is { } tl) recipients.Add(tl);
            }

            // Never alert the closer about their own attempt.
            recipients.Remove(attempt.CloserUserId);
            if (recipients.Count == 0) return;

            var body = $"{closerName} tried to submit a duplicate sale for {attempt.LeadName}.";
            var url = $"/leads/{attempt.LeadId}";
            foreach (var userId in recipients)
            {
                try
                {
                    await _notify.DispatchAsync(
                        new NotificationPayload(attempt.AgencyId, userId, AlertTitle, body, url),
                        new[] { NotificationChannelType.InApp }, ct);
                }
                catch { /* graceful — one bad recipient must not stop the others */ }
            }
        }
        catch { /* graceful — alerting must never turn a clean duplicate rejection into a 500 */ }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
