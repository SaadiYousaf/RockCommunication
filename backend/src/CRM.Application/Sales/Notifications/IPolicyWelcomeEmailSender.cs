namespace CRM.Application.Sales.Notifications;

/// <summary>
/// Sends the customer a branded welcome/onboarding email when their policy is approved — showing the
/// approved carrier and coverage, the agency's logo, and the agency as the sender (reply-to). Wholly
/// best-effort by contract: a mail failure must never block the approval that triggered it.
/// </summary>
public interface IPolicyWelcomeEmailSender
{
    /// <returns>True if the email was dispatched (so the caller can stamp it sent and not retry).</returns>
    Task<bool> SendAsync(PolicyWelcomeEmailRequest request, CancellationToken ct = default);
}

/// <param name="AgencyId">Agency whose branding (name, logo, reply-to) the email carries.</param>
/// <param name="CustomerEmail">The customer's email address (validated non-empty by the caller).</param>
/// <param name="CustomerName">The customer's display name, for the greeting.</param>
/// <param name="Carrier">Carrier the policy was approved with.</param>
/// <param name="Coverage">Approved coverage / face amount (null if not captured).</param>
/// <param name="Premium">Approved monthly premium (null if not captured).</param>
/// <param name="Plan">Approved plan name (null if not captured).</param>
public record PolicyWelcomeEmailRequest(
    Guid AgencyId,
    string CustomerEmail,
    string CustomerName,
    string Carrier,
    decimal? Coverage,
    decimal? Premium,
    string? Plan);
