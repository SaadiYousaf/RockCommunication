using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class Agency : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;

    // ---- Customer-facing branding (used by the policy welcome email) ----
    /// <summary>
    /// The agency's reply-to address for customer mail. Outbound send goes via the shared relay
    /// with the agency <see cref="Name"/> as the From display name; customer replies route here.
    /// </summary>
    public string? SenderEmail { get; set; }
    /// <summary>Storage key of the agency logo shown in customer emails. Null = no logo uploaded.</summary>
    public string? LogoKey { get; set; }

    // ---- Money display ------------------------------------------------------
    /// <summary>
    /// ISO code the UI formats sale/commission money in ("USD" or "PKR"). Sales figures are STORED
    /// in USD, so this is a display choice applied together with <see cref="ExchangeRate"/>.
    /// Payroll is PKR-native and is never converted by this setting.
    /// </summary>
    public string DisplayCurrency { get; set; } = "USD";

    /// <summary>
    /// Units of <see cref="DisplayCurrency"/> per 1 USD. 1 = show the stored figure unchanged
    /// (correct when the display currency is USD). e.g. 280 shows a $400 sale as PKR 112,000.
    /// </summary>
    public decimal ExchangeRate { get; set; } = 1m;

    /// <summary>
    /// Highest sale serial issued for this agency. Incremented atomically when a sale is created
    /// so each agency's sales are numbered 1, 2, 3… independently. See Sale.SaleNumber.
    /// </summary>
    public int LastSaleNumber { get; set; }

    // ---- Contact details ----------------------------------------------------
    // Held on the agency because support, finance and compliance all need to reach a real person,
    // and "who do we call about this agency?" previously lived only in someone's phone.
    /// <summary>Main contact number for the agency (free-form; formats vary by country).</summary>
    public string? Phone { get; set; }
    /// <summary>Postal/registered address, one field — splitting it invites half-filled forms.</summary>
    public string? Address { get; set; }
    /// <summary>Public website, if any.</summary>
    public string? Website { get; set; }

    public ICollection<Team> Teams { get; set; } = new List<Team>();
    public ICollection<CallCenter> CallCenters { get; set; } = new List<CallCenter>();
}

/// <summary>
/// A call center is an operational unit within an <see cref="Agency"/>. It is the finer
/// data-isolation boundary: customer-pipeline data (leads, sales, applications, calls,
/// commissions) belongs to exactly one call center, and call-center-scoped users only see
/// their own. It is agency-scoped itself (a <see cref="TenantEntity"/>), so agency admins
/// manage only their agency's call centers.
/// </summary>
public class CallCenter : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;

    // ---- Site details -------------------------------------------------------
    // A BPO runs several physical sites; knowing where one is, who to ring and what hours it keeps
    // is basic operational information that had nowhere to live.
    /// <summary>Site contact number.</summary>
    public string? Phone { get; set; }
    /// <summary>Street address of the site.</summary>
    public string? Address { get; set; }
    /// <summary>City the site operates from — the label people actually use for a centre.</summary>
    public string? City { get; set; }
    /// <summary>
    /// IANA zone (e.g. "Asia/Karachi") the site works in. Sites in different countries keep
    /// different hours, so attendance and shift reporting need this rather than assuming one clock.
    /// </summary>
    public string? TimeZone { get; set; }
    /// <summary>Number of agent seats, for capacity planning. Null = not recorded.</summary>
    public int? SeatCapacity { get; set; }
}

public class Team : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Vertical { get; set; }
    public Guid? TeamLeadUserId { get; set; }
}

/// <summary>
/// A platform-level source-IP allowlist entry. Enforced globally by
/// <c>IpAllowlistMiddleware</c> for every tenant, so it is deliberately NOT a
/// <see cref="TenantEntity"/> (no agency FK, no tenant query filter) and is managed by
/// SuperAdmin only. <see cref="AgencyId"/> is optional provenance — which SuperAdmin/agency
/// added it — and never scopes enforcement.
/// </summary>
public class IpAllowlistEntry : BaseEntity
{
    public Guid? AgencyId { get; set; }
    public string CidrOrIp { get; set; } = string.Empty;
    public string? Note { get; set; }
}
