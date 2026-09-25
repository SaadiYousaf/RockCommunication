using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// The Learning Academy: a seeded curriculum, per-learner progress, server-side grading, and a
/// certificate at the end.
///
/// The property that matters most is that the ANSWER KEY never leaves the server. If it did, the
/// quiz would be decoration — anyone could read the right answers out of the network tab.
/// </summary>
public class AcademyTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public AcademyTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task The_curriculum_is_seeded_and_scoped_to_my_roles()
    {
        var admin = await _factory.LoginAdminAsync();

        var courses = await admin.GetJsonAsync("/api/academy/courses");
        var list = courses.EnumerateArray().ToList();

        Assert.NotEmpty(list);
        // "Getting started" has an empty audience, so it is in everyone's path.
        Assert.Contains(list, c => c.GetProperty("key").GetString() == "getting-started");

        // Every course reports its size and the learner's progress through it.
        foreach (var c in list)
        {
            Assert.True(c.GetProperty("lessonCount").GetInt32() > 0, "a published course should have lessons");
            Assert.True(c.GetProperty("completedLessons").GetInt32() >= 0);
        }
    }

    /// <summary>Re-running the seeder must refresh content in place, never duplicate the curriculum.</summary>
    [Fact]
    public async Task Course_keys_are_unique()
    {
        var admin = await _factory.LoginAdminAsync();
        var courses = await admin.GetJsonAsync("/api/academy/courses");

        var keys = courses.EnumerateArray().Select(c => c.GetProperty("key").GetString()!).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    /// <summary>THE security property: a lesson's quiz must not carry the answers.</summary>
    [Fact]
    public async Task A_lesson_never_exposes_the_answer_key()
    {
        var admin = await _factory.LoginAdminAsync();

        var res = await admin.GetAsync("/api/academy/courses/getting-started/lessons/the-shape-of-the-app");
        res.EnsureSuccessStatusCode();
        var raw = await res.Content.ReadAsStringAsync();

        // Neither the field name nor a grading hint may appear anywhere in the payload.
        Assert.DoesNotContain("correctIndex", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explanation", raw, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(raw);
        var questions = doc.RootElement.GetProperty("questions");
        Assert.True(questions.GetArrayLength() > 0, "this lesson should have a quiz");
        foreach (var q in questions.EnumerateArray())
            Assert.True(q.GetProperty("options").GetArrayLength() >= 2);
    }

    [Fact]
    public async Task Completing_a_lesson_is_recorded_and_is_idempotent()
    {
        var admin = await _factory.LoginAdminAsync();
        var lesson = await admin.GetJsonAsync("/api/academy/courses/getting-started/lessons/your-dashboard");
        var id = lesson.GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync($"/api/academy/lessons/{id}/complete", new { secondsSpent = 120 }))
            .EnsureSuccessStatusCode();
        // Twice: re-reading a lesson to look something up must never cost the learner their progress.
        (await admin.PostAsJsonAsync($"/api/academy/lessons/{id}/complete", new { secondsSpent = 30 }))
            .EnsureSuccessStatusCode();

        var again = await admin.GetJsonAsync("/api/academy/courses/getting-started/lessons/your-dashboard");
        Assert.Equal("Completed", again.GetProperty("status").GetString());

        var progress = await admin.GetJsonAsync("/api/academy/progress");
        Assert.True(progress.GetProperty("lessonsCompleted").GetInt32() >= 1);
    }

    /// <summary>Grading happens on the server, against the stored key.</summary>
    [Fact]
    public async Task A_quiz_is_graded_server_side_and_explains_each_answer()
    {
        var admin = await _factory.LoginAdminAsync();
        var lesson = await admin.GetJsonAsync("/api/academy/courses/getting-started/lessons/the-shape-of-the-app");
        var id = lesson.GetProperty("id").GetGuid();
        var count = lesson.GetProperty("questions").GetArrayLength();

        // Deliberately wrong: an index no question uses.
        var wrong = await admin.PostJsonAsync($"/api/academy/lessons/{id}/quiz",
            new { answers = Enumerable.Repeat(99, count).ToArray() });

        Assert.Equal(0, wrong.GetProperty("score").GetInt32());
        Assert.False(wrong.GetProperty("passed").GetBoolean());

        // The result is where the teaching happens — every item comes back with the why.
        foreach (var item in wrong.GetProperty("items").EnumerateArray())
        {
            Assert.False(item.GetProperty("correct").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("explanation").GetString()));
        }
    }

    /// <summary>Finishing every lesson in a course issues exactly one certificate.</summary>
    [Fact]
    public async Task Finishing_a_course_issues_one_verifiable_certificate()
    {
        var admin = await _factory.LoginAdminAsync();

        var lessons = (await admin.GetJsonAsync("/api/academy/courses/getting-started")).EnumerateArray().ToList();

        // Reading every lesson is enough — a learner who worked through a whole course earns the
        // certificate whether or not the last lesson happened to carry a quiz.
        System.Text.Json.JsonElement last = default;
        foreach (var l in lessons)
        {
            var id = l.GetProperty("id").GetGuid();
            last = await admin.PostJsonAsync($"/api/academy/lessons/{id}/complete", new { secondsSpent = 60 });
        }
        Assert.True(last.GetProperty("courseCompleted").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(last.GetProperty("certificateSerial").GetString()));

        var certs = await admin.GetJsonAsync("/api/academy/certificates");
        var mine = certs.EnumerateArray()
            .Where(c => c.GetProperty("courseTitle").GetString() == "Getting started").ToList();

        Assert.Single(mine);
        var serial = mine[0].GetProperty("serialNumber").GetString()!;
        Assert.StartsWith("SMH-", serial);

        // …and the printed serial verifies.
        var verified = await admin.GetJsonAsync($"/api/academy/certificates/{serial}");
        Assert.Equal(serial, verified.GetProperty("serialNumber").GetString());
    }

    [Fact]
    public async Task An_unknown_certificate_serial_is_not_found()
    {
        var admin = await _factory.LoginAdminAsync();
        var res = await admin.GetAsync("/api/academy/certificates/RC-0000-0000");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task The_academy_requires_a_signed_in_user()
    {
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/academy/courses")).StatusCode);
    }
}
