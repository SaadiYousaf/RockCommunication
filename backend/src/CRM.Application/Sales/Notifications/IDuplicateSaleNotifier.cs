namespace CRM.Application.Sales.Notifications;

/// <summary>
/// Alerts oversight when a closer attempts to record a sale for a lead that already has one.
/// Recipients: the sale's call-centre admin(s), every Super Admin, and the attempting closer's
/// team lead. Best-effort by contract — a delivery failure must never block the duplicate
/// rejection that triggered it.
/// </summary>
public interface IDuplicateSaleNotifier
{
    Task NotifyAsync(DuplicateSaleAttempt attempt, CancellationToken ct = default);
}

/// <summary>A rejected attempt to record a second sale on a lead that already has one.</summary>
/// <param name="AgencyId">Agency the lead/sale belongs to.</param>
/// <param name="CallCenterId">Call centre the lead belongs to (null when it has none).</param>
/// <param name="CloserUserId">The agent who attempted the duplicate.</param>
/// <param name="LeadId">The lead the duplicate was attempted on (used to deep-link the alert).</param>
/// <param name="LeadName">Display name of the lead, for the alert body.</param>
public record DuplicateSaleAttempt(
    Guid AgencyId,
    Guid? CallCenterId,
    Guid CloserUserId,
    Guid LeadId,
    string LeadName);
