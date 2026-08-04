using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Regression tests that lock in already-fixed bugs. Each test is written so that reverting the
/// corresponding production fix turns it red. All are black-box HTTP tests via the WebApplicationFactory.
/// </summary>
public class RegressionTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public RegressionTests(CrmWebAppFactory factory) => _factory = factory;

    // ── 1. Register rejects an unknown role ──────────────────────────────────────
    // Previously a typo'd role auto-created a junk role and returned 200; now it must 409.
    [Fact]
    public async Task Register_with_unknown_role_returns_409()
    {
        var admin = await _factory.LoginAdminAsync();
        var me = await admin.GetJsonAsync("/api/auth/me");
        var agencyId = me.GetProperty("agencyId").GetGuid();

        var resp = await admin.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"junk-{Guid.NewGuid():N}@crm.local",
            userName = $"junk-{Guid.NewGuid():N}".Substring(0, 16),
            password = "Junk@1234!",
            agencyId,
            roles = new[] { "NotARealRole" }
        });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    // ── 2. QA review rejects a duplicate rubric item ─────────────────────────────
    // Listing the same rubric item id twice previously let TotalScore exceed MaxScore; now it must 409.
    [Fact]
    public async Task Qa_review_with_duplicate_rubric_item_returns_409()
    {
        var admin = await _factory.LoginAdminAsync();
        var me = await admin.GetJsonAsync("/api/auth/me");
        var agentUserId = me.GetProperty("id").GetGuid();

        // Build a rubric with a single scored item.
        var rubric = await admin.PostJsonAsync("/api/qa/rubrics", new
        {
            name = $"Rubric {Guid.NewGuid():N}",
            description = "regression",
            items = new[] { new { label = "Greeting", maxScore = 10, order = 1 } }
        });
        var rubricId = rubric.GetProperty("id").GetGuid();
        var itemId = rubric.GetProperty("items")[0].GetProperty("id").GetGuid();

        // A lead to hang the review off of.
        var lead = await admin.PostJsonAsync("/api/leads", new
        {
            firstName = "Qa", lastName = "Dupe", phoneNumber = "5550004444"
        });
        var leadId = lead.GetProperty("id").GetGuid();

        // Submit a review scoring the SAME rubric item twice — must be rejected as a conflict.
        var resp = await admin.PostAsJsonAsync("/api/qa/reviews", new
        {
            leadId,
            saleId = (Guid?)null,
            agentUserId,
            rubricId,
            items = new[]
            {
                new { rubricItemId = itemId, score = 10, comment = (string?)null },
                new { rubricItemId = itemId, score = 10, comment = (string?)null }
            },
            notes = "dupe"
        });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    // Sanity: a well-formed single-item review still succeeds (proves the 409 above is the duplicate,
    // not a broken request).
    [Fact]
    public async Task Qa_review_without_duplicates_succeeds()
    {
        var admin = await _factory.LoginAdminAsync();
        var me = await admin.GetJsonAsync("/api/auth/me");
        var agentUserId = me.GetProperty("id").GetGuid();

        var rubric = await admin.PostJsonAsync("/api/qa/rubrics", new
        {
            name = $"Rubric {Guid.NewGuid():N}",
            items = new[] { new { label = "Greeting", maxScore = 10, order = 1 } }
        });
        var rubricId = rubric.GetProperty("id").GetGuid();
        var itemId = rubric.GetProperty("items")[0].GetProperty("id").GetGuid();

        var lead = await admin.PostJsonAsync("/api/leads", new
        {
            firstName = "Qa", lastName = "Ok", phoneNumber = "5550005555"
        });
        var leadId = lead.GetProperty("id").GetGuid();

        var review = await admin.PostJsonAsync("/api/qa/reviews", new
        {
            leadId,
            saleId = (Guid?)null,
            agentUserId,
            rubricId,
            items = new[] { new { rubricItemId = itemId, score = 7, comment = "ok" } },
            notes = "ok"
        });

        // TotalScore must never exceed MaxScore.
        var total = review.GetProperty("totalScore").GetDecimal();
        var max = review.GetProperty("maxScore").GetDecimal();
        Assert.Equal(7m, total);
        Assert.True(total <= max);
    }

    // ── 3. Meeting attendee cap holds when one list is null ──────────────────────
    // A single over-cap email list with attendeeUserIds=null previously bypassed the limit; now it must 400.
    [Fact]
    public async Task Meeting_over_attendee_cap_with_null_user_ids_returns_400()
    {
        var admin = await _factory.LoginAdminAsync();

        // 101 emails > SchedulingDefaults.MaxAttendees (100). Distinct so de-dup can't shrink it.
        var emails = Enumerable.Range(0, 101).Select(i => $"invitee{i}-{Guid.NewGuid():N}@crm.local").ToArray();

        // Valid times (EndsAt > StartsAt) + non-empty title so the ONLY failing rule is the attendee cap —
        // otherwise the test could pass on an unrelated validation error even with the cap removed.
        var resp = await admin.PostAsJsonAsync("/api/meetings", new
        {
            title = "Big meeting",
            description = (string?)null,
            startsAt = "2026-09-01T15:00:00Z",
            endsAt = "2026-09-01T16:00:00Z",
            location = (string?)null,
            onlineUrl = (string?)null,
            attendeeUserIds = (Guid[]?)null,
            attendeeEmails = emails
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // Sanity: the same request under the cap (with attendeeUserIds still null) is accepted — proves the
    // 400 above is the cap, not the null user-id list.
    [Fact]
    public async Task Meeting_under_attendee_cap_with_null_user_ids_succeeds()
    {
        var admin = await _factory.LoginAdminAsync();
        var emails = Enumerable.Range(0, 3).Select(i => $"invitee{i}-{Guid.NewGuid():N}@crm.local").ToArray();

        var resp = await admin.PostAsJsonAsync("/api/meetings", new
        {
            title = "Small meeting",
            description = (string?)null,
            startsAt = "2026-09-02T15:00:00Z",
            endsAt = "2026-09-02T16:00:00Z",
            location = (string?)null,
            onlineUrl = (string?)null,
            attendeeUserIds = (Guid[]?)null,
            attendeeEmails = emails
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── 4. Outbound dial is blocked for a DNC number ─────────────────────────────
    // The /api/integrations/dialer/dial path previously bypassed the compliance guard; now a DNC
    // number must 409.
    [Fact]
    public async Task Dialer_dial_to_dnc_number_returns_409()
    {
        var admin = await _factory.LoginAdminAsync();

        const string phone = "5557778899";
        var lead = await admin.PostJsonAsync("/api/leads", new
        {
            firstName = "Dnc", lastName = "Blocked", phoneNumber = phone
        });
        var leadId = lead.GetProperty("id").GetGuid();

        // Put the lead's number on the agency DNC list (normalised the same way the checker compares).
        await admin.PostJsonAsync("/api/cc/dnc", new { phone, reason = "regression", source = "Internal" });

        // Dialing a DNC number must be blocked by the compliance guard (DNC is checked before the
        // TCPA window, so this 409 is deterministic regardless of the wall-clock hour).
        var resp = await admin.PostAsJsonAsync("/api/integrations/dialer/dial", new { leadId });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }
}
