using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CRM.Api.IntegrationTests;

internal static class TestHelpers
{
    /// <summary>Shared HMAC secret the factory configures for the dialer + inbound webhooks.</summary>
    public const string WebhookSecret = "test-webhook-secret-0123456789abcdef";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>
    /// POSTs a webhook payload with a valid X-Signature (hex HMAC-SHA256 of the raw body,
    /// matching the dialer/inbound controllers). Serializes the payload ourselves so the
    /// signed bytes are exactly what the server reads.
    /// </summary>
    public static Task<HttpResponseMessage> PostSignedAsync(this HttpClient client, string url, object payload, string secret = WebhookSecret)
    {
        var json = JsonSerializer.Serialize(payload);
        using var h = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToHexString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-Signature", sig);
        return client.SendAsync(req);
    }

    public static async Task<HttpClient> LoginAsync(this CrmWebAppFactory factory, string userNameOrEmail, string password)
    {
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { userNameOrEmail, password });
        if (!resp.IsSuccessStatusCode)
        {
            var raw = await resp.Content.ReadAsStringAsync();
            throw new Exception($"Login failed for '{userNameOrEmail}': {(int)resp.StatusCode} {raw}");
        }
        var raw2 = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw2);
        var token = doc.RootElement.GetProperty("accessToken").GetString();
        if (string.IsNullOrWhiteSpace(token))
            throw new Exception($"Login OK but no accessToken: {raw2}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static Task<HttpClient> LoginAdminAsync(this CrmWebAppFactory factory)
        => factory.LoginAsync("admin", "Admin@123!");

    public static async Task<JsonElement> PostJsonAsync(this HttpClient client, string url, object body)
    {
        var resp = await client.PostAsJsonAsync(url, body);
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            var auth = client.DefaultRequestHeaders.Authorization?.ToString() ?? "(none)";
            var www = string.Join(",", resp.Headers.WwwAuthenticate.Select(h => h.ToString()));
            throw new Exception($"POST {url} → {(int)resp.StatusCode}\nAuth: {auth.Substring(0, Math.Min(30, auth.Length))}...\nWWW: {www}\nBody: {raw}");
        }
        return string.IsNullOrWhiteSpace(raw)
            ? default
            : JsonDocument.Parse(raw).RootElement.Clone();
    }

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string url)
    {
        var resp = await client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(raw).RootElement.Clone();
    }
}
