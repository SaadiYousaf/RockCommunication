using System.Net.Http.Json;

namespace CRM.Api.IntegrationTests;

public class WorkflowAndAiTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public WorkflowAndAiTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Workflow_rule_crud_round_trip()
    {
        var admin = await _factory.LoginAdminAsync();

        // Create a rule that fires on lead.created with a high score
        var put = await admin.PutAsJsonAsync("/api/workflows/rules", new
        {
            id = (Guid?)null,
            name = "Auto-assign hot leads",
            eventType = "lead.created",
            conditionJson = "{\"all\":[{\"fact\":\"score\",\"op\":\"gte\",\"value\":40}]}",
            priority = 100,
            isActive = true,
            continueOnError = true,
            description = "Round-robin assign hot leads to fronters",
            actions = new[]
            {
                new { actionType = "assign-agent", parametersJson = "{\"role\":\"Fronter\",\"strategy\":\"round-robin\"}", order = 1 }
            }
        });
        put.EnsureSuccessStatusCode();

        var list = await admin.GetJsonAsync("/api/workflows/rules");
        Assert.True(list.GetArrayLength() >= 1);

        var events = await admin.GetJsonAsync("/api/workflows/event-types");
        Assert.Contains(events.EnumerateArray(), e => e.GetString() == "lead.created");

        var actions = await admin.GetJsonAsync("/api/workflows/action-types");
        Assert.Contains(actions.EnumerateArray(), a => a.GetString() == "assign-agent");
    }

    [Fact]
    public async Task Lead_rescore_returns_breakdown()
    {
        var admin = await _factory.LoginAdminAsync();
        var lead = await admin.PostJsonAsync("/api/leads", new
        {
            firstName = "Score", lastName = "Me", phoneNumber = "5550009990", email = "score@crm.local"
        });
        var leadId = lead.GetProperty("id").GetGuid();

        var rescore = await admin.PostJsonAsync($"/api/leads/{leadId}/rescore", new { });
        Assert.True(rescore.GetProperty("score").GetInt32() > 0);
        Assert.True(rescore.GetProperty("breakdown").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Ai_recommendations_for_unverified_lead_includes_jornaya_action()
    {
        var admin = await _factory.LoginAdminAsync();
        var lead = await admin.PostJsonAsync("/api/leads", new
        {
            firstName = "AI", lastName = "Lead", phoneNumber = "5550001011",
            email = "ai@crm.local", jornayaLeadId = "fake-token"
        });
        var leadId = lead.GetProperty("id").GetGuid();

        var recs = await admin.GetJsonAsync($"/api/ai/leads/{leadId}/recommendations");
        var items = recs.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        Assert.Contains(items.EnumerateArray(),
            e => (e.GetProperty("action").GetString() ?? "").Contains("Jornaya", System.StringComparison.OrdinalIgnoreCase));
    }
}
