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

    /// <summary>Approves a sale, naming the licence agent it should be written under.</summary>
    private static Task<HttpResponseMessage> ApproveAsync(HttpClient client, Guid saleId, Guid licenseAgentId)
        => client.PostAsJsonAsync($"/api/intake/validate/{saleId}/status", new
        {
            status = "Approved",
            carrierApproved = "ETHOS",
            coverageApproved = 10000m,
            premiumApproved = 69m,
            planApproved = "Level",
            licenseAgentUserId = licenseAgentId,
        });

    /// <summary>Creates a licence agent inside a named agency and returns their id and username.</summary>
    private static async Task<(Guid Id, string UserName)> CreateLicenseAgentAsync(HttpClient superAdmin, Guid agencyId)
    {
        var userName = $"la{Guid.NewGuid():N}"[..16];
        var created = await superAdmin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password = Password,
            agencyId, roles = new[] { "LicenseAgent" },
        });
        return (created.GetProperty("id").GetGuid(), userName);
    }

    /// <summary>
    /// A sale sold by one agency, written under a licensed agent at another.
    ///
    /// This is the reported bug. The approval screen's Agency picker offers every agency and lists
    /// that agency's licence agents, but the save checked the agent against the SALE's agency — so
    /// choosing any other agency produced a list of names that could never be assigned, and a
    /// "not a License Agent in this sale's agency" error about an agent who plainly was one.
    /// </summary>
    [Fact]
    public async Task A_sale_can_be_written_under_a_license_agent_at_another_agency()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var selling = await CreateAgencyAsync(sa, $"Selling {Guid.NewGuid():N}"[..20]);
        var writing = await CreateAgencyAsync(sa, $"Writing {Guid.NewGuid():N}"[..20]);
        var sale = await SellAsync(sa, selling, "India");
        var (agentId, _) = await CreateLicenseAgentAsync(sa, writing);

        var centralName = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);

        var res = await ApproveAsync(central, sale, agentId);
        Assert.True(res.IsSuccessStatusCode,
            $"a licence agent at another agency should be assignable; got {res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// The money has to reach the agent.
    ///
    /// Commission rows are read back scoped to the reader's own agency, so a line filed under the
    /// SELLING agency would be invisible to an agent at another one — earned, owed, and never shown
    /// or paid. It follows the agent instead; the sale stays with the agency that sold it.
    /// </summary>
    [Fact]
    public async Task The_commission_lands_where_the_license_agent_can_see_it()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var selling = await CreateAgencyAsync(sa, $"Sell2 {Guid.NewGuid():N}"[..20]);
        var writing = await CreateAgencyAsync(sa, $"Write2 {Guid.NewGuid():N}"[..20]);
        var sale = await SellAsync(sa, selling, "Juliet");
        var (agentId, agentName) = await CreateLicenseAgentAsync(sa, writing);

        var centralName = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);
        (await ApproveAsync(central, sale, agentId)).EnsureSuccessStatusCode();

        // The agent signs in to their OWN agency and finds the commission waiting.
        var agent = await _factory.LoginAsync(agentName, Password);
        var mine = await agent.GetJsonAsync("/api/sales/commissions");
        Assert.Contains(mine.EnumerateArray(), c => c.GetProperty("saleId").GetGuid() == sale);
    }

    /// <summary>
    /// Re-assigning to an agent at a different agency MOVES the commission rather than leaving the
    /// first one behind. The old lookup was keyed on the sale's agency, so a cross-agency
    /// re-assignment missed the existing row and stranded it — the first agent still owed
    /// commission on a sale that was no longer theirs.
    /// </summary>
    [Fact]
    public async Task Re_assigning_across_agencies_moves_the_commission_instead_of_stranding_it()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var selling = await CreateAgencyAsync(sa, $"Sell3 {Guid.NewGuid():N}"[..20]);
        var first = await CreateAgencyAsync(sa, $"First3 {Guid.NewGuid():N}"[..20]);
        var second = await CreateAgencyAsync(sa, $"Second3 {Guid.NewGuid():N}"[..20]);
        var sale = await SellAsync(sa, selling, "Kilo");

        var (firstAgentId, firstAgentName) = await CreateLicenseAgentAsync(sa, first);
        var (secondAgentId, secondAgentName) = await CreateLicenseAgentAsync(sa, second);

        var centralName = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);

        (await ApproveAsync(central, sale, firstAgentId)).EnsureSuccessStatusCode();
        (await ApproveAsync(central, sale, secondAgentId)).EnsureSuccessStatusCode();

        var secondAgent = await _factory.LoginAsync(secondAgentName, Password);
        Assert.Contains((await secondAgent.GetJsonAsync("/api/sales/commissions")).EnumerateArray(),
            c => c.GetProperty("saleId").GetGuid() == sale);

        // And the agent it was taken from is no longer owed anything on this sale.
        var firstAgent = await _factory.LoginAsync(firstAgentName, Password);
        Assert.DoesNotContain((await firstAgent.GetJsonAsync("/api/sales/commissions")).EnumerateArray(),
            c => c.GetProperty("saleId").GetGuid() == sale);
    }

    /// <summary>
    /// Reopening a sale shows the agent who is on it.
    ///
    /// The approval screen's Agency picker drives the list of agents, and it opened on the SELLING
    /// agency — which does not contain an outside agent — so the Licence Agent field came back
    /// empty on a sale that plainly had one. It read as "Unspecified", and saving from there would
    /// have quietly unassigned them. The queue has to say which agency the agent belongs to.
    /// </summary>
    [Fact]
    public async Task The_assigned_agent_and_their_agency_come_back_on_the_sale()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var selling = await CreateAgencyAsync(sa, $"Sell4 {Guid.NewGuid():N}"[..20]);
        var writing = await CreateAgencyAsync(sa, $"Write4 {Guid.NewGuid():N}"[..20]);
        var sale = await SellAsync(sa, selling, "Mike");
        var (agentId, agentName) = await CreateLicenseAgentAsync(sa, writing);

        var centralName = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);
        (await ApproveAsync(central, sale, agentId)).EnsureSuccessStatusCode();

        var row = (await central.GetJsonAsync("/api/intake/validate/queue"))
            .EnumerateArray().Single(s => s.GetProperty("saleId").GetGuid() == sale);

        Assert.Equal(agentId, row.GetProperty("licenseAgentUserId").GetGuid());
        Assert.Equal(agentName, row.GetProperty("licenseAgentName").GetString());

        // The agent's OWN agency, not the one that sold it — this is what the picker reopens on.
        Assert.Equal(writing, row.GetProperty("licenseAgentAgencyId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("licenseAgentAgencyName").GetString()),
            "the picker must have a name to show rather than a bare id");
    }

    /// <summary>
    /// The selling agency's own validator sees the outside agent's NAME.
    ///
    /// Names were resolved from the caller's own agency roster, so an agent belonging to another
    /// one was simply missing and the column showed a dash — on a sale that was assigned.
    /// </summary>
    [Fact]
    public async Task An_agency_validator_sees_the_name_of_an_outside_agent()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var selling = (await sa.GetJsonAsync("/api/agencies")).EnumerateArray().First().GetProperty("id").GetGuid();
        var writing = await CreateAgencyAsync(sa, $"Write5 {Guid.NewGuid():N}"[..20]);
        var sale = await SellAsync(sa, selling, "November");
        var (agentId, agentName) = await CreateLicenseAgentAsync(sa, writing);

        var centralName = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);
        (await ApproveAsync(central, sale, agentId)).EnsureSuccessStatusCode();

        var scopedName = await CreateUserAsync(sa, selling, "Validator");
        var scoped = await _factory.LoginAsync(scopedName, Password);

        var row = (await scoped.GetJsonAsync("/api/intake/validate/queue"))
            .EnumerateArray().Single(s => s.GetProperty("saleId").GetGuid() == sale);
        Assert.Equal(agentName, row.GetProperty("licenseAgentName").GetString());
    }

    /// <summary>
    /// …and they are not stuck with it. Re-saving a sale whose agent was assigned by somebody else
    /// sends that same agent back, which the cross-agency gate is not theirs to pass — so without
    /// an explicit allowance for "unchanged", they could not set the status on their own agency's
    /// sale because of an assignment they did not make.
    /// </summary>
    [Fact]
    public async Task An_agency_validator_can_still_resave_a_sale_assigned_to_an_outside_agent()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var selling = (await sa.GetJsonAsync("/api/agencies")).EnumerateArray().First().GetProperty("id").GetGuid();
        var writing = await CreateAgencyAsync(sa, $"Write6 {Guid.NewGuid():N}"[..20]);
        var sale = await SellAsync(sa, selling, "Oscar");
        var (agentId, _) = await CreateLicenseAgentAsync(sa, writing);

        var centralName = await CreateUserAsync(sa, Guid.Empty, "Validator");
        var central = await _factory.LoginAsync(centralName, Password);
        (await ApproveAsync(central, sale, agentId)).EnsureSuccessStatusCode();

        var scopedName = await CreateUserAsync(sa, selling, "Validator");
        var scoped = await _factory.LoginAsync(scopedName, Password);

        var res = await ApproveAsync(scoped, sale, agentId);
        Assert.True(res.IsSuccessStatusCode,
            $"re-saving with the agent already on the sale must be allowed; got {res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// The limit. Cross-agency assignment is for someone who works across agencies; an agency's own
    /// validator must not be able to attribute commission to another agency's people — that is
    /// money crossing a tenant boundary on the say-so of one tenant's staff.
    /// </summary>
    [Fact]
    public async Task An_agency_scoped_validator_cannot_assign_another_agencys_license_agent()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var selling = (await sa.GetJsonAsync("/api/agencies")).EnumerateArray().First().GetProperty("id").GetGuid();
        var elsewhere = await CreateAgencyAsync(sa, $"Else {Guid.NewGuid():N}"[..20]);
        var sale = await SellAsync(sa, selling, "Lima");
        var (outsiderId, _) = await CreateLicenseAgentAsync(sa, elsewhere);

        var scopedName = await CreateUserAsync(sa, selling, "Validator");
        var scoped = await _factory.LoginAsync(scopedName, Password);

        var res = await ApproveAsync(scoped, sale, outsiderId);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        // And the refusal is in plain language — no role codes for a salesperson to decode.
        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("LicenseAgent", body);
    }

    /// <summary>
    /// The queue returns one period, and nothing outside it.
    ///
    /// This is what replaced "the newest N sales, ever" — a limit that quietly dropped the oldest
    /// rows once the queue spanned every agency. A month bounds the result by something the floor
    /// actually reconciles against, and the figures above the table then describe that whole month
    /// rather than whatever happened to fit under a number.
    /// </summary>
    [Fact]
    public async Task The_queue_returns_only_the_requested_period()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agency = await CreateAgencyAsync(sa, $"Period {Guid.NewGuid():N}"[..20]);
        var sale = await SellAsync(sa, agency, "Romeo");          // sold now

        // The month the sale was made in: first instant of it, first instant of the next.
        var now = DateTime.UtcNow;
        var thisMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonth = thisMonth.AddMonths(1);
        var lastMonth = thisMonth.AddMonths(-1);

        static string Iso(DateTime d) => d.ToString("o");

        var inPeriod = await sa.GetJsonAsync(
            $"/api/intake/validate/queue?from={Uri.EscapeDataString(Iso(thisMonth))}&to={Uri.EscapeDataString(Iso(nextMonth))}");
        Assert.Contains(inPeriod.EnumerateArray(), s => s.GetProperty("saleId").GetGuid() == sale);

        // Last month must not contain a sale made this month — and the end is exclusive, so a sale
        // made on the first instant of this month belongs to this month, not the one before.
        var previous = await sa.GetJsonAsync(
            $"/api/intake/validate/queue?from={Uri.EscapeDataString(Iso(lastMonth))}&to={Uri.EscapeDataString(Iso(thisMonth))}");
        Assert.DoesNotContain(previous.EnumerateArray(), s => s.GetProperty("saleId").GetGuid() == sale);
    }

    /// <summary>Asking for no period still returns everything, as it did before.</summary>
    [Fact]
    public async Task The_queue_without_a_period_still_returns_everything()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agency = await CreateAgencyAsync(sa, $"NoPeriod {Guid.NewGuid():N}"[..20]);
        var sale = await SellAsync(sa, agency, "Sierra");

        var all = await sa.GetJsonAsync("/api/intake/validate/queue");
        Assert.Contains(all.EnumerateArray(), s => s.GetProperty("saleId").GetGuid() == sale);
    }

    /// <summary>
    /// A SuperAdmin sees the submission queue.
    ///
    /// They did not. The queue asked "are you a Validator with no agency?" to decide whether to
    /// span agencies — and a SuperAdmin holds no Validator role, so they fell to the agency-scoped
    /// branch and were filtered to an agency id of all zeroes, which matches no sale. The platform
    /// operator opened the Submission Queue on a database full of sales and read "0 of 0 sales".
    ///
    /// It is the exact failure TenantScope was written to prevent, reappearing in a handler that
    /// rolled its own check.
    /// </summary>
    [Fact]
    public async Task A_super_admin_sees_the_submission_queue()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var agency = await CreateAgencyAsync(sa, $"SaSees {Guid.NewGuid():N}"[..20]);
        var sale = await SellAsync(sa, agency, "Papa");

        var queue = await sa.GetJsonAsync("/api/intake/validate/queue");
        Assert.Contains(queue.EnumerateArray(), s => s.GetProperty("saleId").GetGuid() == sale);
    }

    /// <summary>And the sidebar badge agrees with it, rather than reporting nothing to do.</summary>
    [Fact]
    public async Task A_super_admins_queue_badge_counts_the_same_sales()
    {
        var sa = await _factory.LoginSuperAdminAsync();

        var before = (await sa.GetJsonAsync("/api/work-queues/counts"))
            .GetProperty("submissionQueue").GetInt32();

        var agency = await CreateAgencyAsync(sa, $"SaBadge {Guid.NewGuid():N}"[..20]);
        await SellAsync(sa, agency, "Quebec");

        var after = (await sa.GetJsonAsync("/api/work-queues/counts"))
            .GetProperty("submissionQueue").GetInt32();

        Assert.Equal(before + 1, after);
    }

    /// <summary>
    /// The queue's own limit must be far above what a cross-agency queue actually holds.
    ///
    /// This is the shape of a real incident. The limit was 200 and the queue is ordered newest
    /// first, which was ample while a submission agent saw one agency — and became silent data
    /// loss the moment a central agent pooled every agency into one list. Each new sale pushed an
    /// older one past the limit, nothing on screen said the list had been cut, and sales appeared
    /// to vanish while people were working on them.
    ///
    /// Asserting on the limit rather than creating thousands of sales: the bug was the NUMBER, and
    /// a test that seeds 200 sales to prove it would take minutes and still only pin today's
    /// volume. If someone lowers this again, they have to read the sentence above first.
    /// </summary>
    [Fact]
    public void The_queue_limit_leaves_room_for_every_agency()
    {
        var limit = new CRM.Application.Intake.ValidatorQueueQuery().Take;
        Assert.True(limit >= 1000,
            $"the submission queue returns at most {limit} sales, ordered newest first. A queue " +
            "that spans every agency will pass that, and the oldest sales will drop off the bottom " +
            "with nothing to say they were cut.");
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
