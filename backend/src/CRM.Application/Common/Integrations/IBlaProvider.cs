namespace CRM.Application.Common.Integrations;

public record BlaQuoteRequest(string FirstName, string LastName, string Phone, string? State, DateTime DateOfBirth);
public record BlaQuoteResult(bool Eligible, decimal? EstimatedPremium, string? CarrierMatch, string? Reason);

public interface IBlaProvider
{
    Task<BlaQuoteResult> GetQuoteAsync(BlaQuoteRequest request, CancellationToken ct = default);
}

public record TrelloCardRequest(string ListId, string Title, string? Description, string[]? Labels);
public record TrelloCardResult(bool Created, string? CardId, string? Url, string? Reason);

public interface ITrelloProvider
{
    Task<TrelloCardResult> CreateCardAsync(TrelloCardRequest request, CancellationToken ct = default);
}
