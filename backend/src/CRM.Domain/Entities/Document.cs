using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// A shared Word document or spreadsheet uploaded for staff to read in a protected
/// viewer. The raw bytes live in file storage (StorageKey is opaque); the binary is
/// only ever streamed through an authorized endpoint, never served statically.
/// </summary>
public class Document : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>"word" | "spreadsheet" | "other" — drives which client renderer is used.</summary>
    public string Kind { get; set; } = "other";

    public Guid UploadedByUserId { get; set; }
}

/// <summary>
/// A note a user writes against a document. This is how "they may write in it"
/// is satisfied without exposing the source file for copy/extraction — users
/// annotate alongside the protected viewer instead of editing the binary.
/// </summary>
public class DocumentNote : TenantEntity
{
    public Guid DocumentId { get; set; }
    public Guid UserId { get; set; }
    public string Body { get; set; } = string.Empty;
}
