using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// A record that a one-shot platform operation has already been carried out.
///
/// Some operations must happen exactly once for the lifetime of a deployment — the go-live reset
/// being the obvious one, since running it twice would destroy real customer data. A config flag
/// alone cannot express that: flags stay switched on, deploys repeat, and services restart. The
/// marker lives in the database it protects, so the operation and the proof it ran cannot drift
/// apart.
///
/// Not a TenantEntity: this describes the installation, not any agency inside it.
/// </summary>
public class PlatformState : BaseEntity
{
    /// <summary>Stable identifier for the operation, e.g. "go-live-reset". Unique.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>When it ran.</summary>
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    /// <summary>What it did, in plain words, for whoever finds this row later.</summary>
    public string Detail { get; set; } = string.Empty;
}
