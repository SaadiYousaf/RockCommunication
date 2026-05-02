namespace CRM.Application.Common.Integrations;

public record DialResult(string CallId, string Status, DateTime InitiatedAt);

public interface IDialerProvider
{
    string Name { get; }
    Task<DialResult> DialAsync(Guid agentId, string phoneNumber, Guid leadId, CancellationToken ct = default);
    Task HangupAsync(string callId, CancellationToken ct = default);
    Task<string> GetStatusAsync(string callId, CancellationToken ct = default);
}
