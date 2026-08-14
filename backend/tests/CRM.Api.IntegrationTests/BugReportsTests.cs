using System.Linq;
using System.Net.Http.Json;

namespace CRM.Api.IntegrationTests;

/// <summary>In-app bug reporter: filing, the status workflow, and the activity trail.</summary>
public class BugReportsTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public BugReportsTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Report_transition_and_activity_round_trip()
    {
        var admin = await _factory.LoginAdminAsync();

        var created = await admin.PostJsonAsync("/api/bugs", new
        {
            title = "Payroll slip shows wrong net",
            description = "Net pay on the slip doesn't match the row.",
            severity = "High",
            pageUrl = "/hr/payroll",
        });
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("New", created.GetProperty("status").GetString());
        Assert.Equal("High", created.GetProperty("severity").GetString());
        Assert.True(created.GetProperty("canManage").GetBoolean());   // admin can triage

        // It shows up in the list.
        var list = await admin.GetJsonAsync("/api/bugs");
        Assert.Contains(list.EnumerateArray(), b => b.GetProperty("id").GetGuid() == id);

        // Move it through the workflow with a resolution note.
        (await admin.PatchAsJsonAsync($"/api/bugs/{id}/status", new { status = "InProgress" })).EnsureSuccessStatusCode();
        (await admin.PatchAsJsonAsync($"/api/bugs/{id}/status", new { status = "Resolved", resolution = "Fixed the rounding." }))
            .EnsureSuccessStatusCode();

        var detail = await admin.GetJsonAsync($"/api/bugs/{id}");
        Assert.Equal("Resolved", detail.GetProperty("bug").GetProperty("status").GetString());
        Assert.Equal("Fixed the rounding.", detail.GetProperty("bug").GetProperty("resolution").GetString());

        // The activity trail recorded both transitions, most-recent last.
        var activity = detail.GetProperty("activity");
        Assert.Equal(2, activity.GetArrayLength());
        Assert.Equal("New", activity[0].GetProperty("fromStatus").GetString());
        Assert.Equal("InProgress", activity[0].GetProperty("toStatus").GetString());
        Assert.Equal("Resolved", activity[1].GetProperty("toStatus").GetString());
    }

    [Fact]
    public async Task Filter_by_status_only_returns_matching_bugs()
    {
        var admin = await _factory.LoginAdminAsync();
        var b = await admin.PostJsonAsync("/api/bugs", new { title = "Filter probe", description = "x", severity = "Low" });
        var id = b.GetProperty("id").GetGuid();

        var news = await admin.GetJsonAsync("/api/bugs?status=New");
        Assert.Contains(news.EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);

        var closed = await admin.GetJsonAsync("/api/bugs?status=Closed");
        Assert.DoesNotContain(closed.EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
    }
}
