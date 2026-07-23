using System.Net;
using System.Net.Http.Json;

namespace CRM.Api.IntegrationTests;

public class CallCenterTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public CallCenterTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Wrap_up_codes_round_trip()
    {
        var admin = await _factory.LoginAdminAsync();
        var put = await admin.PutAsJsonAsync("/api/cc/wrap-up-codes", new
        {
            id = (Guid?)null, code = "SALE", label = "Sale", isSale = true, isContact = true, isRetry = false, isActive = true
        });
        put.EnsureSuccessStatusCode();
        var list = await admin.GetJsonAsync("/api/cc/wrap-up-codes");
        Assert.Contains(list.EnumerateArray(), e => e.GetProperty("code").GetString() == "SALE");
    }

    [Fact]
    public async Task Dnc_blocks_compliance_check()
    {
        var admin = await _factory.LoginAdminAsync();

        // Add a DNC entry
        var added = await admin.PostJsonAsync("/api/cc/dnc", new { phone = "5551234567", reason = "Customer requested" });
        Assert.Equal("5551234567", added.GetProperty("phoneNormalized").GetString());

        // Compliance check during permitted hours uses lead state and may still fail TCPA when run outside 8-9pm.
        // To make the test deterministic we just verify DNC blocks regardless of TCPA.
        var resp = await admin.PostAsJsonAsync("/api/cc/compliance/check", new { phone = "555-123-4567", state = (string?)null });
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ComplianceResult>();
        Assert.False(result!.Allowed);
        Assert.Contains("DNC", result.BlockReason);
    }

    [Fact]
    public async Task Agent_session_clock_in_status_clock_out()
    {
        var admin = await _factory.LoginAdminAsync();

        var clockIn = await admin.PostJsonAsync("/api/cc/clock-in", new { });
        Assert.NotEqual(Guid.Empty, clockIn.GetProperty("id").GetGuid());

        var setStatus = await admin.PostAsJsonAsync("/api/cc/status", new { status = "Break", reason = "10-min" });
        setStatus.EnsureSuccessStatusCode();

        var session = await admin.GetJsonAsync("/api/cc/session");
        Assert.Equal("Break", session.GetProperty("currentStatus").GetString());

        var clockOut = await admin.PostJsonAsync("/api/cc/clock-out", new { });
        Assert.True(clockOut.GetProperty("clockOutAt").ValueKind != System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Cannot_go_available_with_unwrapped_call()
    {
        var admin = await _factory.LoginAdminAsync();
        await admin.PostJsonAsync("/api/cc/clock-in", new { });

        // Need a wrap-up code to wrap up later (not used here, just defensive)
        await admin.PutAsJsonAsync("/api/cc/wrap-up-codes", new
        {
            id = (Guid?)null, code = "NA", label = "No answer", isSale = false, isContact = false, isRetry = true, isActive = true
        });

        // Simulate a call ended without wrap-up via webhook
        var lead = await admin.PostJsonAsync("/api/leads", new { firstName = "X", lastName = "Y", phoneNumber = "5550001111" });
        var leadId = lead.GetProperty("id").GetGuid();
        var me = await admin.GetJsonAsync("/api/auth/me");
        var agentId = me.GetProperty("id").GetGuid();
        var agencyId = me.GetProperty("agencyId").GetGuid();

        var hook = await admin.PostSignedAsync("/api/webhooks/dialer", new
        {
            provider = "Vici", providerCallId = "test-call-1", eventType = "ended",
            occurredAt = DateTime.UtcNow, agencyId, agentUserId = agentId, leadId
        });
        hook.EnsureSuccessStatusCode();

        // Try to go Available — should 409
        var resp = await admin.PostAsJsonAsync("/api/cc/status", new { status = "Available", reason = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);

        // Wrap up the call
        var calls = await admin.GetAsync("/api/cc/wrap-up-codes");
        var codes = await calls.Content.ReadFromJsonAsync<List<WrapCodeDto>>();
        var callId = await GetCallId(admin, "test-call-1");
        await admin.PostAsJsonAsync($"/api/cc/calls/{callId}/wrap-up", new { wrapUpCode = "NA", notes = "no answer" });

        // Now Available works
        var resp2 = await admin.PostAsJsonAsync("/api/cc/status", new { status = "Available", reason = (string?)null });
        resp2.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Webhook_rejects_bad_signature_when_secret_set()
    {
        // A secret is configured — a bad signature must be rejected (fail-closed HMAC).
        var client = _factory.CreateClient();
        var bad = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/dialer")
        {
            Content = new StringContent("{\"provider\":\"Vici\",\"providerCallId\":\"abc\"}",
                System.Text.Encoding.UTF8, "application/json")
        };
        bad.Headers.Add("X-Signature", "not-a-valid-signature");
        var badResp = await client.SendAsync(bad);
        Assert.Equal(HttpStatusCode.Unauthorized, badResp.StatusCode);

        // A correctly-signed payload gets past auth into the handler (Accepted, or Conflict
        // for the unknown agency) — proving the signature check, not a blanket reject.
        var ok = await client.PostSignedAsync("/api/webhooks/dialer", new
        {
            provider = "Vici", providerCallId = "abc2", eventType = "answered",
            occurredAt = DateTime.UtcNow, agencyId = Guid.NewGuid(),
            agentUserId = Guid.NewGuid(), leadId = Guid.NewGuid()
        });
        Assert.True(ok.StatusCode == HttpStatusCode.Accepted || ok.StatusCode == HttpStatusCode.Conflict);
    }

    private static async Task<Guid> GetCallId(HttpClient client, string providerCallId)
    {
        // No /api/cc/calls list endpoint yet — query DB-equivalent via /api/leads timeline isn't ideal.
        // Workaround: re-issue the same webhook (idempotent) and return a deterministic guid lookup via an
        // endpoint we add — for now, create a dedicated tiny endpoint or use a search.
        // Quick path: call the timeline of any lead → call records appear; simpler is to expose a debug list later.
        // For test, fetch from chat unread (won't work) — instead use a direct EF query via service.
        // Pragmatic: use HttpClient to GET /api/cc/calls?providerCallId=... — we'll add it.
        var resp = await client.GetAsync($"/api/cc/calls/by-provider?providerCallId={providerCallId}");
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<CallSummaryDto>();
        return doc!.Id;
    }

    private record ComplianceResult(bool Allowed, string? BlockReason, List<string> Warnings);
    private record WrapCodeDto(Guid Id, string Code, string Label, bool IsSale, bool IsContact, bool IsRetry, bool IsActive);
    private record CallSummaryDto(Guid Id, string Provider, string ProviderCallId, string Status);
}
