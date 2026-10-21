using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Chat that spans agencies — and, just as importantly, chat that does not.
///
/// A central Submission Agent works every company's sales, so "message the closer about their
/// sale" and "make a group with the agents on each floor" both mean reaching people outside any
/// single agency. Chat was scoped by agency, so a cross-agency room was invisible to its own
/// members: they were in a group they could not see, read or post to.
///
/// The gate is now MEMBERSHIP rather than the tenant filter. Every test here that proves the
/// feature works is paired with one proving the boundary still holds, because the failure mode of
/// getting this wrong is one agency reading another's private conversations.
/// </summary>
public class CrossAgencyChatTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public CrossAgencyChatTests(CrmWebAppFactory factory) => _factory = factory;

    private const string Password = "Chat!12345";

    private static async Task<Guid> CreateAgencyAsync(HttpClient superAdmin, string name)
    {
        var res = await superAdmin.PostAsJsonAsync("/api/agencies", new
        {
            name,
            code = $"C{Random.Shared.Next(100000, 999999)}",
            ceoName = $"{name} Chief",
            ceoEmail = $"ceo-{Guid.NewGuid():N}@example.com",
        });
        res.EnsureSuccessStatusCode();
        var dto = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return dto.GetProperty("id").GetGuid();
    }

    private static async Task<(Guid Id, string UserName)> CreateUserAsync(
        HttpClient superAdmin, Guid agencyId, string role)
    {
        var userName = $"ch{Guid.NewGuid():N}"[..16];
        var created = await superAdmin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password = Password,
            agencyId, roles = new[] { role },
        });
        return (created.GetProperty("id").GetGuid(), userName);
    }

    /// <summary>
    /// THE feature Akash asked for: one group holding agents from different call centres.
    ///
    /// Every member must be able to see it and read what is said in it — a room its own members
    /// cannot open is worse than no room at all, because it looks like it worked.
    /// </summary>
    [Fact]
    public async Task A_cross_agency_group_is_visible_to_all_of_its_members()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agencyA = await CreateAgencyAsync(sa, $"ChatA {Guid.NewGuid():N}"[..18]);
        var agencyB = await CreateAgencyAsync(sa, $"ChatB {Guid.NewGuid():N}"[..18]);
        var (closerA, nameA) = await CreateUserAsync(sa, agencyA, "Closer");
        var (closerB, nameB) = await CreateUserAsync(sa, agencyB, "Closer");

        // The central submission agent: a Validator belonging to no agency.
        var (_, centralName) = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);

        var room = await central.PostJsonAsync("/api/chat/rooms", new
        {
            name = "Floor leads",
            isDirect = false,
            memberUserIds = new[] { closerA, closerB },
        });
        var roomId = room.GetProperty("id").GetGuid();

        // The creator posts, and each member — in a different agency — can read it.
        await central.PostJsonAsync($"/api/chat/rooms/{roomId}/messages", new { body = "Morning both." });

        foreach (var userName in new[] { nameA, nameB })
        {
            var member = await _factory.LoginAsync(userName, Password);

            var rooms = await member.GetJsonAsync("/api/chat/rooms");
            Assert.Contains(rooms.EnumerateArray(), r => r.GetProperty("id").GetGuid() == roomId);

            var msgs = await member.GetJsonAsync($"/api/chat/rooms/{roomId}/messages");
            Assert.Contains(msgs.EnumerateArray(),
                m => (m.GetProperty("body").GetString() ?? "").Contains("Morning both"));
        }
    }

    /// <summary>And a member in another agency can reply, not just read.</summary>
    [Fact]
    public async Task A_member_in_another_agency_can_post()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agency = await CreateAgencyAsync(sa, $"ChatPost {Guid.NewGuid():N}"[..18]);
        var (closer, closerName) = await CreateUserAsync(sa, agency, "Closer");
        var (_, centralName) = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);

        var room = await central.PostJsonAsync("/api/chat/rooms", new
        {
            name = "Submissions", isDirect = false, memberUserIds = new[] { closer },
        });
        var roomId = room.GetProperty("id").GetGuid();

        var member = await _factory.LoginAsync(closerName, Password);
        var res = await member.PostAsJsonAsync($"/api/chat/rooms/{roomId}/messages", new { body = "Got it." });
        Assert.True(res.IsSuccessStatusCode, $"a member should be able to post; got {res.StatusCode}");

        var msgs = await central.GetJsonAsync($"/api/chat/rooms/{roomId}/messages");
        Assert.Contains(msgs.EnumerateArray(), m => (m.GetProperty("body").GetString() ?? "").Contains("Got it"));
    }

    /// <summary>
    /// THE boundary. Somebody who is not in the room cannot read it — whatever agency they are in,
    /// and even though the tenant filter is no longer the thing standing in their way.
    ///
    /// This is the test that matters. If membership were ever not checked, the change that made
    /// cross-agency rooms work would have quietly opened every room to everyone.
    /// </summary>
    [Fact]
    public async Task A_non_member_cannot_read_the_room()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agencyA = await CreateAgencyAsync(sa, $"ChatIn {Guid.NewGuid():N}"[..18]);
        var agencyB = await CreateAgencyAsync(sa, $"ChatOut {Guid.NewGuid():N}"[..18]);
        var (insider, _) = await CreateUserAsync(sa, agencyA, "Closer");
        var (_, outsiderName) = await CreateUserAsync(sa, agencyB, "Closer");

        var (_, centralName) = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);

        var room = await central.PostJsonAsync("/api/chat/rooms", new
        {
            name = "Private", isDirect = false, memberUserIds = new[] { insider },
        });
        var roomId = room.GetProperty("id").GetGuid();
        await central.PostJsonAsync($"/api/chat/rooms/{roomId}/messages", new { body = "Confidential." });

        var outsider = await _factory.LoginAsync(outsiderName, Password);

        // Not in their room list…
        var theirRooms = await outsider.GetJsonAsync("/api/chat/rooms");
        Assert.DoesNotContain(theirRooms.EnumerateArray(), r => r.GetProperty("id").GetGuid() == roomId);

        // …and refused outright when asking for it by id.
        var res = await outsider.GetAsync($"/api/chat/rooms/{roomId}/messages");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        // …and cannot post into it either.
        var post = await outsider.PostAsJsonAsync($"/api/chat/rooms/{roomId}/messages", new { body = "Hello?" });
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    /// <summary>
    /// An ordinary agency user still cannot start a conversation outside their own office. The
    /// cross-agency reach belongs to the roles that work across agencies, not to everyone.
    /// </summary>
    [Fact]
    public async Task An_agency_user_still_cannot_message_another_agency()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agencyA = await CreateAgencyAsync(sa, $"ChatHome {Guid.NewGuid():N}"[..18]);
        var agencyB = await CreateAgencyAsync(sa, $"ChatAway {Guid.NewGuid():N}"[..18]);
        var (_, homeName) = await CreateUserAsync(sa, agencyA, "Closer");
        var (awayId, _) = await CreateUserAsync(sa, agencyB, "Closer");

        var home = await _factory.LoginAsync(homeName, Password);
        var res = await home.PostAsJsonAsync($"/api/chat/direct/{awayId}", new { });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    /// <summary>
    /// …while the central agent can, because chasing a closer about their sale is the job.
    /// </summary>
    [Fact]
    public async Task A_central_agent_can_open_a_direct_chat_with_any_closer()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agency = await CreateAgencyAsync(sa, $"ChatDm {Guid.NewGuid():N}"[..18]);
        var (closer, closerName) = await CreateUserAsync(sa, agency, "Closer");
        var (_, centralName) = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);

        var room = await central.PostJsonAsync($"/api/chat/direct/{closer}", new { });
        var roomId = room.GetProperty("id").GetGuid();

        await central.PostJsonAsync($"/api/chat/rooms/{roomId}/messages", new { body = "About sale 12." });

        var theirs = await _factory.LoginAsync(closerName, Password);
        var rooms = await theirs.GetJsonAsync("/api/chat/rooms");
        Assert.Contains(rooms.EnumerateArray(), r => r.GetProperty("id").GetGuid() == roomId);
    }
}
