using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>How demanding a course is, used to order a learner's path.</summary>
public enum AcademyLevel
{
    Beginner = 0,
    Intermediate = 10,
    Advanced = 20,
    Admin = 30,
}

/// <summary>Where a learner has got to with one lesson.</summary>
public enum LessonStatus
{
    NotStarted = 0,
    InProgress = 10,
    Completed = 20,
}

/// <summary>
/// A course in the Learning Academy — a set of lessons about one area of the product.
///
/// BaseEntity, NOT TenantEntity, on purpose: the curriculum describes the product itself, so it is
/// identical for every agency and is seeded with the application rather than authored per tenant.
/// Only a learner's PROGRESS is tenant-scoped.
/// </summary>
public class AcademyCourse : BaseEntity
{
    /// <summary>Stable slug (e.g. "leads-basics"). The seeder upserts on this, so re-running never duplicates.</summary>
    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;

    /// <summary>Icon name from the shared UI kit, so courses look like the rest of the app.</summary>
    public string Icon { get; set; } = "book";

    public AcademyLevel Level { get; set; } = AcademyLevel.Beginner;

    /// <summary>Display order within a level.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Comma-separated role names this course is FOR. Empty means everyone.
    ///
    /// This is what makes a learner's path relevant instead of a wall of every module: a Fronter is
    /// shown fronting and lead capture, not payroll or carrier rules. Stored as CSV rather than a
    /// join table because it is seeded, read-only reference data that is only ever read as a whole.
    /// </summary>
    public string AudienceRolesCsv { get; set; } = string.Empty;

    public bool IsPublished { get; set; } = true;

    public ICollection<AcademyLesson> Lessons { get; set; } = new List<AcademyLesson>();
}

/// <summary>One lesson. Seeded with the product; see <see cref="AcademyCourse"/> for why.</summary>
public class AcademyLesson : BaseEntity
{
    public Guid CourseId { get; set; }

    /// <summary>Stable slug, unique within the course.</summary>
    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int Order { get; set; }

    /// <summary>Roughly how long this takes, so a learner can decide whether to start it now.</summary>
    public int EstimatedMinutes { get; set; } = 5;

    /// <summary>
    /// The route in the CRM this lesson teaches, e.g. "/queue". Lets the lesson offer "Open the real
    /// screen" — learning next to the thing itself rather than a screenshot that goes stale.
    /// Null for conceptual lessons with no single screen.
    /// </summary>
    public string? Route { get; set; }

    /// <summary>
    /// Lesson body in a small, deliberately limited markdown subset (headings, paragraphs, lists,
    /// bold, inline code, callouts). Rendered by our own renderer — no new dependency, and no raw
    /// HTML, so seeded content can never inject markup.
    /// </summary>
    public string BodyMarkdown { get; set; } = string.Empty;

    public bool IsPublished { get; set; } = true;

    public AcademyCourse? Course { get; set; }
    public ICollection<AcademyQuizQuestion> Questions { get; set; } = new List<AcademyQuizQuestion>();
}

/// <summary>A check-your-understanding question at the end of a lesson.</summary>
public class AcademyQuizQuestion : BaseEntity
{
    public Guid LessonId { get; set; }
    public int Order { get; set; }

    public string Prompt { get; set; } = string.Empty;

    /// <summary>The answer choices, JSON string array. Two entries makes it a true/false in practice.</summary>
    public string OptionsJson { get; set; } = "[]";

    /// <summary>
    /// Index into Options. Deliberately NEVER serialised to the learner — the grade comes from the
    /// server, so the answer key cannot be read out of the network response.
    /// </summary>
    public int CorrectIndex { get; set; }

    /// <summary>Shown after answering, right or wrong — the teaching moment.</summary>
    public string Explanation { get; set; } = string.Empty;

    public AcademyLesson? Lesson { get; set; }
}

/// <summary>
/// One learner's progress through one lesson.
///
/// TenantEntity: progress belongs to a person inside an agency, so an agency's managers can see
/// their own team's progress and nobody else's — the global tenant filter enforces that for free.
/// </summary>
public class AcademyLessonProgress : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid LessonId { get; set; }

    public LessonStatus Status { get; set; } = LessonStatus.NotStarted;

    /// <summary>Set once, the first time it is completed — so a re-read never resets the record.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Accumulated reading time, for "time spent learning".</summary>
    public int SecondsSpent { get; set; }
}

/// <summary>A graded quiz attempt. Kept per attempt so a learner can see improvement.</summary>
public class AcademyQuizAttempt : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid LessonId { get; set; }

    public int Score { get; set; }
    public int Total { get; set; }

    /// <summary>Set when the attempt reached the pass mark.</summary>
    public DateTime? PassedAt { get; set; }
}

/// <summary>
/// Proof a learner finished a course. One row per user per course, created once.
/// </summary>
public class AcademyCertificate : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Short human-quotable code printed on the certificate (e.g. "RC-4F2A-9C11"). Verification looks
    /// this up, so it is unique and never reused. Not derived from the user or course id, so the code
    /// alone leaks nothing about who holds it.
    /// </summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>Denormalised at issue time so a certificate still reads correctly after a rename.</summary>
    public string LearnerName { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
}
