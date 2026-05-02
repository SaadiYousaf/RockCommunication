using CRM.Application.Common.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace CRM.Infrastructure.Integrations.Trello;

public class TrelloOptions
{
    public string Provider { get; set; } = "Stub";
    public string? Key { get; set; }
    public string? Token { get; set; }
    public string BaseUrl { get; set; } = "https://api.trello.com/1";
}

public class StubTrelloProvider : ITrelloProvider
{
    private readonly ILogger<StubTrelloProvider> _logger;
    public StubTrelloProvider(ILogger<StubTrelloProvider> logger) => _logger = logger;
    public Task<TrelloCardResult> CreateCardAsync(TrelloCardRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Trello stub: create card '{Title}' on {ListId}", request.Title, request.ListId);
        var id = Guid.NewGuid().ToString("N");
        return Task.FromResult(new TrelloCardResult(true, id, $"https://trello.com/c/{id}", null));
    }
}

public class HttpTrelloProvider : ITrelloProvider
{
    private readonly HttpClient _http;
    private readonly TrelloOptions _opts;
    private readonly ILogger<HttpTrelloProvider> _logger;

    public HttpTrelloProvider(HttpClient http, IOptions<TrelloOptions> opts, ILogger<HttpTrelloProvider> logger)
    {
        _http = http;
        _opts = opts.Value;
        _logger = logger;
        _http.BaseAddress = new Uri(_opts.BaseUrl);
    }

    public async Task<TrelloCardResult> CreateCardAsync(TrelloCardRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_opts.Key) || string.IsNullOrEmpty(_opts.Token))
            return new TrelloCardResult(false, null, null, "Trello key/token not configured");
        try
        {
            var query = $"?key={_opts.Key}&token={_opts.Token}&idList={Uri.EscapeDataString(request.ListId)}" +
                        $"&name={Uri.EscapeDataString(request.Title)}" +
                        (string.IsNullOrEmpty(request.Description) ? "" : $"&desc={Uri.EscapeDataString(request.Description)}");
            using var resp = await _http.PostAsync($"/cards{query}", null, ct);
            if (!resp.IsSuccessStatusCode)
                return new TrelloCardResult(false, null, null, $"HTTP {(int)resp.StatusCode}");
            var body = await resp.Content.ReadFromJsonAsync<TrelloApiCardDto>(cancellationToken: ct);
            return new TrelloCardResult(true, body?.Id, body?.ShortUrl, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Trello card create failed");
            return new TrelloCardResult(false, null, null, ex.Message);
        }
    }

    private record TrelloApiCardDto(string? Id, string? ShortUrl);
}
