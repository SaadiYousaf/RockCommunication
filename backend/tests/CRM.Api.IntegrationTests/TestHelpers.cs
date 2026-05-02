using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CRM.Api.IntegrationTests;

internal static class TestHelpers
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

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
