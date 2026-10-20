using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// The duplicate check on Leads → Search.
///
/// Its whole job is to find the same customer entered twice. People do not type a phone number the
/// same way each time — one agent writes 555-123-4567, the next writes (555) 123 4567, an import
/// stores 5551234567 — so a check that compares the text as typed finds only the duplicates that
/// happen to have been typed identically, and reports a clean database to someone looking straight
/// at two copies of the same lead.
/// </summary>
public class DuplicateLeadScanTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public DuplicateLeadScanTests(CrmWebAppFactory factory) => _factory = factory;

    /// <summary>Captures a lead with the phone number written exactly as given.</summary>
    private static async Task<Guid> AddLeadAsync(HttpClient client, string first, string phone)
    {
        var created = await client.PostJsonAsync("/api/intake/leads", new
        {
            firstName = first,
            lastName = "Duplicatecase",
            maritalStatus = "Single",
            createdDate = DateTime.UtcNow,
            streetAddress = "1 Main St",
            city = "Phoenix",
            state = "AZ",
            zipcode = "85004",
            phoneNumber = phone,
            birthDate = new DateTime(1960, 1, 1),
            ageYears = 66,
            email = $"{first.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
            jornayaLeadId = Guid.NewGuid().ToString("N"),
        });
        return created.GetProperty("leadId").GetGuid();
    }

    /// <summary>The groups the scan reports, keyed by the digits of the number.</summary>
    private static async Task<List<(string Key, int Count)>> ScanAsync(HttpClient client)
    {
        var groups = await client.GetJsonAsync("/api/leads/duplicates");
        return groups.EnumerateArray()
            .Select(g => (g.GetProperty("key").GetString() ?? "", g.GetProperty("leads").GetArrayLength()))
            .ToList();
    }

    /// <summary>
    /// The case the check was built for, and the one it already handled: the same number typed the
    /// same way twice.
    /// </summary>
    [Fact]
    public async Task Two_leads_with_the_identical_number_are_reported()
    {
        var admin = await _factory.LoginAdminAsync();
        var phone = $"555{Random.Shared.Next(1000000, 9999999)}";

        await AddLeadAsync(admin, "Identicalone", phone);
        await AddLeadAsync(admin, "Identicaltwo", phone);

        var groups = await ScanAsync(admin);
        Assert.Contains(groups, g => g.Key.Contains(phone) && g.Count >= 2);
    }

    /// <summary>
    /// THE bug. The same customer, entered by two people who punctuate differently. These are one
    /// number — 555-123-4567 and 5551234567 reach the same telephone — and the check reported
    /// nothing at all, because it compared the strings rather than the digits.
    /// </summary>
    [Fact]
    public async Task The_same_number_written_differently_is_still_a_duplicate()
    {
        var admin = await _factory.LoginAdminAsync();
        var digits = $"555{Random.Shared.Next(1000000, 9999999)}";
        var dashed = $"{digits[..3]}-{digits[3..6]}-{digits[6..]}";

        await AddLeadAsync(admin, "Plainform", digits);
        await AddLeadAsync(admin, "Dashedform", dashed);

        var groups = await ScanAsync(admin);
        Assert.Contains(groups, g => g.Key.Contains(digits) && g.Count >= 2);
    }

    /// <summary>
    /// A leading country code is the same telephone too — this is how a number arrives from a web
    /// form, while the floor types the last ten digits.
    /// </summary>
    [Fact]
    public async Task A_country_code_does_not_make_it_a_different_number()
    {
        var admin = await _factory.LoginAdminAsync();
        var digits = $"555{Random.Shared.Next(1000000, 9999999)}";

        await AddLeadAsync(admin, "Tendigit", digits);
        await AddLeadAsync(admin, "Countrycode", $"+1 ({digits[..3]}) {digits[3..6]}-{digits[6..]}");

        var groups = await ScanAsync(admin);
        Assert.Contains(groups, g => g.Key.Contains(digits) && g.Count >= 2);
    }

    /// <summary>
    /// And it must not invent duplicates. Two genuinely different customers stay apart, or the
    /// screen becomes noise nobody acts on.
    /// </summary>
    [Fact]
    public async Task Two_different_numbers_are_not_reported_together()
    {
        var admin = await _factory.LoginAdminAsync();
        var one = $"555{Random.Shared.Next(1000000, 9999999)}";
        var two = $"555{Random.Shared.Next(1000000, 9999999)}";

        await AddLeadAsync(admin, "Separateone", one);
        await AddLeadAsync(admin, "Separatetwo", two);

        var groups = await ScanAsync(admin);
        Assert.DoesNotContain(groups, g => g.Key.Contains(one) && g.Count >= 2);
    }
}
