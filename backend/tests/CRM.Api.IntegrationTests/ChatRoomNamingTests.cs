using System.Net.Http.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// What a direct conversation is called, and who it is called that for.
///
/// The stored name is written once, by whoever starts the conversation, as "DM: &lt;the other
/// person&gt;". That is right for the creator and wrong for everyone else — the person who did not
/// start it saw their OWN name as the title. Someone whose five colleagues had each messaged them
/// first saw five conversations all labelled with their own name and no way to tell them apart.
/// </summary>
public class ChatRoomNamingTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public ChatRoomNamingTests(CrmWebAppFactory factory) => _factory = factory;

    private const string Password = "ChatName!123";

    private static async Task<(Guid Id, string UserName)> CreateAsync(HttpClient admin)
    {
        var userName = $"cn{Guid.NewGuid():N}"[..16];
        var created = await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password = Password, roles = new[] { "Closer" },
        });
        return (created.GetProperty("id").GetGuid(), userName);
    }

    /// <summary>Each side sees the OTHER person's name, not the name that happens to be stored.</summary>
    [Fact]
    public async Task Both_sides_of_a_direct_conversation_see_the_other_person()
    {
        var admin = await _factory.LoginAdminAsync();
        var (aliceId, aliceName) = await CreateAsync(admin);
        var (bobId, bobName) = await CreateAsync(admin);

        // Alice starts it, so the stored name is written from HER point of view.
        var alice = await _factory.LoginAsync(aliceName, Password);
        await alice.PostJsonAsync($"/api/chat/direct/{bobId}", new { });

        var aliceRooms = await alice.GetJsonAsync("/api/chat/rooms");
        var aliceRoom = aliceRooms.EnumerateArray().First(r => r.GetProperty("isDirect").GetBoolean());
        Assert.Contains(bobName, aliceRoom.GetProperty("name").GetString() ?? "");
        Assert.DoesNotContain(aliceName, aliceRoom.GetProperty("name").GetString() ?? "");

        // Bob did not create it. He must still see Alice, not himself.
        var bob = await _factory.LoginAsync(bobName, Password);
        var bobRooms = await bob.GetJsonAsync("/api/chat/rooms");
        var bobRoom = bobRooms.EnumerateArray().First(r => r.GetProperty("isDirect").GetBoolean());

        Assert.Contains(aliceName, bobRoom.GetProperty("name").GetString() ?? "");
        Assert.DoesNotContain(bobName, bobRoom.GetProperty("name").GetString() ?? "");

        // …and it is one conversation, seen from two sides.
        Assert.Equal(aliceRoom.GetProperty("id").GetGuid(), bobRoom.GetProperty("id").GetGuid());
    }

    /// <summary>A named group keeps its name — that is the entire point of naming one.</summary>
    [Fact]
    public async Task A_group_room_keeps_the_name_it_was_given()
    {
        var admin = await _factory.LoginAdminAsync();
        var (otherId, _) = await CreateAsync(admin);
        var (_, meName) = await CreateAsync(admin);
        var me = await _factory.LoginAsync(meName, Password);

        var name = $"Floor {Guid.NewGuid():N}"[..14];
        await me.PostJsonAsync("/api/chat/rooms", new
        {
            name, isDirect = false, memberUserIds = new[] { otherId },
        });

        var rooms = await me.GetJsonAsync("/api/chat/rooms");
        var group = rooms.EnumerateArray().First(r => !r.GetProperty("isDirect").GetBoolean());
        Assert.Equal(name, group.GetProperty("name").GetString());
    }
}
