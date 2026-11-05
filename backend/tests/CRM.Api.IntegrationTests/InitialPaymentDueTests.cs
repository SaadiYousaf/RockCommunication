using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Approved sales whose first premium never arrived.
///
/// An approved sale is not money until the first draft clears. One that fails used to sit in
/// Approved indefinitely — counted in the month's approved premium, nobody told, surfacing only
/// when somebody compared a carrier statement against the queue by hand. The draft date is
/// already on the application; this acts on it.
/// </summary>
public class InitialPaymentDueTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public InitialPaymentDueTests(CrmWebAppFactory factory) => _factory = factory;

    private const string Password = "Draft!12345";

    /// <summary>Sells a policy whose initial draft falls on the given date, and approves it.</summary>
    private async Task<Guid> ApprovedSaleWithDraftAsync(HttpClient sa, DateTime initialDraftDate)
    {
        // An explicit agency: a SuperAdmin belongs to none, so a closer registered without one
        // lands at platform level and cannot close anything.
        var agencyId = (await sa.GetJsonAsync("/api/agencies"))
            .EnumerateArray().First().GetProperty("id").GetGuid();

        var userName = $"dd{Guid.NewGuid():N}"[..16];
        await sa.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password = Password,
            agencyId, roles = new[] { "Closer" },
        });
        var closer = await _factory.LoginAsync(userName, Password);

        var created = await closer.PostJsonAsync("/api/intake/close/leads", new
        {
            firstName = "Draftcase", lastName = "Testcase", maritalStatus = "Single",
            createdDate = DateTime.UtcNow,
            streetAddress = "1 Main St", city = "Phoenix", state = "AZ", zipcode = "85004",
            phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
            birthDate = new DateTime(1960, 1, 1), ageYears = 66,
            email = $"dd-{Guid.NewGuid():N}@example.com",
            jornayaLeadId = Guid.NewGuid().ToString("N"),
        });

        var sold = await closer.PostJsonAsync($"/api/intake/close/{created.GetProperty("leadId").GetGuid()}", new
        {
            status = "CompleteAndSold",
            application = new
            {
                healthConditions = "None disclosed", gender = "Female", age = 66,
                smokerStatus = "Non-smoker", name = "Draftcase Testcase",
                dateOfBirth = new DateTime(1960, 1, 1),
                address = "1 Main St, Phoenix AZ 85004",
                carrier = "Mutual of Omaha", plan = "Whole Life",
                faceAmount = 10000m, premium = 40m,
                email = $"app-{Guid.NewGuid():N}@example.com",
                beneficiary = "A Relative", secondBeneficiary = (string?)null,
                initialDraftDate = initialDraftDate,
                futureDraftDate = (DateTime?)null,
                phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
                altPhone = (string?)null, primaryDoctor = "Dr Who",
                social = "000-00-0000", bornIn = "Arizona", driversLicense = "D1234567",
                height = "5'4\"", weight = "150",
                accountType = "Checking", bankName = "Wells Fargo",
                accountNumber = "000123456789", routingNumber = "121000248",
                banking198Reason = "Confirmed on the recorded line.",
            },
        });
        var saleId = sold.GetProperty("saleId").GetGuid();

        (await sa.PostAsJsonAsync($"/api/intake/validate/{saleId}/status", new
        {
            status = "Approved", carrierApproved = "Mutual of Omaha",
            coverageApproved = 10000m, premiumApproved = 40m, planApproved = "Whole Life",
        })).EnsureSuccessStatusCode();

        return saleId;
    }

    /// <summary>Runs one sweep, the way the hosted service does on its timer.</summary>
    private async Task SweepAsync()
    {
        var svc = _factory.Services.GetServices<IHostedService>()
            .OfType<CRM.Infrastructure.BackgroundJobs.InitialPaymentDueService>()
            .Single();
        await svc.TickAsync(CancellationToken.None);
    }

    private static async Task<string?> StatusOfAsync(HttpClient client, Guid saleId)
    {
        var queue = await client.GetJsonAsync("/api/intake/validate/queue");
        return queue.EnumerateArray()
            .Where(s => s.GetProperty("saleId").GetGuid() == saleId)
            .Select(s => s.GetProperty("status").GetString())
            .FirstOrDefault();
    }

    /// <summary>The whole point: a draft date that has passed moves the sale off Approved.</summary>
    [Fact]
    public async Task An_approved_sale_whose_draft_date_passed_becomes_pending_initial_payment()
    {
        var sa = await _factory.LoginSuperAdminAsync();
        var saleId = await ApprovedSaleWithDraftAsync(sa, DateTime.UtcNow.Date.AddDays(-3));

        Assert.Equal("Approved", await StatusOfAsync(sa, saleId));

        await SweepAsync();

        Assert.Equal("PendingInitialPayment", await StatusOfAsync(sa, saleId));
    }

    /// <summary>
    /// A draft due in the future is not late. Moving it early would tell a closer their payment
    /// failed before the carrier had even tried to take it.
    /// </summary>
    [Fact]
    public async Task A_draft_date_still_to_come_is_left_alone()
    {
        var sa = await _factory.LoginSuperAdminAsync();
        var saleId = await ApprovedSaleWithDraftAsync(sa, DateTime.UtcNow.Date.AddDays(14));

        await SweepAsync();

        Assert.Equal("Approved", await StatusOfAsync(sa, saleId));
    }

    /// <summary>
    /// A draft due TODAY is not late today — the carrier still has the day to take it.
    /// </summary>
    [Fact]
    public async Task A_draft_due_today_is_not_yet_overdue()
    {
        var sa = await _factory.LoginSuperAdminAsync();
        var saleId = await ApprovedSaleWithDraftAsync(sa, DateTime.UtcNow.Date);

        await SweepAsync();

        Assert.Equal("Approved", await StatusOfAsync(sa, saleId));
    }

    /// <summary>
    /// A background job must not overrule a person. Once somebody records Active Paid, a passed
    /// draft date means the payment DID arrive — moving it to "pending payment" would be wrong
    /// and would undo their work on a timer.
    /// </summary>
    [Fact]
    public async Task A_sale_somebody_marked_active_paid_is_not_dragged_back()
    {
        var sa = await _factory.LoginSuperAdminAsync();
        var saleId = await ApprovedSaleWithDraftAsync(sa, DateTime.UtcNow.Date.AddDays(-3));

        (await sa.PostAsJsonAsync($"/api/intake/validate/{saleId}/status",
            new { status = "ActivePaid" })).EnsureSuccessStatusCode();

        await SweepAsync();

        Assert.Equal("ActivePaid", await StatusOfAsync(sa, saleId));
    }
}
