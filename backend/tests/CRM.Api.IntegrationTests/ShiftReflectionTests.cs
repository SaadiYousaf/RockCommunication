using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// The two end-of-day questions, asked when somebody signs out.
///
/// Answering is optional, which is what most of these tests are really about: the dialog carries a
/// Skip button, so the server has to cope with one question answered and not the other, with a
/// second sign-out later the same day, and with nothing at all — without ever failing a sign-out.
/// </summary>
public class ShiftReflectionTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public ShiftReflectionTests(CrmWebAppFactory factory) => _factory = factory;

    private const string Password = "Reflect!123";

    private static async Task<(Guid Id, string UserName)> CreateAsync(HttpClient admin, string role)
    {
        var userName = $"rf{Guid.NewGuid():N}"[..16];
        var created = await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com",
            userName,
            password = Password,
            roles = new[] { role },
        });
        return (created.GetProperty("id").GetGuid(), userName);
    }

    [Fact]
    public async Task An_answer_is_saved_and_read_back()
    {
        var admin = await _factory.LoginAdminAsync();
        var (_, userName) = await CreateAsync(admin, "Closer");
        var closer = await _factory.LoginAsync(userName, Password);

        await closer.PostJsonAsync("/api/hr/reflections", new
        {
            learned = "Asking for the beneficiary before the bank details keeps people talking.",
            didJustice = true,
        });

        var mine = await closer.GetJsonAsync("/api/hr/reflections?scope=mine");
        var row = mine.EnumerateArray().Single();
        Assert.Contains("beneficiary", row.GetProperty("learned").GetString() ?? "");
        Assert.True(row.GetProperty("didJustice").GetBoolean());
    }

    /// <summary>
    /// Only one of the two questions answered. The dialog allows it, so this must not be a 400 —
    /// and the unanswered one must come back as null rather than as a guess.
    /// </summary>
    [Fact]
    public async Task One_question_may_be_answered_without_the_other()
    {
        var admin = await _factory.LoginAdminAsync();
        var (_, userName) = await CreateAsync(admin, "Closer");
        var closer = await _factory.LoginAsync(userName, Password);

        var res = await closer.PostAsJsonAsync("/api/hr/reflections", new { learned = "Only this one." });
        Assert.True(res.IsSuccessStatusCode, $"a single answer should save; got {res.StatusCode}");

        var row = (await closer.GetJsonAsync("/api/hr/reflections?scope=mine")).EnumerateArray().Single();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, row.GetProperty("didJustice").ValueKind);
    }

    /// <summary>
    /// Signing out twice in one day — lunch and then home.
    ///
    /// The second pass must edit the day's answer rather than file a second one, and a box left
    /// empty the second time must leave the morning's answer standing. Overwriting it with null
    /// would quietly delete the only thing the person actually wrote.
    /// </summary>
    [Fact]
    public async Task A_second_sign_out_the_same_day_updates_without_erasing()
    {
        var admin = await _factory.LoginAdminAsync();
        var (_, userName) = await CreateAsync(admin, "Closer");
        var closer = await _factory.LoginAsync(userName, Password);

        await closer.PostJsonAsync("/api/hr/reflections", new { learned = "Morning note." });
        // Second sign-out: answers the other question, leaves the first box empty.
        await closer.PostJsonAsync("/api/hr/reflections", new { didJustice = false });

        var rows = (await closer.GetJsonAsync("/api/hr/reflections?scope=mine")).EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("Morning note.", rows[0].GetProperty("learned").GetString());
        Assert.False(rows[0].GetProperty("didJustice").GetBoolean());
    }

    /// <summary>
    /// Pressing Skip sends nothing at all, so an empty body is a client fault rather than a user
    /// action. It is refused to keep blank rows out of a manager's list — and the browser signs
    /// the person out regardless, so refusing it strands nobody.
    /// </summary>
    [Fact]
    public async Task An_empty_submission_is_refused()
    {
        var admin = await _factory.LoginAdminAsync();
        var (_, userName) = await CreateAsync(admin, "Closer");
        var closer = await _factory.LoginAsync(userName, Password);

        var res = await closer.PostAsJsonAsync("/api/hr/reflections", new { });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// An agent reads their own answers and nobody else's. Not a 403 — they are narrowed to
    /// themselves, because the page is useful to them and refusing it would teach them that their
    /// own words are none of their business.
    /// </summary>
    [Fact]
    public async Task An_agent_sees_only_their_own_check_ins()
    {
        var admin = await _factory.LoginAdminAsync();
        var (_, oneName) = await CreateAsync(admin, "Closer");
        var (_, twoName) = await CreateAsync(admin, "Closer");

        var one = await _factory.LoginAsync(oneName, Password);
        var two = await _factory.LoginAsync(twoName, Password);
        await one.PostJsonAsync("/api/hr/reflections", new { learned = "Written by the first closer." });
        await two.PostJsonAsync("/api/hr/reflections", new { learned = "Written by the second closer." });

        // Asks for everything, with no scope: still gets only their own.
        var seen = await two.GetJsonAsync("/api/hr/reflections");
        var rows = seen.EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("Written by the second closer.", rows[0].GetProperty("learned").GetString());
    }

    /// <summary>Management reads the floor — that is the entire reason the questions are asked.</summary>
    [Fact]
    public async Task An_admin_reads_everybody()
    {
        var admin = await _factory.LoginAdminAsync();
        var (_, userName) = await CreateAsync(admin, "Closer");
        var closer = await _factory.LoginAsync(userName, Password);

        await closer.PostJsonAsync("/api/hr/reflections",
            new { learned = "A sentence only an administrator should be able to find.", didJustice = false });

        var all = await admin.GetJsonAsync("/api/hr/reflections");
        Assert.Contains(all.EnumerateArray(),
            r => (r.GetProperty("learned").GetString() ?? "").Contains("only an administrator"));
    }

    [Fact]
    public async Task Signed_out_callers_cannot_file_one()
    {
        var anon = _factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/hr/reflections", new { learned = "Nobody sent this." });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
