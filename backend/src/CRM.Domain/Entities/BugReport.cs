using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

/// <summary>
/// A bug/issue reported by any user from within the app. Agency-scoped (TenantEntity) and moved
/// through a status workflow by a triager (Admin/SuperAdmin). Captures the page the reporter was on
/// to aid reproduction.
/// </summary>
public class BugReport : TenantEntity
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public BugSeverity Severity { get; set; } = BugSeverity.Medium;
    public BugStatus Status { get; set; } = BugStatus.New;
    public Guid ReporterUserId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    /// <summary>The in-app route the reporter was on when they filed it.</summary>
    public string? PageUrl { get; set; }
    /// <summary>Best-effort browser/user-agent snapshot for reproduction context.</summary>
    public string? UserAgent { get; set; }
    /// <summary>The fix note / reason, set when the bug reaches a terminal status.</summary>
    public string? Resolution { get; set; }
}

/// <summary>One entry in a bug's activity trail — a status transition and/or a comment.</summary>
public class BugReportActivity : TenantEntity
{
    public Guid BugReportId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Set on a status transition (null for a plain comment).</summary>
    public BugStatus? FromStatus { get; set; }
    public BugStatus? ToStatus { get; set; }
    public string? Comment { get; set; }
}
