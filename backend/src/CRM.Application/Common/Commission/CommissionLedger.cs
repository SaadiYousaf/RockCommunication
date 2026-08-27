using CRM.Application.Common.Interfaces;
using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Common.Commission;

/// <summary>
/// The lifecycle of one sale's commission lines: void them when the sale goes bad, revive them when
/// it comes good, flip their sign on a charge-back.
///
/// Centralised because FOUR handlers need the same rules — the validator queue, validate-sale, the
/// commission desk and retention — and they had drifted apart: one voided a rejected sale's lines
/// but never revived them on re-approval (the agent was silently paid nothing), another declined a
/// sale without voiding anything (payroll paid out on a dead policy). Every path now shares this.
///
/// Two invariants hold everywhere:
///  • PAID lines are history and are never touched — payroll already paid them out.
///  • Reads bypass the tenant query filter and scope to the sale's own agency, because a central
///    Submission Agent acts with an empty tenant and would otherwise match no rows at all.
///    IgnoreQueryFilters also drops the soft-delete filter, so !IsDeleted is re-added by hand where
///    it matters — and deliberately NOT added where the point is to find deleted rows.
/// </summary>
public static class CommissionLedger
{
    /// <summary>
    /// Void the sale's still-unpaid commission (a negative outcome: declined, cancelled). The audit
    /// interceptor turns the remove into a soft delete, so <see cref="ReviveUnpaidAsync"/> can bring
    /// them back if the sale is later re-approved. Idempotent.
    /// </summary>
    public static async Task VoidUnpaidAsync(IApplicationDbContext db, Sale sale, CancellationToken ct = default)
    {
        var unpaid = await db.CommissionEntries.IgnoreQueryFilters()
            .Where(c => c.SaleId == sale.Id && c.AgencyId == sale.AgencyId && !c.Paid && !c.IsDeleted)
            .ToListAsync(ct);
        if (unpaid.Count > 0) db.CommissionEntries.RemoveRange(unpaid);
    }

    /// <summary>
    /// Bring back commission a previous negative outcome voided, because the sale came good again.
    /// Only unpaid lines are revived; a paid line was never voided in the first place. Idempotent —
    /// a line that is already live is left alone.
    /// </summary>
    public static async Task ReviveUnpaidAsync(IApplicationDbContext db, Sale sale, CancellationToken ct = default)
    {
        // No !IsDeleted here — finding the soft-deleted rows IS the point.
        var voided = await db.CommissionEntries.IgnoreQueryFilters()
            .Where(c => c.SaleId == sale.Id && c.AgencyId == sale.AgencyId && !c.Paid && c.IsDeleted)
            .ToListAsync(ct);
        foreach (var line in voided) line.IsDeleted = false;
    }

    /// <summary>
    /// Drive the sale's unpaid lines to the sign the outcome implies — negative once the carrier
    /// claws an advance back, positive again when the policy recovers. Idempotent in both
    /// directions, so re-saving a status can never double a clawback or re-flip a restored line.
    /// </summary>
    public static async Task FlipSignAsync(IApplicationDbContext db, Sale sale, bool clawedBack, CancellationToken ct = default)
    {
        var lines = await db.CommissionEntries.IgnoreQueryFilters()
            .Where(c => c.SaleId == sale.Id && c.AgencyId == sale.AgencyId && !c.Paid && !c.IsDeleted)
            .ToListAsync(ct);
        foreach (var line in lines)
            line.Amount = SignedAmount(line.Amount, clawedBack);
    }

    /// <summary>
    /// The amount an unpaid line should hold for an outcome. Idempotent in BOTH directions:
    /// applying it twice is the same as applying it once.
    /// </summary>
    public static decimal SignedAmount(decimal amount, bool clawedBack) =>
        clawedBack
            ? (amount > 0 ? -amount : amount)
            : (amount < 0 ? -amount : amount);
}
