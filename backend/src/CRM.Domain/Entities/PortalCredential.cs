using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// A confidential login for an external insurance / carrier portal, stored in the agency's
/// vault. The <see cref="Password"/> is encrypted at rest (EncryptedStringConverter, same
/// AES-GCM protection as SSN). Agency-scoped (TenantEntity) and reachable only by Admin /
/// SuperAdmin — see the ConfidentialController + handler EnsureAdmin gate.
/// </summary>
public class PortalCredential : TenantEntity
{
    /// <summary>Display name of the portal, e.g. "Mutual of Omaha — agent portal".</summary>
    public string PortalName { get; set; } = string.Empty;

    /// <summary>Optional login URL.</summary>
    public string? Url { get; set; }

    /// <summary>The portal username / login id.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>The portal password — encrypted at rest.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Free-text notes (MFA hints, which team uses it, etc.).</summary>
    public string? Notes { get; set; }

    /// <summary>Who last created / updated this entry (for a light audit trail).</summary>
    public Guid CreatedByUserId { get; set; }
}
