namespace CRM.Domain.Enums;

/// <summary>
/// Where a reported bug sits in its lifecycle. Persisted as ints, so values are stable.
/// The workflow: New → Triaged → InProgress → Resolved → Closed, with three off-ramps
/// (WontFix / Duplicate / CannotReproduce) that also close the report.
/// </summary>
public enum BugStatus
{
    New = 0,              // reported, awaiting triage
    Triaged = 1,          // acknowledged as a valid, reproducible issue
    InProgress = 2,       // actively being worked on
    Resolved = 3,         // a fix has been made
    Closed = 4,           // verified and archived
    WontFix = 5,          // valid, but intentionally not being fixed
    Duplicate = 6,        // already tracked by another report
    CannotReproduce = 7,  // could not be reproduced
}

/// <summary>How badly a bug affects users — drives triage priority.</summary>
public enum BugSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}
