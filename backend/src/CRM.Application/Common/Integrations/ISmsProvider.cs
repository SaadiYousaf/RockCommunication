namespace CRM.Application.Common.Integrations;

public record SmsMessage(string To, string Body, string? FromName = null);
public record SmsResult(bool Sent, string? ProviderMessageId, string? Reason);

public interface ISmsProvider
{
    string Name { get; }
    Task<SmsResult> SendAsync(SmsMessage message, CancellationToken ct = default);
}

/// <summary>
/// A file attached to an email (e.g. an .ics calendar invite). When <paramref name="ContentId"/> is
/// set the file is embedded INLINE (a linked resource) instead of as a download, so the HTML body can
/// reference it with <c>&lt;img src="cid:THE-CONTENT-ID"&gt;</c> — the reliable way to show a logo in
/// email (remote-image blocking and data: URIs are widely stripped by mail clients).
/// </summary>
public record EmailAttachment(string FileName, string ContentType, byte[] Content, string? ContentId = null);

public record EmailMessage(
    string To, string Subject, string Body, bool IsHtml = false, string? FromName = null,
    IReadOnlyList<EmailAttachment>? Attachments = null,
    /// <summary>Reply-To address (e.g. the agency's own inbox) — send still goes via the shared relay.</summary>
    string? ReplyTo = null);
public record EmailResult(bool Sent, string? ProviderMessageId, string? Reason);

public interface IEmailProvider
{
    string Name { get; }
    Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}

public record FundingRequest(Guid SaleId, string PolicyNumber, decimal Amount, string CarrierCode);
public record FundingResult(bool Accepted, string? FundingReferenceId, string Status, string? Reason);

public interface IFundingProvider
{
    Task<FundingResult> SubmitAsync(FundingRequest request, CancellationToken ct = default);
    Task<FundingResult> GetStatusAsync(string fundingReferenceId, CancellationToken ct = default);
}
