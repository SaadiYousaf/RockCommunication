using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// A central (SMH-level) Submission Agent: one person who validates sales for EVERY agency.
///
/// The alternative — a submission agent per agency — means the same sale is worked by whoever
/// happens to belong to the right tenant, and nobody has a single queue of everything waiting on
/// Head Office. The whole point of the role is that it spans agencies, so these tests set up two
/// agencies with a real sale in each and then insist that one agent can see and finish both.
/// </summary>
public class CentralSubmissionAgentTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public CentralSubmissionAgentTests(CrmWebAppFactory factory) => _factory = factory;

    private const string Password = "Central!123";

    /// <summary>A second agency, so "across agencies" means something.</summary>
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

    /// <summary>A user in a named agency (or, with Guid.Empty, at platform level with no agency).</summary>
    private static async Task<string> CreateUserAsync(HttpClient superAdmin, Guid agencyId, string role)
    {
        var userName = $"ca{Guid.NewGuid():N}"[..16];
        await superAdmin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com",
            userName,
            password = Password,
            agencyId,
            roles = new[] { role },
        });
        return userName;
    }

    /// <summary>A closer in the given agency sells a policy, leaving a sale for submission.</summary>
    private async Task<Guid> SellAsync(HttpClient superAdmin, Guid agencyId, string customerFirstName)
    {
        var closerName = await CreateUserAsync(superAdmin, agencyId, "Closer");
        var closer = await _factory.LoginAsync(closerName, Password);

        var created = await closer.PostJsonAsync("/api/intake/close/leads", new
        {
            firstName = customerFirstName,
            lastName = "Crossagency",
            maritalStatus = "Single",
            createdDate = DateTime.UtcNow,
            streetAddress = "1 Main St",
            city = "Phoenix",
            state = "AZ",
            zipcode = "85004",
            phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
            birthDate = new DateTime(1960, 1, 1),
            ageYears = 66,
            email = $"{customerFirstName.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
            jornayaLeadId = Guid.NewGuid().ToString("N"),
        });

        var sold = await closer.PostJsonAsync($"/api/intake/close/{created.GetProperty("leadId").GetGuid()}", new
        {
            status = "CompleteAndSold",
            application = new
            {
                healthConditions = "None disclosed",
                gender = "Female",
                age = 66,
                smokerStatus = "Non-smoker",
                name = $"{customerFirstName} Crossagency",
                dateOfBirth = new DateTime(1960, 1, 1),
                address = "1 Main St, Phoenix AZ 85004",
                carrier = "Mutual of Omaha",
                plan = "Whole Life",
                faceAmount = 10000m,
                premium = 40m,
                email = $"app-{Guid.NewGuid():N}@example.com",
                beneficiary = "A Relative",
                secondBeneficiary = (string?)null,
                initialDraftDate = DateTime.UtcNow.AddDays(14),
                futureDraftDate = (DateTime?)null,
                phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
                altPhone = (string?)null,
                primaryDoctor = "Dr Who",
                social = "000-00-0000",
                bornIn = "Arizona",
                driversLicense = "D1234567",
                height = "5'4\"",
                weight = "150",
                accountType = "Checking",
                bankName = "Wells Fargo",
                accountNumber = "000123456789",
                routingNumber = "121000248",
                banking198Reason = "Confirmed on the recorded line.",
            },
        });
        return sold.GetProperty("saleId").GetGuid();
    }

    /// <summary>
    /// The central agent's queue holds work from every agency at once. This is the behaviour the
    /// role exists for; without it there is no single place to see what Head Office owes an answer on.
    /// </summary>
    [Fact]
    public async Task One_agent_sees_sales_from_every_agency_in_one_queue()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agencyA = (await sa.GetJsonAsync("/api/agencies")).EnumerateArray().First().GetProperty("id").GetGuid();
        var agencyB = await CreateAgencyAsync(sa, $"Second Agency {Guid.NewGuid():N}"[..24]);

        var saleA = await SellAsync(sa, agencyA, "Alpha");
        var saleB = await SellAsync(sa, agencyB, "Bravo");

        // The central agent: a Validator bound to NO agency.
        var centralName = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);

        var queue = await central.GetJsonAsync("/api/intake/validate/queue");
        var ids = queue.EnumerateArray().Select(s => s.GetProperty("saleId").GetGuid()).ToHashSet();

        Assert.Contains(saleA, ids);
        Assert.Contains(saleB, ids);

        // And each row says which agency it came from, or the agent cannot tell them apart.
        var rowB = queue.EnumerateArray().Single(s => s.GetProperty("saleId").GetGuid() == saleB);
        Assert.Equal(agencyB, rowB.GetProperty("agencyId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(rowB.GetProperty("agencyName").GetString()));
    }

    /// <summary>Verifying is the job, and it has to work on a sale from any agency.</summary>
    [Fact]
    public async Task The_agent_can_approve_a_sale_in_any_agency()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agencyB = await CreateAgencyAsync(sa, $"Approve Agency {Guid.NewGuid():N}"[..24]);
        var saleB = await SellAsync(sa, agencyB, "Charlie");

        var centralName = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);

        var result = await central.PostJsonAsync($"/api/intake/validate/{saleB}/status", new
        {
            status = "Approved",
            carrierApproved = "Mutual of Omaha",
            coverageApproved = 10000m,
            premiumApproved = 42.50m,
            planApproved = "Whole Life",
        });

        Assert.Equal("Approved", result.GetProperty("status").GetString());
    }

    /// <summary>
    /// The application itself — the PII the agent reads to check the sale against the carrier — must
    /// open for a lead in another agency. A queue you can see but not open is useless.
    /// </summary>
    [Fact]
    public async Task The_agent_can_open_an_application_from_another_agency()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agencyB = await CreateAgencyAsync(sa, $"Open Agency {Guid.NewGuid():N}"[..24]);
        var saleB = await SellAsync(sa, agencyB, "Delta");

        var centralName = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);

        var row = (await central.GetJsonAsync("/api/intake/validate/queue"))
            .EnumerateArray().Single(s => s.GetProperty("saleId").GetGuid() == saleB);
        var leadId = row.GetProperty("leadId").GetGuid();

        var res = await central.GetAsync($"/api/intake/validate/{leadId}");
        Assert.True(res.IsSuccessStatusCode,
            $"a central submission agent should be able to open a lead in any agency; got {res.StatusCode}");
    }

    /// <summary>
    /// The sidebar badge has to count every agency's work. If it reports only one agency's, the
    /// agent trusts the number and leaves the rest sitting there.
    ///
    /// Measured as a before/after delta rather than an absolute number: this class shares one
    /// database across its tests, so the only figure that means anything is how much the counter
    /// moves when a brand-new agency makes a sale.
    /// </summary>
    [Fact]
    public async Task The_queue_counter_counts_every_agency()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var centralName = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);

        var before = (await central.GetJsonAsync("/api/work-queues/counts"))
            .GetProperty("submissionQueue").GetInt32();

        var agencyB = await CreateAgencyAsync(sa, $"Count Agency {Guid.NewGuid():N}"[..24]);
        await SellAsync(sa, agencyB, "Echo");

        var after = (await central.GetJsonAsync("/api/work-queues/counts"))
            .GetProperty("submissionQueue").GetInt32();

        Assert.Equal(before + 1, after);
    }

    /// <summary>
    /// Leaving a note on a sale in another agency.
    ///
    /// This was broken: the note handler read the sale through the tenant filter, which for an agent
    /// carrying no agency scopes to Guid.Empty and matches nothing. Every note on every sale came
    /// back "not found" — on the one role whose entire job is other agencies' sales.
    /// </summary>
    [Fact]
    public async Task The_agent_can_leave_a_note_on_a_sale_in_another_agency()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agencyB = await CreateAgencyAsync(sa, $"Note Agency {Guid.NewGuid():N}"[..24]);
        var saleB = await SellAsync(sa, agencyB, "Hotel");

        var centralName = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);

        var res = await central.PutAsJsonAsync($"/api/intake/validate/{saleB}/comment",
            new { comment = "Carrier wants a second signature." });
        Assert.True(res.IsSuccessStatusCode,
            $"a central submission agent should be able to note any agency's sale; got {res.StatusCode}");

        // And it is actually on the sale, readable from the queue.
        var row = (await central.GetJsonAsync("/api/intake/validate/queue"))
            .EnumerateArray().Single(s => s.GetProperty("saleId").GetGuid() == saleB);
        Assert.Equal("Carrier wants a second signature.", row.GetProperty("submissionComment").GetString());
    }

    /// <summary>
    /// Promoting a submission agent who is ALREADY doing the job for one company.
    ///
    /// This is the case that was impossible: cross-agency reach comes from having no agency, and the
    /// only way to get there was to create a fresh account, because the move-agency path insisted on
    /// a real agency row. An agency's existing submission agent could not be widened, so in practice
    /// "work across agencies" meant "abandon the account you have been using".
    /// </summary>
    [Fact]
    public async Task An_existing_agency_submission_agent_can_be_promoted_to_work_across_agencies()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agencyA = (await sa.GetJsonAsync("/api/agencies")).EnumerateArray().First().GetProperty("id").GetGuid();
        var agencyB = await CreateAgencyAsync(sa, $"Promote Agency {Guid.NewGuid():N}"[..24]);
        var saleB = await SellAsync(sa, agencyB, "Golf");

        // An ordinary, agency-scoped submission agent. Agency B's sale is none of their business.
        var userName = $"ca{Guid.NewGuid():N}"[..16];
        var created = await sa.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password = Password,
            agencyId = agencyA, roles = new[] { "Validator" },
        });
        var userId = created.GetProperty("id").GetGuid();

        var before = await _factory.LoginAsync(userName, Password);
        Assert.DoesNotContain(saleB, (await before.GetJsonAsync("/api/intake/validate/queue"))
            .EnumerateArray().Select(s => s.GetProperty("saleId").GetGuid()));

        // Promote: no agency at all.
        await sa.PutJsonAsync($"/api/admin/users/{userId}/agency", new { agencyId = (Guid?)null });

        // After signing in again they work the whole platform.
        var after = await _factory.LoginAsync(userName, Password);
        Assert.Contains(saleB, (await after.GetJsonAsync("/api/intake/validate/queue"))
            .EnumerateArray().Select(s => s.GetProperty("saleId").GetGuid()));
    }

    /// <summary>
    /// The agency is a CLAIM in the access token, so the database write alone changes nothing for a
    /// session already running. Narrowing somebody back to one company has to end the session that
    /// still carries cross-agency reach — otherwise the dangerous direction stays open until the
    /// token happens to expire.
    /// </summary>
    [Fact]
    public async Task Re_scoping_a_user_ends_the_session_that_carried_the_old_scope()
    {
        var sa = await _factory.LoginSuperAdminAsync();
        var agencyA = (await sa.GetJsonAsync("/api/agencies")).EnumerateArray().First().GetProperty("id").GetGuid();

        var userName = $"ca{Guid.NewGuid():N}"[..16];
        var created = await sa.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password = Password,
            agencyId = Guid.Empty, roles = new[] { "Validator" },
        });
        var userId = created.GetProperty("id").GetGuid();

        var anon = _factory.CreateClient();
        var login = await anon.PostJsonAsync("/api/auth/login", new { userNameOrEmail = userName, password = Password });
        var refreshToken = login.GetProperty("refreshToken").GetString();

        // Narrow them to a single agency.
        await sa.PutJsonAsync($"/api/admin/users/{userId}/agency", new { agencyId = agencyA });

        var refreshed = await anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.False(refreshed.IsSuccessStatusCode,
            "re-scoping a user must end the sessions still carrying their old agency");
    }

    /// <summary>
    /// Taking a Closer's agency away does not promote them, it breaks them: every lead queue is
    /// scoped by agency, so they would sign in to a day of empty screens. Refused with a message
    /// that says what to do instead, and in the user's own words — no role codes.
    /// </summary>
    [Fact]
    public async Task A_role_with_no_cross_agency_mode_cannot_be_sent_platform_level()
    {
        var sa = await _factory.LoginSuperAdminAsync();
        var agencyA = (await sa.GetJsonAsync("/api/agencies")).EnumerateArray().First().GetProperty("id").GetGuid();

        var userName = $"ca{Guid.NewGuid():N}"[..16];
        var created = await sa.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password = Password,
            agencyId = agencyA, roles = new[] { "Closer" },
        });
        var userId = created.GetProperty("id").GetGuid();

        var res = await sa.PutAsJsonAsync($"/api/admin/users/{userId}/agency", new { agencyId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("Submission Agent", body);
        Assert.DoesNotContain("Validator", body);          // no internal role codes in user-facing copy
    }

    /// <summary>
    /// Moving somebody between tenants — or out of all of them — is a platform operation. An agency
    /// admin must never be able to lift one of their own users to cross-agency reach.
    /// </summary>
    [Fact]
    public async Task An_agency_admin_cannot_make_anybody_cross_agency()
    {
        var sa = await _factory.LoginSuperAdminAsync();
        var admin = await _factory.LoginAdminAsync();
        var agencyA = (await sa.GetJsonAsync("/api/agencies")).EnumerateArray().First().GetProperty("id").GetGuid();

        var userName = $"ca{Guid.NewGuid():N}"[..16];
        var created = await sa.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password = Password,
            agencyId = agencyA, roles = new[] { "Validator" },
        });
        var userId = created.GetProperty("id").GetGuid();

        var res = await admin.PutAsJsonAsync($"/api/admin/users/{userId}/agency", new { agencyId = (Guid?)null });
        Assert.True(res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"only the platform operator may re-scope a user; got {res.StatusCode}");
    }

    /// <summary>
    /// The limit that makes the role safe to hand out: an AGENCY-scoped validator stays in their own
    /// agency. Cross-agency reach comes from having no agency, not from holding the role.
    /// </summary>
    [Fact]
    public async Task An_agency_scoped_validator_still_sees_only_their_own_agency()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agencyA = (await sa.GetJsonAsync("/api/agencies")).EnumerateArray().First().GetProperty("id").GetGuid();
        var agencyB = await CreateAgencyAsync(sa, $"Scoped Agency {Guid.NewGuid():N}"[..24]);
        var saleB = await SellAsync(sa, agencyB, "Foxtrot");

        var scopedName = await CreateUserAsync(sa, agencyA, "Validator");
        var scoped = await _factory.LoginAsync(scopedName, Password);

        var queue = await scoped.GetJsonAsync("/api/intake/validate/queue");
        var ids = queue.EnumerateArray().Select(s => s.GetProperty("saleId").GetGuid()).ToHashSet();
        Assert.DoesNotContain(saleB, ids);

        // Not even by id: guessing a sale id must not reach into another tenant. "Completed" needs
        // no extra fields, so a 404 here is the tenant boundary refusing — not field validation
        // happening to fire first and hide what the handler would have done.
        var res = await scoped.PostAsJsonAsync($"/api/intake/validate/{saleB}/status", new { status = "Completed" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
