using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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
    public async Task Poll_can_be_created_voted_and_revote_moves_the_vote()
    {
        var admin = await _factory.LoginAdminAsync();

        var post = await admin.PostJsonAsync("/api/feed",
            new { body = "Lunch spot?", kind = "Poll", options = new[] { "Pizza", "Sushi", "Tacos" } });
        var postId = post.GetProperty("id").GetGuid();
        Assert.Equal("Poll", post.GetProperty("kind").GetString());
        var options = post.GetProperty("poll").GetProperty("options");
        Assert.Equal(3, options.GetArrayLength());
        var pizzaId = options[0].GetProperty("id").GetGuid();
        var sushiId = options[1].GetProperty("id").GetGuid();

        // Vote Pizza.
        (await admin.PostAsJsonAsync($"/api/feed/{postId}/vote", new { optionId = pizzaId })).EnsureSuccessStatusCode();
        var afterPizza = await GetPoll(admin, postId);
        Assert.Equal(1, afterPizza.GetProperty("totalVotes").GetInt32());
        Assert.Equal(pizzaId, afterPizza.GetProperty("myOptionId").GetGuid());

        // Re-vote Sushi — the single vote MOVES (still one total, Pizza back to 0).
        (await admin.PostAsJsonAsync($"/api/feed/{postId}/vote", new { optionId = sushiId })).EnsureSuccessStatusCode();
        var afterSushi = await GetPoll(admin, postId);
        Assert.Equal(1, afterSushi.GetProperty("totalVotes").GetInt32());
        Assert.Equal(sushiId, afterSushi.GetProperty("myOptionId").GetGuid());

        // Tap the current choice again — un-votes.
        (await admin.PostAsJsonAsync($"/api/feed/{postId}/vote", new { optionId = sushiId })).EnsureSuccessStatusCode();
        var afterClear = await GetPoll(admin, postId);
        Assert.Equal(0, afterClear.GetProperty("totalVotes").GetInt32());
        Assert.Equal(JsonValueKind.Null, afterClear.GetProperty("myOptionId").ValueKind);
    }

    [Fact]
    public async Task Poll_with_fewer_than_two_options_is_rejected()
    {
        var admin = await _factory.LoginAdminAsync();
        var resp = await admin.PostAsJsonAsync("/api/feed",
            new { body = "One choice?", kind = "Poll", options = new[] { "Only one" } });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private static async Task<System.Text.Json.JsonElement> GetPoll(System.Net.Http.HttpClient client, Guid postId)
    {
        var feed = await client.GetJsonAsync("/api/feed?take=20");
        return feed.EnumerateArray().First(p => p.GetProperty("id").GetGuid() == postId).GetProperty("poll");
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
