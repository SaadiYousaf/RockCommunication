using System.Linq;
using System.Net.Http.Json;

namespace CRM.Api.IntegrationTests;

public class LeadLifecycleTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public LeadLifecycleTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_transition_record_sale_validate_fund_full_flow()
    {
        var admin = await _factory.LoginAdminAsync();

        // Get agency id from /me
        var me = await admin.GetJsonAsync("/api/auth/me");
        var agencyId = me.GetProperty("agencyId").GetGuid();

        // Register a validator user
        var validatorEmail = $"val-{Guid.NewGuid():N}@crm.local";
        var validatorName = $"validator-{Guid.NewGuid():N}".Substring(0, 16);
        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = validatorEmail, userName = validatorName,
            password = "Val@1234!", agencyId, roles = new[] { "Validator" }
        });

        // Create lead
        var lead = await admin.PostJsonAsync("/api/leads", new
        {
            firstName = "Test", lastName = "Lead", phoneNumber = "5550001122", email = "test@crm.local"
        });
        var leadId = lead.GetProperty("id").GetGuid();

        // Transition: New → Fronted → Verified
        await admin.PostJsonAsync($"/api/leads/{leadId}/transition", new { toStage = "Fronted", disposition = "Interested" });
        await admin.PostJsonAsync($"/api/leads/{leadId}/transition", new { toStage = "Verified", disposition = "Interested" });

        // Record sale (admin acts as closer)
        // Bank details are validated by the stub Lyons validator: a valid ABA routing
        // number (011000015) with an account not ending in 9 clears to banking code 103.
        var sale = await admin.PostJsonAsync("/api/sales", new
        {
            leadId, carrier = "AETNA", policyNumber = "POL-T1", monthlyPremium = 250m,
            routingNumber = "011000015", accountNumber = "1234567800", accountType = "checking"
        });
        var saleId = sale.GetProperty("id").GetGuid();

        // Validator approves. With policy-funding automation on (the default), an approved sale is
        // auto-funded immediately, so it is already Funded once validation returns.
        var validatorClient = await _factory.LoginAsync(validatorName, "Val@1234!");
        var validated = await validatorClient.PostJsonAsync($"/api/sales/{saleId}/validate", new { approve = true, notes = "Approved" });
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, validated.GetProperty("fundedAt").ValueKind);

        // Funding is idempotent: manually re-funding an already-funded sale is rejected (409),
        // never re-submitted to the funding provider.
        var refund = await admin.PostAsJsonAsync($"/api/sales/{saleId}/fund", new { });
        Assert.Equal(System.Net.HttpStatusCode.Conflict, refund.StatusCode);

        // Timeline includes stage changes
        var timeline = await admin.GetJsonAsync($"/api/leads/{leadId}/timeline");
        Assert.True(timeline.GetProperty("entries").GetArrayLength() >= 3);

        // Dashboard reflects funded sale
        var resp = await admin.GetAsync($"/api/dashboard?from=2026-01-01&to=2027-01-01&metrics=sales.funded");
        resp.EnsureSuccessStatusCode();
        var metrics = await resp.Content.ReadFromJsonAsync<List<MetricValue>>();
        Assert.Contains(metrics!, m => m.Key == "sales.funded" && m.Value >= 1);
    }

    [Fact]
    public async Task Universal_script_shows_for_every_stage()
    {
        var admin = await _factory.LoginAdminAsync();

        (await admin.PutAsJsonAsync("/api/cc/scripts", new { name = "Universal opener", body = "Hi", isActive = true }))
            .EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync("/api/cc/scripts", new { name = "Verified only", stage = "Verified", body = "V", isActive = true }))
            .EnsureSuccessStatusCode();

        // Querying by stage must include the Universal (null-stage) script, not just the exact-stage one.
        var verified = await admin.GetJsonAsync("/api/cc/scripts?stage=Verified");
        var vNames = verified.EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToList();
        Assert.Contains("Universal opener", vNames);
        Assert.Contains("Verified only", vNames);

        // A different stage still shows the Universal one, but not the Verified-only one.
        var fronted = await admin.GetJsonAsync("/api/cc/scripts?stage=Fronted");
        var fNames = fronted.EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToList();
        Assert.Contains("Universal opener", fNames);
        Assert.DoesNotContain("Verified only", fNames);
    }

    [Fact]
    public async Task Invalid_stage_transition_returns_409()
    {
        var admin = await _factory.LoginAdminAsync();
        var lead = await admin.PostJsonAsync("/api/leads", new
        {
            firstName = "Bad", lastName = "Trans", phoneNumber = "5550009999"
        });
        var leadId = lead.GetProperty("id").GetGuid();

        var resp = await admin.PostAsJsonAsync($"/api/leads/{leadId}/transition", new
        {
            toStage = "Funded", disposition = "Interested"
        });
        Assert.Equal(System.Net.HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Record_sale_is_blocked_when_lyons_rejects_the_bank_account()
    {
        var admin = await _factory.LoginAdminAsync();
        var lead = await admin.PostJsonAsync("/api/leads", new
        {
            firstName = "Bank", lastName = "Blocked", phoneNumber = "5550007777"
        });
        var leadId = lead.GetProperty("id").GetGuid();
        await admin.PostJsonAsync($"/api/leads/{leadId}/transition", new { toStage = "Fronted", disposition = "Interested" });
        await admin.PostJsonAsync($"/api/leads/{leadId}/transition", new { toStage = "Verified", disposition = "Interested" });

        // Invalid ABA routing number → stub Lyons blocks the sale (409).
        var blocked = await admin.PostAsJsonAsync("/api/sales", new
        {
            leadId, carrier = "AETNA", monthlyPremium = 100m,
            routingNumber = "123456789", accountNumber = "1234567800", accountType = "checking"
        });
        Assert.Equal(System.Net.HttpStatusCode.Conflict, blocked.StatusCode);
    }

    [Fact]
    public async Task Record_sale_flagged_by_lyons_requires_a_recording()
    {
        var admin = await _factory.LoginAdminAsync();
        var lead = await admin.PostJsonAsync("/api/leads", new
        {
            firstName = "Bank", lastName = "Flagged", phoneNumber = "5550008888"
        });
        var leadId = lead.GetProperty("id").GetGuid();
        await admin.PostJsonAsync($"/api/leads/{leadId}/transition", new { toStage = "Fronted", disposition = "Interested" });
        await admin.PostJsonAsync($"/api/leads/{leadId}/transition", new { toStage = "Verified", disposition = "Interested" });

        // Valid routing but account ending in 9 → stub Lyons returns 198 (needs recording).
        var flagged = await admin.PostAsJsonAsync("/api/sales", new
        {
            leadId, carrier = "AETNA", monthlyPremium = 100m,
            routingNumber = "011000015", accountNumber = "1234567809", accountType = "checking"
        });
        Assert.Equal(System.Net.HttpStatusCode.Conflict, flagged.StatusCode);
    }

    private record MetricValue(string Key, string Label, decimal Value, string? Unit, string? Group);
}
