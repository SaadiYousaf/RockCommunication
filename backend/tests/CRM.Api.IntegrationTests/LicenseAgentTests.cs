using System.Net;
using System.Net.Http.Json;

namespace CRM.Api.IntegrationTests;

public class LicenseAgentTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public LicenseAgentTests(CrmWebAppFactory factory) => _factory = factory;

    private static Task<HttpClient> SuperAdmin(CrmWebAppFactory f) => f.LoginAsync("superadmin", "SuperAdmin@123!");

    [Fact]
    public async Task SuperAdmin_can_provision_and_list_submission_and_license_agents()
    {
        var sa = await SuperAdmin(_factory);

        // Central submission agent (SMH-level).
        var subEmail = $"sub-{Guid.NewGuid():N}@crm.local";
        var sub = await sa.PostJsonAsync("/api/agencies/submission-agents", new { name = "Sam Submit", email = subEmail });
        Assert.NotEqual(Guid.Empty, sub.GetProperty("id").GetGuid());

        var subs = await sa.GetJsonAsync("/api/agencies/submission-agents");
        Assert.Contains(subs.EnumerateArray(), a => a.GetProperty("email").GetString() == subEmail);

        // License agent inside an agency.
        var agencies = await sa.GetJsonAsync("/api/agencies");
        var agencyId = agencies.EnumerateArray().First().GetProperty("id").GetGuid();

        var laEmail = $"la-{Guid.NewGuid():N}@crm.local";
        var la = await sa.PostJsonAsync($"/api/agencies/{agencyId}/license-agents", new { name = "Lee Agent", email = laEmail });
        Assert.NotEqual(Guid.Empty, la.GetProperty("id").GetGuid());

        var agents = await sa.GetJsonAsync($"/api/agencies/{agencyId}/license-agents");
        Assert.Contains(agents.EnumerateArray(), a => a.GetProperty("email").GetString() == laEmail);

        // Agency options power the approval popup's Agency picker.
        var options = await sa.GetJsonAsync("/api/agencies/options");
        Assert.Contains(options.EnumerateArray(), o => o.GetProperty("id").GetGuid() == agencyId);
    }

    [Fact]
    public async Task Agency_admin_cannot_provision_license_agents_or_list_submission_agents()
    {
        var admin = await _factory.LoginAdminAsync();
        var me = await admin.GetJsonAsync("/api/auth/me");
        var agencyId = me.GetProperty("agencyId").GetGuid();

        var create = await admin.PostAsJsonAsync($"/api/agencies/{agencyId}/license-agents",
            new { name = "Nope", email = $"nope-{Guid.NewGuid():N}@crm.local" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var subs = await admin.GetAsync("/api/agencies/submission-agents");
        Assert.Equal(HttpStatusCode.Forbidden, subs.StatusCode);
    }

    [Fact]
    public async Task Approval_assigns_license_agent_pays_commission_and_stamps_serials()
    {
        var admin = await _factory.LoginAdminAsync();
        var me = await admin.GetJsonAsync("/api/auth/me");
        var agencyId = me.GetProperty("agencyId").GetGuid();

        // A License Agent in this agency (provisioned by SuperAdmin).
        var sa = await SuperAdmin(_factory);
        var la = await sa.PostJsonAsync($"/api/agencies/{agencyId}/license-agents",
            new { name = "Assign Me", email = $"la-{Guid.NewGuid():N}@crm.local" });
        var licenseAgentId = la.GetProperty("id").GetGuid();

        // A validator (Submission Agent) in this agency, with a known password so we can act as them.
        var valName = $"val{Guid.NewGuid():N}".Substring(0, 14);
        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{valName}@crm.local", userName = valName,
            password = "Val@1234!", agencyId, roles = new[] { "Validator" }
        });
        var validator = await _factory.LoginAsync(valName, "Val@1234!");

        var firstSale = await RecordSaleAsync(admin, "5550000001");
        var secondSale = await RecordSaleAsync(admin, "5550000002");

        // Guard: assigning a non-License-Agent (the admin) is rejected.
        var badApprove = await validator.PostAsJsonAsync($"/api/intake/validate/{firstSale}/status", new
        {
            status = "Approved", carrierApproved = "AETNA", coverageApproved = 25000m,
            premiumApproved = 250m, planApproved = "PlanA",
            licenseAgentUserId = me.GetProperty("id").GetGuid()
        });
        Assert.Equal(HttpStatusCode.BadRequest, badApprove.StatusCode);

        // Valid approval assigns the license agent.
        await validator.PostJsonAsync($"/api/intake/validate/{firstSale}/status", new
        {
            status = "Approved", carrierApproved = "AETNA", coverageApproved = 25000m,
            premiumApproved = 250m, planApproved = "PlanA",
            licenseAgentUserId = licenseAgentId
        });

        // The sales list surfaces the assignment, commission and per-agency serials.
        var sales = await admin.GetJsonAsync("/api/sales?take=100");
        var items = sales.GetProperty("items").EnumerateArray().ToList();

        var first = items.First(i => i.GetProperty("id").GetGuid() == firstSale);
        Assert.False(first.GetProperty("licenseAgentUserId").ValueKind == System.Text.Json.JsonValueKind.Null,
            "license agent should be assigned");
        Assert.True(first.GetProperty("commissionEarned").GetDecimal() > 0, "license agent commission should be recorded");

        var s1 = items.First(i => i.GetProperty("id").GetGuid() == firstSale).GetProperty("saleNumber").GetInt32();
        var s2 = items.First(i => i.GetProperty("id").GetGuid() == secondSale).GetProperty("saleNumber").GetInt32();
        Assert.True(s1 > 0 && s2 > 0 && s1 != s2, $"per-agency serials must be positive and distinct (got {s1}, {s2})");
    }

    [Fact]
    public async Task Agency_admin_can_list_license_agents_and_options_for_the_approval_popup()
    {
        // Regression: the approval popup's License-Agent + Agency pickers 403'd for a plain agency
        // Admin (the endpoints were role-gated to SuperAdmin,Validator only), so the dropdown was
        // always empty. They're now gated by SalesValidate, which an Admin holds.
        var admin = await _factory.LoginAdminAsync();
        var me = await admin.GetJsonAsync("/api/auth/me");
        var agencyId = me.GetProperty("agencyId").GetGuid();

        var agents = await admin.GetAsync($"/api/agencies/{agencyId}/license-agents");
        Assert.Equal(HttpStatusCode.OK, agents.StatusCode);

        var options = await admin.GetAsync("/api/agencies/options");
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        // A plain agency admin sees only their own agency in the picker (can't reassign cross-tenant).
        var opts = await options.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Contains(opts.EnumerateArray(), o => o.GetProperty("id").GetGuid() == agencyId);
    }

    [Fact]
    public async Task License_agent_sees_their_assigned_sale_commission_and_dashboard()
    {
        var admin = await _factory.LoginAdminAsync();
        var me = await admin.GetJsonAsync("/api/auth/me");
        var agencyId = me.GetProperty("agencyId").GetGuid();

        // A License Agent we can log in as (registered with a known password + the LicenseAgent role).
        var laName = $"la{Guid.NewGuid():N}".Substring(0, 14);
        var reg = await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{laName}@crm.local", userName = laName,
            password = "Agent@1234!", agencyId, roles = new[] { "LicenseAgent" }
        });
        var licenseAgentId = reg.GetProperty("id").GetGuid();

        // Record a sale and approve it, assigning this license agent.
        var saleId = await RecordSaleAsync(admin, "5551230001");
        await admin.PostJsonAsync($"/api/intake/validate/{saleId}/status", new
        {
            status = "Approved", carrierApproved = "AETNA", coverageApproved = 25000m,
            premiumApproved = 250m, planApproved = "PlanA", licenseAgentUserId = licenseAgentId
        });

        var agent = await _factory.LoginAsync(laName, "Agent@1234!");

        // 1) The assigned sale appears in the license agent's own Sales list (scoped by
        //    LicenseAgentUserId, not CloserUserId — they are never the closer).
        var sales = await agent.GetJsonAsync("/api/sales?take=100");
        var items = sales.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("id").GetGuid() == saleId);
        Assert.True(sales.GetProperty("totalPremium").GetDecimal() > 0, "total premium must sum (SQLite decimal-SUM guard)");

        // 2) The license-agent-approval commission appears in their Commissions (default date window
        //    is inclusive of today — a commission earned today must not be dropped by an exclusive end).
        var commissions = await agent.GetJsonAsync("/api/sales/commissions");
        Assert.Contains(commissions.EnumerateArray(),
            c => c.GetProperty("saleId").GetGuid() == saleId && c.GetProperty("amount").GetDecimal() > 0);

        // 3) The dashboard summary loads (previously 500'd on the SQL-side decimal SUM for any tenant
        //    that had sales).
        var summary = await agent.GetAsync("/api/dashboard/summary");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
    }

    [Fact]
    public async Task Wallboard_and_leaderboard_load_with_sales_present()
    {
        // Regression: the wallboard "top agents" + leaderboard did a SQL-side SUM over the
        // TEXT-stored decimal premium, which 500'd both supervisory widgets once any sale existed
        // in the window. Record a sale today, then confirm both endpoints return 200.
        var admin = await _factory.LoginAdminAsync();
        await RecordSaleAsync(admin, "5559990001");

        var wallboard = await admin.GetAsync("/api/cc/wallboard");
        Assert.Equal(HttpStatusCode.OK, wallboard.StatusCode);

        var leaderboard = await admin.GetAsync("/api/cc/leaderboard?period=today");
        Assert.Equal(HttpStatusCode.OK, leaderboard.StatusCode);
    }

    /// <summary>Drives a lead through New→Fronted→Verified and records a clean sale; returns the sale id.</summary>
    private async Task<Guid> RecordSaleAsync(HttpClient admin, string phone)
    {
        var lead = await admin.PostJsonAsync("/api/leads", new
        {
            firstName = "Serial", lastName = "Case", phoneNumber = phone, email = $"{phone}@crm.local"
        });
        var leadId = lead.GetProperty("id").GetGuid();
        await admin.PostJsonAsync($"/api/leads/{leadId}/transition", new { toStage = "Fronted", disposition = "Interested" });
        await admin.PostJsonAsync($"/api/leads/{leadId}/transition", new { toStage = "Verified", disposition = "Interested" });
        var sale = await admin.PostJsonAsync("/api/sales", new
        {
            leadId, carrier = "AETNA", policyNumber = "POL-LA", monthlyPremium = 250m,
            routingNumber = "011000015", accountNumber = "1234567800", accountType = "checking"
        });
        return sale.GetProperty("id").GetGuid();
    }
}
