namespace CRM.Application.Common.Integrations;

public record SmsMessage(string To, string Body, string? FromName = null);
public record SmsResult(bool Sent, string? ProviderMessageId, string? Reason);

public interface ISmsProvider
{
    string Name { get; }
    Task<SmsResult> SendAsync(SmsMessage message, CancellationToken ct = default);
}

public record EmailMessage(string To, string Subject, string Body, bool IsHtml = false, string? FromName = null);
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
