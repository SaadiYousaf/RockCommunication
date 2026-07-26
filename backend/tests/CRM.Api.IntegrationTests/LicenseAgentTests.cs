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

        // 4) The license agent can OPEN the assigned sale's detail — not just see it in the list.
        //    (GetSaleDetail previously scoped to the closer only and 404'd the assigned license agent.)
        var detail = await agent.GetAsync($"/api/sales/{saleId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        // 5) The assignment dispatched a DURABLE in-app notification the agent can see in their inbox
        //    (the persisted-notification read API — previously the rows were write-only).
        var inbox = await agent.GetJsonAsync("/api/notifications");
        Assert.NotEmpty(inbox.EnumerateArray());
        var unreadCount = await agent.GetJsonAsync("/api/notifications/unread-count");
        Assert.True(unreadCount.GetProperty("count").GetInt32() >= 1);
    }

    [Fact]
    public async Task CEO_sees_agency_sales_in_list_and_can_open_detail()
    {
        // Regression: ListSales / GetSaleDetail hard-coded a privileged-role set that omitted CEO
        // (the agency owner, who holds SalesRead), so a CEO saw an EMPTY sales list and 404'd on
        // every sale detail.
        var admin = await _factory.LoginAdminAsync();
        var me = await admin.GetJsonAsync("/api/auth/me");
        var agencyId = me.GetProperty("agencyId").GetGuid();

        var ceoName = $"ceo{Guid.NewGuid():N}".Substring(0, 14);
        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{ceoName}@crm.local", userName = ceoName,
            password = "Ceo@12345!", agencyId, roles = new[] { "CEO" }
        });

        var closer = await NewCloserAsync(admin, agencyId);
        var saleId = await RecordSaleAsync(admin, "5554440001", closer);

        var ceo = await _factory.LoginAsync(ceoName, "Ceo@12345!");
        var sales = await ceo.GetJsonAsync("/api/sales?take=100");
        Assert.Contains(sales.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == saleId);

        var detail = await ceo.GetAsync($"/api/sales/{saleId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        // CEO holds PayrollProcess and is an oversight role — payroll export and the call-history
        // log must not 403 them (both previously re-gated on a role list that omitted CEO).
        Assert.Equal(HttpStatusCode.OK, (await ceo.GetAsync("/api/sales/payroll-export")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ceo.GetAsync("/api/cc/calls")).StatusCode);
    }

    [Fact]
    public async Task License_agent_commission_survives_unassign_then_reassign()
    {
        // Regression: unassigning a license agent SOFT-deletes the commission line; re-assigning found
        // that soft-deleted row (IgnoreQueryFilters) and updated it WITHOUT clearing IsDeleted, so the
        // new agent's commission stayed invisible and was never paid. The line must be revived.
        var admin = await _factory.LoginAdminAsync();
        var me = await admin.GetJsonAsync("/api/auth/me");
        var agencyId = me.GetProperty("agencyId").GetGuid();

        var sa = await SuperAdmin(_factory);
        var agentA = (await sa.PostJsonAsync($"/api/agencies/{agencyId}/license-agents",
            new { name = "Agent A", email = $"a-{Guid.NewGuid():N}@crm.local" })).GetProperty("id").GetGuid();

        var bName = $"lab{Guid.NewGuid():N}".Substring(0, 14);
        var agentB = (await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{bName}@crm.local", userName = bName,
            password = "Agent@1234!", agencyId, roles = new[] { "LicenseAgent" }
        })).GetProperty("id").GetGuid();

        var closer = await NewCloserAsync(admin, agencyId);
        var saleId = await RecordSaleAsync(admin, "5556660001", closer);
        await admin.PostJsonAsync($"/api/intake/validate/{saleId}/status", new
        {
            status = "Approved", carrierApproved = "AETNA", coverageApproved = 25000m,
            premiumApproved = 250m, planApproved = "PlanA", licenseAgentUserId = agentA
        });

        // Unassign (soft-deletes the line), then re-assign to B (must revive it).
        await admin.PutAsJsonAsync($"/api/sales/{saleId}/license-agent", new { licenseAgentUserId = (Guid?)null });
        await admin.PutAsJsonAsync($"/api/sales/{saleId}/license-agent", new { licenseAgentUserId = agentB });

        // B's commission is live: it shows on the sale (commissionEarned > 0, assigned to B) …
        var sales = await admin.GetJsonAsync("/api/sales?take=100");
        var sale = sales.GetProperty("items").EnumerateArray().First(i => i.GetProperty("id").GetGuid() == saleId);
        Assert.Equal(agentB, sale.GetProperty("licenseAgentUserId").GetGuid());
        Assert.True(sale.GetProperty("commissionEarned").GetDecimal() > 0, "revived commission must be visible & payable");

        // … and in B's own Commissions.
        var bClient = await _factory.LoginAsync(bName, "Agent@1234!");
        var commissions = await bClient.GetJsonAsync("/api/sales/commissions");
        Assert.Contains(commissions.EnumerateArray(), c => c.GetProperty("saleId").GetGuid() == saleId);
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

    [Fact]
    public async Task Assigning_a_lead_notifies_the_assignee()
    {
        // Regression/feature: the single-assign path never told the assignee. Now it fires a durable
        // in-app notification the assignee sees in their inbox.
        var admin = await _factory.LoginAdminAsync();
        var me = await admin.GetJsonAsync("/api/auth/me");
        var agencyId = me.GetProperty("agencyId").GetGuid();

        var name = $"cl{Guid.NewGuid():N}".Substring(0, 14);
        var closerId = (await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{name}@crm.local", userName = name, password = "Closer@1234!",
            agencyId, roles = new[] { "Closer" }
        })).GetProperty("id").GetGuid();

        var lead = await admin.PostJsonAsync("/api/leads", new
        {
            firstName = "Assign", lastName = "Me", phoneNumber = "5557770009", email = "assignme9@crm.local"
        });
        var leadId = lead.GetProperty("id").GetGuid();
        await admin.PostJsonAsync($"/api/leads/{leadId}/assign", new { targetRole = "Closer", userId = closerId });

        var closer = await _factory.LoginAsync(name, "Closer@1234!");
        var unread = await closer.GetJsonAsync("/api/notifications/unread-count");
        Assert.True(unread.GetProperty("count").GetInt32() >= 1, "the assignee should get a durable notification");
    }

    /// <summary>Drives a lead through New→Fronted→Verified and records a clean sale; returns the sale id.
    /// Pass <paramref name="saleRecorder"/> to record the sale as a dedicated closer so the per-closer
    /// "5 sales/hour ⇒ internal" anti-fraud heuristic doesn't trip from tests sharing this class's DB.</summary>
    private async Task<Guid> RecordSaleAsync(HttpClient admin, string phone, HttpClient? saleRecorder = null)
    {
        var lead = await admin.PostJsonAsync("/api/leads", new
        {
            firstName = "Serial", lastName = "Case", phoneNumber = phone, email = $"{phone}@crm.local"
        });
        var leadId = lead.GetProperty("id").GetGuid();
        await admin.PostJsonAsync($"/api/leads/{leadId}/transition", new { toStage = "Fronted", disposition = "Interested" });
        await admin.PostJsonAsync($"/api/leads/{leadId}/transition", new { toStage = "Verified", disposition = "Interested" });
        var sale = await (saleRecorder ?? admin).PostJsonAsync("/api/sales", new
        {
            leadId, carrier = "AETNA", policyNumber = "POL-LA", monthlyPremium = 250m,
            routingNumber = "011000015", accountNumber = "1234567800", accountType = "checking"
        });
        return sale.GetProperty("id").GetGuid();
    }

    /// <summary>Registers + logs in a fresh Closer so a test's sale doesn't add to admin's per-closer velocity.</summary>
    private async Task<HttpClient> NewCloserAsync(HttpClient admin, Guid agencyId)
    {
        var name = $"cl{Guid.NewGuid():N}".Substring(0, 14);
        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{name}@crm.local", userName = name, password = "Closer@1234!",
            agencyId, roles = new[] { "Closer" }
        });
        return await _factory.LoginAsync(name, "Closer@1234!");
    }
}
