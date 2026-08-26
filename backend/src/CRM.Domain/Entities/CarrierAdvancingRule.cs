using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// How a carrier advances commission: the rate it pays and how many months it advances up front.
/// Managed by the Commission Agent and keyed by carrier NAME (carriers are free-text on
/// <see cref="Sale.Carrier"/>, so there is no Carrier entity to reference). Deliberately GLOBAL —
/// a <see cref="BaseEntity"/>, not a tenant entity — because one carrier advances the same way for
/// every agency, and the Commission Agent works across all of them. The sales list joins this in
/// read-only; it is only edited from the Carrier Advancing Rules screen.
/// </summary>
public class CarrierAdvancingRule : BaseEntity
{
    /// <summary>Carrier name, matched case-insensitively against <see cref="Sale.Carrier"/>. Unique.</summary>
    public string Carrier { get; set; } = string.Empty;

    /// <summary>Commission rate the carrier pays, as a percentage (e.g. 80 = 80%).</summary>
    public decimal CommissionRate { get; set; }

    /// <summary>How many months of premium the carrier advances up front (e.g. 6).</summary>
    public int AdvancedMonths { get; set; }

    public string? Notes { get; set; }

    /// <summary>Inactive rules stop being applied to the sales list without losing the history.</summary>
    public bool IsActive { get; set; } = true;
}
