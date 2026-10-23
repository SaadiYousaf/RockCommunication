using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// A closer's own queue — how much of it they can actually see.
///
/// It returned fifty leads against a ceiling of two hundred, ordered OLDEST FIRST. A closer
/// holding more than fifty therefore never saw the ones most recently assigned to them: a lead
/// that arrived this morning sat invisible behind a backlog, with nothing on screen to say the
/// list had been cut. The same shape of bug as the submission queue's, on the screen closers live
/// in all day.
/// </summary>
public class MyQueueCapacityTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public MyQueueCapacityTests(CrmWebAppFactory factory) => _factory = factory;

    private const string Password = "Queue!12345";

    /// <summary>
    /// The limit must be far above a real closer's workload.
    ///
    /// Asserted on the limit itself rather than by seeding hundreds of leads: the bug WAS the
    /// number, and a test that creates fifty-one leads to prove it takes minutes and still only
    /// pins today's volume. Anyone lowering this has to read the sentence above first.
    /// </summary>
    [Fact]
    public void The_queue_limit_is_not_a_closers_whole_workload()
    {
        var limit = new CRM.Application.Leads.Queries.MyQueueQuery().Take;
        Assert.True(limit >= 1000,
            $"a closer's queue returns at most {limit} leads, oldest first. Hold more than that and " +
            "the most recently assigned ones are never shown.");
    }

    /// <summary>
    /// Asking for no period returns the whole workload.
    ///
    /// This is the deliberate difference from the submission queue, which opens on the current
    /// month. A submitted sale belongs to the month it was made in; this is a list of work still
    /// owed, and defaulting it to one month would hide a lead from three weeks ago that somebody
    /// is still waiting on a call about.
    /// </summary>
    [Fact]
    public async Task Without_a_period_the_whole_queue_comes_back()
    {
        var admin = await _factory.LoginAdminAsync();
        var userName = $"cq{Guid.NewGuid():N}"[..16];
        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password = Password,
            roles = new[] { "Closer" },
        });
        var closer = await _factory.LoginAsync(userName, Password);

        // A lead of their own, captured through the closer's own capture path.
        var created = await closer.PostJsonAsync("/api/intake/close/leads", new
        {
            firstName = "Wholequeue", lastName = "Testcase", maritalStatus = "Single",
            createdDate = DateTime.UtcNow,
            streetAddress = "1 Main St", city = "Phoenix", state = "AZ", zipcode = "85004",
            phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
            birthDate = new DateTime(1960, 1, 1), ageYears = 66,
            email = $"wq-{Guid.NewGuid():N}@example.com",
            jornayaLeadId = Guid.NewGuid().ToString("N"),
        });
        var leadId = created.GetProperty("leadId").GetGuid();

        var mine = await closer.GetJsonAsync("/api/leads/mine");
        Assert.Contains(mine.EnumerateArray(), l => l.GetProperty("id").GetGuid() == leadId);
    }

    /// <summary>…and a period, when asked for, actually narrows it.</summary>
    [Fact]
    public async Task A_period_narrows_the_queue()
    {
        var admin = await _factory.LoginAdminAsync();
        var userName = $"cq{Guid.NewGuid():N}"[..16];
        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password = Password,
            roles = new[] { "Closer" },
        });
        var closer = await _factory.LoginAsync(userName, Password);

        var created = await closer.PostJsonAsync("/api/intake/close/leads", new
        {
            firstName = "Periodcase", lastName = "Testcase", maritalStatus = "Single",
            createdDate = DateTime.UtcNow,
            streetAddress = "1 Main St", city = "Phoenix", state = "AZ", zipcode = "85004",
            phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
            birthDate = new DateTime(1960, 1, 1), ageYears = 66,
            email = $"pc-{Guid.NewGuid():N}@example.com",
            jornayaLeadId = Guid.NewGuid().ToString("N"),
        });
        var leadId = created.GetProperty("leadId").GetGuid();

        var now = DateTime.UtcNow;
        var thisMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        static string Iso(DateTime d) => Uri.EscapeDataString(d.ToString("o"));

        var inPeriod = await closer.GetJsonAsync(
            $"/api/leads/mine?from={Iso(thisMonth)}&to={Iso(thisMonth.AddMonths(1))}");
        Assert.Contains(inPeriod.EnumerateArray(), l => l.GetProperty("id").GetGuid() == leadId);

        // Last month cannot contain a lead that arrived this month — the end is exclusive.
        var previous = await closer.GetJsonAsync(
            $"/api/leads/mine?from={Iso(thisMonth.AddMonths(-1))}&to={Iso(thisMonth)}");
        Assert.DoesNotContain(previous.EnumerateArray(), l => l.GetProperty("id").GetGuid() == leadId);
    }
}
