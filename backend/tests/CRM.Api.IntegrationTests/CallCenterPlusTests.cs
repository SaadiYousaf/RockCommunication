using System.Net.Http.Json;

namespace CRM.Api.IntegrationTests;

public class CallCenterPlusTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public CallCenterPlusTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task LeadList_create_and_csv_import_works()
    {
        var admin = await _factory.LoginAdminAsync();
        var list = await admin.PutAsJsonAsync("/api/cc/lists", new
        {
            id = (Guid?)null, name = $"List-{Guid.NewGuid():N}".Substring(0, 16),
            campaignId = (Guid?)null, leadSourceId = (Guid?)null, isActive = true
        });
        list.EnsureSuccessStatusCode();
        var listDoc = await list.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var listId = listDoc.GetProperty("id").GetGuid();

        var csv = "firstname,lastname,phone,email,state\nAnn,Test,5551234001,a@x.com,TX\nBob,Test,5551234002,b@x.com,FL";
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "leads.csv");

        var resp = await admin.PostAsync($"/api/cc/lists/{listId}/import", content);
        resp.EnsureSuccessStatusCode();
        var batch = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(2, batch.GetProperty("imported").GetInt32());
    }

    [Fact]
    public async Task Cadence_round_trip_and_enroll()
    {
        var admin = await _factory.LoginAdminAsync();
        var c = await admin.PutAsJsonAsync("/api/cc/cadences", new
        {
            id = (Guid?)null, name = $"Cadence-{Guid.NewGuid():N}".Substring(0, 16),
            campaignId = (Guid?)null, isActive = true, description = "Test",
            steps = new[]
            {
                new { order = 1, stepKind = "Call", delayMinutes = 0, parametersJson = "{}", stopIfContacted = true },
                new { order = 2, stepKind = "Sms", delayMinutes = 60, parametersJson = "{\"template\":\"Hi {{firstName}}\"}", stopIfContacted = true }
            }
        });
        c.EnsureSuccessStatusCode();
        var cdoc = await c.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var cadenceId = cdoc.GetProperty("id").GetGuid();

        var lead = await admin.PostJsonAsync("/api/leads", new { firstName = "C", lastName = "T", phoneNumber = "5550009111" });
        var leadId = lead.GetProperty("id").GetGuid();

        var enroll = await admin.PostAsJsonAsync("/api/cc/cadences/enroll", new { cadenceId, leadId });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, enroll.StatusCode);

        var enrollments = await admin.GetJsonAsync($"/api/cc/cadences/enrollments?cadenceId={cadenceId}");
        Assert.True(enrollments.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Inbound_route_with_no_queue_returns_decision()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/webhooks/inbound/route", new
        {
            agencyId = Guid.NewGuid(), provider = "Vici", providerCallId = "ic-1",
            fromPhone = "5550001234", dialedNumber = "5550009999"
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("no-queue", body.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Wallboard_returns_today_metrics()
    {
        var admin = await _factory.LoginAdminAsync();
        await admin.PostJsonAsync("/api/cc/clock-in", new { });
        var w = await admin.GetJsonAsync("/api/cc/wallboard");
        Assert.True(w.GetProperty("agentsClockedIn").GetInt32() >= 1);
    }

    [Fact]
    public async Task Knowledge_article_round_trip_and_search()
    {
        var admin = await _factory.LoginAdminAsync();
        var put = await admin.PutAsJsonAsync("/api/kb/articles", new
        {
            id = (Guid?)null, slug = "tcpa-overview", title = "TCPA overview",
            body = "TCPA rules apply between 8 AM and 9 PM local.",
            tags = "compliance,tcpa", category = "Compliance", isPublished = true
        });
        put.EnsureSuccessStatusCode();
        var search = await admin.GetJsonAsync("/api/kb/articles?q=tcpa");
        Assert.True(search.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Public_endpoint_create_and_capture_round_trip()
    {
        var admin = await _factory.LoginAdminAsync();
        var slug = $"web{Guid.NewGuid():N}".Substring(0, 16);
        var create = await admin.PostJsonAsync("/api/admin/public-endpoints", new
        {
            slug, campaignId = (Guid?)null, leadSourceId = (Guid?)null,
            cadenceId = (Guid?)null, allowedOrigins = (string?)null
        });
        var secret = create.GetProperty("secret").GetString()!;

        var payload = new
        {
            firstName = "Web", lastName = "Form", phoneNumber = "5550008877",
            email = "form@ex.com", state = "TX", postalCode = (string?)null,
            source = (string?)null, jornayaLeadId = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var bytesKey = System.Text.Encoding.UTF8.GetBytes(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(secret))).ToLowerInvariant());
        using var hmac = new System.Security.Cryptography.HMACSHA256(bytesKey);
        var sig = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

        var anon = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/public/leads/{slug}")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-Signature", sig);
        var resp = await anon.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, resp.StatusCode);
    }
}
