using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Calling a colleague from chat.
///
/// The number is never sent to the browser — the caller asks for a person by id and the server
/// resolves the number and dials it. So the tests are about who may be dialled: your own office
/// only, never yourself, and never someone who hasn't given a number to call.
/// </summary>
public class CallColleagueTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public CallColleagueTests(CrmWebAppFactory factory) => _factory = factory;

    private const string Password = "Colleague!123";

    private static async Task<(Guid Id, string UserName)> CreateAsync(
        HttpClient admin, string role, string? phone = null)
    {
        var userName = $"cc{Guid.NewGuid():N}"[..16];
        var created = await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com",
            userName,
            password = Password,
            roles = new[] { role },
            phoneNumber = phone,
        });
        return (created.GetProperty("id").GetGuid(), userName);
    }

    [Fact]
    public async Task Calling_yourself_is_refused()
    {
        var admin = await _factory.LoginAdminAsync();
        var (id, userName) = await CreateAsync(admin, "Closer", "+15551230000");
        var me = await _factory.LoginAsync(userName, Password);

        var res = await me.PostAsJsonAsync("/api/cc/calls/call-colleague", new { userId = id });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    /// <summary>
    /// A colleague with no number on their profile can't be rung. The message says so plainly —
    /// "the call failed" would send the caller hunting for a fault that isn't there.
    /// </summary>
    [Fact]
    public async Task A_colleague_without_a_number_is_refused_with_an_explanation()
    {
        var admin = await _factory.LoginAdminAsync();
        var (targetId, _) = await CreateAsync(admin, "Closer");            // no phone
        var (_, callerName) = await CreateAsync(admin, "Closer", "+15551230001");
        var caller = await _factory.LoginAsync(callerName, Password);

        var res = await caller.PostAsJsonAsync("/api/cc/calls/call-colleague", new { userId = targetId });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("phone number", await res.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unknown_user_is_not_found()
    {
        var admin = await _factory.LoginAdminAsync();
        var (_, callerName) = await CreateAsync(admin, "Closer", "+15551230002");
        var caller = await _factory.LoginAsync(callerName, Password);

        var res = await caller.PostAsJsonAsync("/api/cc/calls/call-colleague", new { userId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Signed_out_callers_are_refused()
    {
        var anon = _factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/cc/calls/call-colleague", new { userId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
