using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Scheduling a meeting must actually reach the people invited to it.
///
/// It did not. Attendees were persisted correctly and Upcoming Events queried them correctly, but
/// the create handler only ever sent an EMAIL — no in-app notification row was written for anyone.
/// So someone already signed in saw a bell reading "You're all caught up" while a meeting sat on
/// their calendar. Production had one meeting, one linked attendee, and zero meeting notifications.
/// </summary>
public class MeetingNotificationTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public MeetingNotificationTests(CrmWebAppFactory factory) => _factory = factory;

    private static object MeetingBody(string title, DateTime startsAtUtc, params Guid[] attendees) => new
    {
        title,
        description = (string?)null,
        startsAt = startsAtUtc,
        endsAt = startsAtUtc.AddMinutes(30),
        location = "Room 1",
        onlineUrl = (string?)null,
        attendeeUserIds = attendees,
        attendeeEmails = Array.Empty<string>(),
    };

    /// <summary>An agency user who is NOT the caller — the person we invite.</summary>
    private static async Task<(Guid Id, string UserName)> AnotherUserAsync(HttpClient admin)
    {
        var users = await admin.GetJsonAsync("/api/users");
        var me = await admin.GetJsonAsync("/api/profile/me");
        var myId = me.GetProperty("id").GetString();

        var other = users.EnumerateArray().First(u =>
            u.GetProperty("id").GetString() != myId &&
            (u.GetProperty("isActive").ValueKind != JsonValueKind.False));
        return (Guid.Parse(other.GetProperty("id").GetString()!), other.GetProperty("userName").GetString()!);
    }

    /// <summary>THE bug: an invited attendee must get an in-app notification, not just an email.</summary>
    [Fact]
    public async Task Inviting_a_user_to_a_meeting_notifies_them_in_the_app()
    {
        var admin = await _factory.LoginAdminAsync();
        var (inviteeId, _) = await AnotherUserAsync(admin);

        var before = await CountForUserAsync(inviteeId);

        var created = await admin.PostJsonAsync("/api/meetings",
            MeetingBody($"Client follow-up {Guid.NewGuid():N}".Substring(0, 24),
                DateTime.UtcNow.AddDays(1), inviteeId));
        Assert.NotEqual(Guid.Empty, created.GetProperty("id").GetGuid());

        var after = await CountForUserAsync(inviteeId);
        Assert.True(after > before,
            "the invited attendee should have a new in-app notification, not only an email");
    }

    /// <summary>Moving a meeting changes where someone has to be — they must be told.</summary>
    [Fact]
    public async Task Rescheduling_notifies_the_attendees()
    {
        var admin = await _factory.LoginAdminAsync();
        var (inviteeId, _) = await AnotherUserAsync(admin);

        var created = await admin.PostJsonAsync("/api/meetings",
            MeetingBody("Reschedule me", DateTime.UtcNow.AddDays(2), inviteeId));
        var id = created.GetProperty("id").GetGuid();

        var before = await CountForUserAsync(inviteeId);

        var moved = await admin.PutAsJsonAsync($"/api/meetings/{id}",
            MeetingBody("Reschedule me", DateTime.UtcNow.AddDays(3), inviteeId));
        // This returned a 500 before the fix — editing any meeting was broken outright.
        moved.EnsureSuccessStatusCode();

        Assert.True(await CountForUserAsync(inviteeId) > before,
            "moving a meeting should notify the people who have to attend it");
    }

    /// <summary>Someone who cleared their diary for a meeting must hear that it is off.</summary>
    [Fact]
    public async Task Cancelling_notifies_the_attendees()
    {
        var admin = await _factory.LoginAdminAsync();
        var (inviteeId, _) = await AnotherUserAsync(admin);

        var created = await admin.PostJsonAsync("/api/meetings",
            MeetingBody("Cancel me", DateTime.UtcNow.AddDays(2), inviteeId));
        var id = created.GetProperty("id").GetGuid();

        var before = await CountForUserAsync(inviteeId);
        (await admin.PostAsync($"/api/meetings/{id}/cancel", null)).EnsureSuccessStatusCode();

        Assert.True(await CountForUserAsync(inviteeId) > before,
            "cancelling should notify the people who were going to attend");
    }

    /// <summary>
    /// A meeting booked in the past is invisible the moment it is saved — Upcoming Events and the
    /// calendar only show future meetings. That is exactly how a real meeting went missing, so the
    /// form now refuses it instead of accepting it silently.
    /// </summary>
    [Fact]
    public async Task A_meeting_cannot_be_scheduled_in_the_past()
    {
        var admin = await _factory.LoginAdminAsync();

        var res = await admin.PostAsJsonAsync("/api/meetings",
            MeetingBody("Yesterday's meeting", DateTime.UtcNow.AddDays(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("future", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An attendee sees the meeting in Upcoming Events, not just the organiser.</summary>
    [Fact]
    public async Task An_attendee_sees_the_meeting_in_upcoming_events()
    {
        var admin = await _factory.LoginAdminAsync();
        var title = $"Standup {Guid.NewGuid():N}".Substring(0, 16);

        await admin.PostJsonAsync("/api/meetings", MeetingBody(title, DateTime.UtcNow.AddDays(1)));

        var events = await admin.GetJsonAsync("/api/dashboard/upcoming-events");
        var titles = events.EnumerateArray().Select(e => e.GetProperty("title").GetString()).ToList();
        Assert.Contains(title, titles);
    }

    /// <summary>Counts a user's notifications directly, so the assertion does not depend on login.</summary>
    private async Task<int> CountForUserAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<CRM.Infrastructure.Persistence.AppDbContext>();
        return await db.Notifications.IgnoreQueryFilters().CountAsync(n => n.UserId == userId);
    }
}
