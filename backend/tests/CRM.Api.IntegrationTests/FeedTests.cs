using System.Linq;
using System.Net;
using System.Net.Http.Json;

namespace CRM.Api.IntegrationTests;

/// <summary>Pulse feed: posting, emoji reactions (toggle), comments, and input validation.</summary>
public class FeedTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public FeedTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Post_react_and_comment_round_trip()
    {
        var admin = await _factory.LoginAdminAsync();

        var post = await admin.PostJsonAsync("/api/feed", new { body = "Hello team 🎉" });
        var postId = post.GetProperty("id").GetGuid();
        Assert.Equal("Hello team 🎉", post.GetProperty("body").GetString());

        (await admin.PostAsJsonAsync($"/api/feed/{postId}/react", new { emoji = "👍" })).EnsureSuccessStatusCode();
        var comment = await admin.PostJsonAsync($"/api/feed/{postId}/comments", new { body = "Nice!" });
        Assert.Equal("Nice!", comment.GetProperty("body").GetString());

        var feed = await admin.GetJsonAsync("/api/feed?take=20");
        var mine = feed.EnumerateArray().First(p => p.GetProperty("id").GetGuid() == postId);
        Assert.Equal(1, mine.GetProperty("reactions").GetArrayLength());
        Assert.True(mine.GetProperty("reactions")[0].GetProperty("mine").GetBoolean());
        Assert.Equal(1, mine.GetProperty("reactions")[0].GetProperty("count").GetInt32());
        Assert.Equal(1, mine.GetProperty("comments").GetArrayLength());
    }

    [Fact]
    public async Task Reacting_with_the_same_emoji_twice_toggles_it_off()
    {
        var admin = await _factory.LoginAdminAsync();
        var post = await admin.PostJsonAsync("/api/feed", new { body = "toggle test" });
        var postId = post.GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync($"/api/feed/{postId}/react", new { emoji = "🔥" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/feed/{postId}/react", new { emoji = "🔥" })).EnsureSuccessStatusCode();

        var feed = await admin.GetJsonAsync("/api/feed?take=20");
        var mine = feed.EnumerateArray().First(p => p.GetProperty("id").GetGuid() == postId);
        Assert.Equal(0, mine.GetProperty("reactions").GetArrayLength());
    }

    [Fact]
    public async Task Empty_post_with_no_image_is_rejected()
    {
        var admin = await _factory.LoginAdminAsync();
        var resp = await admin.PostAsJsonAsync("/api/feed", new { body = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Announcement_post_round_trips_its_kind()
    {
        var admin = await _factory.LoginAdminAsync();
        var post = await admin.PostJsonAsync("/api/feed", new { body = "Big news team", kind = "Announcement" });
        Assert.Equal("Announcement", post.GetProperty("kind").GetString());
        Assert.False(post.GetProperty("hasImage").GetBoolean());
    }

    [Fact]
    public async Task Timestamps_are_serialized_as_utc_with_a_z()
    {
        // Without the UTC converter, a SQLite-read timestamp serializes WITHOUT a zone and the browser
        // parses it as local time (a just-posted item showed "5h ago"). Every timestamp must end with 'Z'.
        var admin = await _factory.LoginAdminAsync();
        await admin.PostJsonAsync("/api/feed", new { body = "tz check" });
        var feed = await admin.GetJsonAsync("/api/feed?take=5");
        var createdAt = feed.EnumerateArray().First().GetProperty("createdAt").GetString();
        Assert.NotNull(createdAt);
        Assert.EndsWith("Z", createdAt);
    }
}
