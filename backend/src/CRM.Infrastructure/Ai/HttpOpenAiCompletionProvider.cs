using CRM.Application.Common.Ai;
using CRM.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CRM.Infrastructure.Ai;

/// <summary>
/// Real OpenAI Chat Completions provider. Compatible with any OpenAI-shaped API
/// (Anthropic-via-proxy, Azure OpenAI, Groq, OpenRouter, vLLM, Together, etc.) by
/// changing <c>Ai:BaseUrl</c>.
/// </summary>
public class HttpOpenAiCompletionProvider : IAiCompletionProvider
{
    private readonly HttpClient _http;
    private readonly AiOptions _opts;
    private readonly ILogger<HttpOpenAiCompletionProvider> _logger;

    public HttpOpenAiCompletionProvider(HttpClient http, IOptions<AiOptions> opts, ILogger<HttpOpenAiCompletionProvider> logger)
    {
        _http = Guard.AgainstNull(http);
        _opts = Guard.AgainstNull(opts).Value;
        _logger = Guard.AgainstNull(logger);

        _http.BaseAddress = new Uri(string.IsNullOrEmpty(_opts.BaseUrl) ? "https://api.openai.com" : _opts.BaseUrl.TrimEnd('/'));
        if (!string.IsNullOrEmpty(_opts.ApiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        Guard.AgainstNull(systemPrompt);
        Guard.AgainstNull(userPrompt);
        if (string.IsNullOrEmpty(_opts.ApiKey))
        {
            _logger.LogWarning("OpenAI provider hit but Ai:ApiKey not configured — returning placeholder.");
            return $"[ai-disabled] {Trim(userPrompt, 200)}";
        }

        var request = new ChatCompletionRequest(
            Model: string.IsNullOrEmpty(_opts.Model) ? "gpt-4o-mini" : _opts.Model,
            Messages: new[]
            {
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", userPrompt),
            },
            Temperature: 0.4,
            MaxTokens: 600);

        try
        {
            var resp = await _http.PostAsJsonAsync("/v1/chat/completions", request, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("OpenAI {Status}: {Body}", resp.StatusCode, Trim(body, 300));
                return $"[ai-error-{(int)resp.StatusCode}]";
            }
            var parsed = await resp.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: ct);
            return parsed?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "[ai-empty]";
        }
        catch (TaskCanceledException) { _logger.LogWarning("OpenAI request timed out"); return "[ai-timeout]"; }
        catch (Exception ex) { _logger.LogError(ex, "OpenAI request failed"); return "[ai-error]"; }
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

    private record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private record ChatCompletionResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<Choice>? Choices);

    private record Choice(
        [property: JsonPropertyName("message")] ChoiceMessage? Message);

    private record ChoiceMessage(
        [property: JsonPropertyName("content")] string? Content);
}
