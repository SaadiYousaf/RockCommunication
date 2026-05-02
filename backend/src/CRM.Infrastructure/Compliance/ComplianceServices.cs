using CRM.Application.Common.Compliance;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Compliance;

public class PhoneNormalizer : IPhoneNormalizer
{
    public string Normalize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits.StartsWith('1')) digits = digits.Substring(1);
        return digits;
    }
}

public class DncChecker : IDncChecker
{
    private readonly AppDbContext _db;
    private readonly IPhoneNormalizer _normalizer;

    public DncChecker(AppDbContext db, IPhoneNormalizer normalizer)
    {
        _db = db;
        _normalizer = normalizer;
    }

    public async Task<bool> IsBlockedAsync(Guid agencyId, string phone, CancellationToken ct = default)
    {
        var normalized = _normalizer.Normalize(phone);
        if (string.IsNullOrEmpty(normalized)) return false;

        return await _db.DncEntries.AsNoTracking()
            .AnyAsync(e => e.AgencyId == agencyId
                && e.PhoneNormalized == normalized
                && (e.ExpiresAt == null || e.ExpiresAt > DateTime.UtcNow), ct);
    }
}

public class TcpaWindowChecker : ITcpaWindowChecker
{
    // Map US state code → IANA tz name. (Subset; expand as needed.)
    private static readonly Dictionary<string, string> StateTz = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "America/Chicago", ["AK"] = "America/Anchorage", ["AZ"] = "America/Phoenix",
        ["AR"] = "America/Chicago", ["CA"] = "America/Los_Angeles", ["CO"] = "America/Denver",
        ["CT"] = "America/New_York", ["DE"] = "America/New_York", ["FL"] = "America/New_York",
        ["GA"] = "America/New_York", ["HI"] = "Pacific/Honolulu", ["ID"] = "America/Boise",
        ["IL"] = "America/Chicago", ["IN"] = "America/Indiana/Indianapolis", ["IA"] = "America/Chicago",
        ["KS"] = "America/Chicago", ["KY"] = "America/New_York", ["LA"] = "America/Chicago",
        ["ME"] = "America/New_York", ["MD"] = "America/New_York", ["MA"] = "America/New_York",
        ["MI"] = "America/Detroit", ["MN"] = "America/Chicago", ["MS"] = "America/Chicago",
        ["MO"] = "America/Chicago", ["MT"] = "America/Denver", ["NE"] = "America/Chicago",
        ["NV"] = "America/Los_Angeles", ["NH"] = "America/New_York", ["NJ"] = "America/New_York",
        ["NM"] = "America/Denver", ["NY"] = "America/New_York", ["NC"] = "America/New_York",
        ["ND"] = "America/Chicago", ["OH"] = "America/New_York", ["OK"] = "America/Chicago",
        ["OR"] = "America/Los_Angeles", ["PA"] = "America/New_York", ["RI"] = "America/New_York",
        ["SC"] = "America/New_York", ["SD"] = "America/Chicago", ["TN"] = "America/Chicago",
        ["TX"] = "America/Chicago", ["UT"] = "America/Denver", ["VT"] = "America/New_York",
        ["VA"] = "America/New_York", ["WA"] = "America/Los_Angeles", ["WV"] = "America/New_York",
        ["WI"] = "America/Chicago", ["WY"] = "America/Denver", ["DC"] = "America/New_York",
    };

    public bool IsWithinPermittedWindow(string? state, DateTime utcNow)
    {
        var tz = state is not null && StateTz.TryGetValue(state, out var z)
            ? TryFindTimeZone(z) : TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), tz);
        var hour = local.Hour;
        return hour >= 8 && hour < 21;
    }

    private static TimeZoneInfo TryFindTimeZone(string ianaId)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(ianaId); }
        catch { return TimeZoneInfo.Utc; }
    }
}

public class ComplianceGuard : IComplianceGuard
{
    private readonly IDncChecker _dnc;
    private readonly ITcpaWindowChecker _tcpa;

    public ComplianceGuard(IDncChecker dnc, ITcpaWindowChecker tcpa)
    {
        _dnc = dnc;
        _tcpa = tcpa;
    }

    public async Task<ComplianceCheck> CheckOutboundDialAsync(Guid agencyId, string phone, string? state, CancellationToken ct = default)
    {
        var warnings = new List<string>();

        if (await _dnc.IsBlockedAsync(agencyId, phone, ct))
            return new ComplianceCheck(false, "Phone is on the DNC list.", warnings);

        if (!_tcpa.IsWithinPermittedWindow(state, DateTime.UtcNow))
            return new ComplianceCheck(false, $"Outside TCPA calling hours (8am–9pm local{(state is null ? "" : $" {state}")}).", warnings);

        if (string.IsNullOrEmpty(state))
            warnings.Add("Lead state unknown; using UTC for TCPA window — confirm before dialing.");

        return new ComplianceCheck(true, null, warnings);
    }
}
