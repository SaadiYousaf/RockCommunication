using CRM.Application.Common.Commission;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Sales.Commands;

/// <summary>
/// Assign (or clear, when LicenseAgentUserId is null) the License Agent on an already-recorded sale,
/// e.g. from the sale detail page when it wasn't set at approval. Mirrors the assignment done inside
/// the Submission-Agent approval so the "license-agent-approval" commission stays de-duplicated:
/// create if missing, update the UNPAID line on re-assignment, never touch a paid line.
/// </summary>
public record AssignSaleLicenseAgentCommand(Guid SaleId, Guid? LicenseAgentUserId) : IRequest<Unit>;

public class AssignSaleLicenseAgentHandler : IRequestHandler<AssignSaleLicenseAgentCommand, Unit>
{
    private const string LicenseAgentRule = "license-agent-approval";

    private readonly IApplicationDbContext _db;
    private readonly IIdentityService _identity;
    private readonly ICommissionEngine _commission;
    private readonly INotificationDispatcher _notify;

    public AssignSaleLicenseAgentHandler(IApplicationDbContext db, IIdentityService identity, ICommissionEngine commission, INotificationDispatcher notify)
    {
        _db = Guard.AgainstNull(db); _identity = Guard.AgainstNull(identity); _commission = Guard.AgainstNull(commission); _notify = Guard.AgainstNull(notify);
    }

    public async Task<Unit> Handle(AssignSaleLicenseAgentCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);

        // Tenant users are scoped by the query filter; SuperAdmin bypasses and can reach any sale.
        var sale = await _db.Sales.FirstOrDefaultAsync(s => s.Id == request.SaleId, ct)
            ?? throw new NotFoundException(nameof(Sale), request.SaleId);

        // Unassign.
        if (request.LicenseAgentUserId is not { } licenseAgentId || licenseAgentId == Guid.Empty)
        {
            sale.LicenseAgentUserId = null;
            var unpaid = await _db.CommissionEntries.IgnoreQueryFilters()
                .Where(c => c.SaleId == sale.Id && c.AgencyId == sale.AgencyId
                            && c.RuleName == LicenseAgentRule && !c.Paid)
                .ToListAsync(ct);
            if (unpaid.Count > 0) _db.CommissionEntries.RemoveRange(unpaid);
            await _db.SaveChangesAsync(ct);
            return Unit.Value;
        }

        // Assign: the agent must be a License Agent belonging to this sale's agency.
        var agencyUsers = await _identity.ListUsersAsync(sale.AgencyId, ct);
        var agent = agencyUsers.FirstOrDefault(u => u.Id == licenseAgentId
            && u.Roles.Contains(DomainRoles.LicenseAgent, StringComparer.OrdinalIgnoreCase));
        if (agent is null)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["licenseAgentUserId"] = new[] { "The selected agent is not a License Agent in this sale's agency." }
            });

        sale.LicenseAgentUserId = licenseAgentId;

        var line = (await _commission.CalculateForAgentAsync(sale, licenseAgentId, DomainRoles.LicenseAgent, ct))
            .FirstOrDefault();
        if (line is not null)
        {
            var existing = await _db.CommissionEntries.IgnoreQueryFilters().FirstOrDefaultAsync(
                c => c.SaleId == sale.Id && c.AgencyId == sale.AgencyId && c.RuleName == line.RuleName, ct);
            if (existing is null)
            {
                _db.CommissionEntries.Add(new CommissionEntry
                {
                    AgencyId = sale.AgencyId,
                    CallCenterId = sale.CallCenterId,
                    SaleId = sale.Id,
                    AgentUserId = licenseAgentId,
                    RuleName = line.RuleName,
                    Amount = line.Amount,
                    Note = line.Note
                });
            }
            else if (!existing.Paid)
            {
                existing.AgentUserId = licenseAgentId;
                existing.Amount = line.Amount;
                existing.Note = line.Note;
            }
            // else: already paid — leave the historical entry untouched.
        }

        await _db.SaveChangesAsync(ct);
        await NotifyAssignedAsync(sale, licenseAgentId, ct);
        return Unit.Value;
    }

    /// <summary>
    /// Tell the License Agent a sale is now theirs — an in-app bell alert (live-pushed) plus an
    /// email. Best-effort: a notification failure must never fail the assignment itself.
    /// </summary>
    private async Task NotifyAssignedAsync(Sale sale, Guid licenseAgentId, CancellationToken ct)
    {
        try
        {
            await _notify.DispatchAsync(
                new NotificationPayload(
                    sale.AgencyId, licenseAgentId,
                    "A sale was assigned to you",
                    $"Sale #{sale.SaleNumber} ({sale.Carrier}, {sale.MonthlyPremium:C}/mo) is now assigned to you for approval.",
                    $"/sales/{sale.Id}"),
                new[] { NotificationChannelType.InApp, NotificationChannelType.Email }, ct);
        }
        catch { /* advisory only */ }
    }
}
