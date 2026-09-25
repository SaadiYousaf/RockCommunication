using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace CRM.Application.Academy;

// ── DTOs ────────────────────────────────────────────────────────────────────

/// <summary>A course as it appears in the learner's path, with their own progress folded in.</summary>
public record AcademyCourseDto(
    Guid Id, string Key, string Title, string Summary, string Icon,
    string Level, int Order,
    int LessonCount, int CompletedLessons, int EstimatedMinutes,
    bool IsComplete, string? CertificateSerial);

/// <summary>A lesson in a course listing — enough to choose what to read next.</summary>
public record AcademyLessonSummaryDto(
    Guid Id, string Key, string Title, string Summary, int Order,
    int EstimatedMinutes, string? Route, string Status, bool HasQuiz);

/// <summary>A question as the LEARNER sees it — deliberately without the answer key.</summary>
public record AcademyQuestionDto(Guid Id, int Order, string Prompt, IReadOnlyList<string> Options);

/// <summary>The full lesson: body, its quiz, and where it sits in the course.</summary>
public record AcademyLessonDto(
    Guid Id, string Key, string Title, string Summary, string BodyMarkdown,
    int EstimatedMinutes, string? Route, string Status,
    Guid CourseId, string CourseKey, string CourseTitle,
    IReadOnlyList<AcademyQuestionDto> Questions,
    string? PreviousLessonKey, string? NextLessonKey);

/// <summary>One graded answer, returned after submitting — right or wrong, always with the why.</summary>
public record QuizResultItemDto(Guid QuestionId, bool Correct, int CorrectIndex, string Explanation);

/// <summary>The outcome of a quiz attempt, plus anything it unlocked.</summary>
public record QuizResultDto(
    int Score, int Total, bool Passed,
    IReadOnlyList<QuizResultItemDto> Items,
    bool CourseCompleted, string? CertificateSerial);

/// <summary>The learner's own headline numbers for the Academy dashboard.</summary>
public record AcademyProgressDto(
    int CoursesAvailable, int CoursesCompleted,
    int LessonsAvailable, int LessonsCompleted,
    int MinutesSpent, int CertificatesEarned,
    string? NextLessonCourseKey, string? NextLessonKey, string? NextLessonTitle);

/// <summary>A certificate, as shown on the wall and as verified by serial.</summary>
public record AcademyCertificateDto(
    string SerialNumber, string LearnerName, string CourseTitle, DateTime IssuedAt);

// ── Requests ────────────────────────────────────────────────────────────────

/// <summary>Courses this user should see, ordered into a path, with their progress.</summary>
public record ListAcademyCoursesQuery : IRequest<IReadOnlyList<AcademyCourseDto>>;

/// <summary>The lessons of one course, with this learner's status on each.</summary>
public record GetAcademyCourseQuery(string CourseKey) : IRequest<IReadOnlyList<AcademyLessonSummaryDto>>;

/// <summary>One lesson to read, including its quiz (without answers).</summary>
public record GetAcademyLessonQuery(string CourseKey, string LessonKey) : IRequest<AcademyLessonDto>;

/// <summary>What finishing a lesson did — including whether it was the one that finished the course.</summary>
public record LessonCompletedDto(bool CourseCompleted, string? CertificateSerial);

/// <summary>Record that a lesson was read. Idempotent — re-reading never un-completes it.</summary>
public record CompleteLessonCommand(Guid LessonId, int SecondsSpent) : IRequest<LessonCompletedDto>;

/// <summary>Grade a quiz attempt server-side and return what was right, wrong, and why.</summary>
public record SubmitQuizCommand(Guid LessonId, IReadOnlyList<int> Answers) : IRequest<QuizResultDto>;

/// <summary>The learner's own dashboard numbers.</summary>
public record MyAcademyProgressQuery : IRequest<AcademyProgressDto>;

/// <summary>Certificates this learner has earned.</summary>
public record MyCertificatesQuery : IRequest<IReadOnlyList<AcademyCertificateDto>>;

/// <summary>Look a certificate up by its printed serial, to confirm it is genuine.</summary>
public record VerifyCertificateQuery(string SerialNumber) : IRequest<AcademyCertificateDto>;

public class SubmitQuizValidator : AbstractValidator<SubmitQuizCommand>
{
    public SubmitQuizValidator()
    {
        RuleFor(x => x.LessonId).NotEmpty();
        RuleFor(x => x.Answers).NotNull()
            .Must(a => a.Count <= 50).WithMessage("That's more answers than the quiz has questions.");
    }
}

public class CompleteLessonValidator : AbstractValidator<CompleteLessonCommand>
{
    public CompleteLessonValidator()
    {
        RuleFor(x => x.LessonId).NotEmpty();
        // Clamped rather than rejected below; this only stops an absurd value reaching the sum.
        RuleFor(x => x.SecondsSpent).InclusiveBetween(0, 24 * 60 * 60);
    }
}

// ── Handler ─────────────────────────────────────────────────────────────────

/// <summary>
/// The Learning Academy.
///
/// Two things are kept strictly apart. The CURRICULUM (courses, lessons, questions) is seeded with
/// the application and is the same for every agency — it describes the product itself. PROGRESS is
/// per learner and tenant-scoped, so an agency only ever sees its own people.
///
/// Grading happens here, never in the browser: the answer key is never serialised to the client, so
/// it cannot be read out of the network response.
/// </summary>
public class AcademyHandlers :
    IRequestHandler<ListAcademyCoursesQuery, IReadOnlyList<AcademyCourseDto>>,
    IRequestHandler<GetAcademyCourseQuery, IReadOnlyList<AcademyLessonSummaryDto>>,
    IRequestHandler<GetAcademyLessonQuery, AcademyLessonDto>,
    IRequestHandler<CompleteLessonCommand, LessonCompletedDto>,
    IRequestHandler<SubmitQuizCommand, QuizResultDto>,
    IRequestHandler<MyAcademyProgressQuery, AcademyProgressDto>,
    IRequestHandler<MyCertificatesQuery, IReadOnlyList<AcademyCertificateDto>>,
    IRequestHandler<VerifyCertificateQuery, AcademyCertificateDto>
{
    /// <summary>Share of questions that must be right to pass. Two-thirds — enough to show it landed.</summary>
    private const double PassMark = 0.66;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public AcademyHandlers(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    private Guid Me => _user.UserId ?? throw new ForbiddenAccessException();

    /// <summary>
    /// Is this course meant for me? Empty audience means everyone; otherwise any one of my roles
    /// matching is enough. This is what keeps a Fronter's path about fronting instead of showing
    /// them payroll and carrier rules.
    /// </summary>
    private bool IsForMe(AcademyCourse c)
    {
        if (string.IsNullOrWhiteSpace(c.AudienceRolesCsv)) return true;
        var audience = c.AudienceRolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return audience.Any(r => _user.Roles.Contains(r, StringComparer.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<AcademyCourseDto>> Handle(ListAcademyCoursesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = Me;

        var courses = await _db.AcademyCourses.AsNoTracking()
            .Where(c => c.IsPublished)
            .Include(c => c.Lessons.Where(l => l.IsPublished))
            .ToListAsync(ct);

        var mine = courses.Where(IsForMe).ToList();
        var lessonIds = mine.SelectMany(c => c.Lessons).Select(l => l.Id).ToList();

        var done = await _db.AcademyLessonProgress.AsNoTracking()
            .Where(p => p.UserId == me && p.Status == LessonStatus.Completed && lessonIds.Contains(p.LessonId))
            .Select(p => p.LessonId)
            .ToListAsync(ct);
        var doneSet = done.ToHashSet();

        var certs = (await _db.AcademyCertificates.AsNoTracking()
                .Where(x => x.UserId == me)
                .Select(x => new { x.CourseId, x.SerialNumber })
                .ToListAsync(ct))
            .ToDictionary(x => x.CourseId, x => x.SerialNumber);

        return mine
            .OrderBy(c => c.Level).ThenBy(c => c.Order).ThenBy(c => c.Title)
            .Select(c =>
            {
                var lessons = c.Lessons.ToList();
                var completed = lessons.Count(l => doneSet.Contains(l.Id));
                return new AcademyCourseDto(
                    c.Id, c.Key, c.Title, c.Summary, c.Icon,
                    c.Level.ToString(), c.Order,
                    lessons.Count, completed,
                    lessons.Sum(l => l.EstimatedMinutes),
                    lessons.Count > 0 && completed == lessons.Count,
                    certs.GetValueOrDefault(c.Id));
            })
            .ToList();
    }

    public async Task<IReadOnlyList<AcademyLessonSummaryDto>> Handle(GetAcademyCourseQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = Me;
        var course = await LoadCourseAsync(request.CourseKey, ct);

        var lessonIds = course.Lessons.Select(l => l.Id).ToList();
        var statuses = (await _db.AcademyLessonProgress.AsNoTracking()
                .Where(p => p.UserId == me && lessonIds.Contains(p.LessonId))
                .Select(p => new { p.LessonId, p.Status })
                .ToListAsync(ct))
            .ToDictionary(p => p.LessonId, p => p.Status);

        var withQuiz = (await _db.AcademyQuizQuestions.AsNoTracking()
                .Where(q => lessonIds.Contains(q.LessonId))
                .Select(q => q.LessonId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        return course.Lessons
            .OrderBy(l => l.Order)
            .Select(l => new AcademyLessonSummaryDto(
                l.Id, l.Key, l.Title, l.Summary, l.Order, l.EstimatedMinutes, l.Route,
                statuses.GetValueOrDefault(l.Id, LessonStatus.NotStarted).ToString(),
                withQuiz.Contains(l.Id)))
            .ToList();
    }

    public async Task<AcademyLessonDto> Handle(GetAcademyLessonQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = Me;
        var course = await LoadCourseAsync(request.CourseKey, ct);

        var ordered = course.Lessons.OrderBy(l => l.Order).ToList();
        var index = ordered.FindIndex(l => l.Key == request.LessonKey);
        if (index < 0) throw new NotFoundException(nameof(AcademyLesson), request.LessonKey);
        var lesson = ordered[index];

        var questions = await _db.AcademyQuizQuestions.AsNoTracking()
            .Where(q => q.LessonId == lesson.Id)
            .OrderBy(q => q.Order)
            // Projecting the fields the learner may see. CorrectIndex is deliberately absent —
            // sending it would put the answer key in the browser's network tab.
            .Select(q => new { q.Id, q.Order, q.Prompt, q.OptionsJson })
            .ToListAsync(ct);

        var status = await _db.AcademyLessonProgress.AsNoTracking()
            .Where(p => p.UserId == me && p.LessonId == lesson.Id)
            .Select(p => (LessonStatus?)p.Status)
            .FirstOrDefaultAsync(ct) ?? LessonStatus.NotStarted;

        return new AcademyLessonDto(
            lesson.Id, lesson.Key, lesson.Title, lesson.Summary, lesson.BodyMarkdown,
            lesson.EstimatedMinutes, lesson.Route, status.ToString(),
            course.Id, course.Key, course.Title,
            questions.Select(q => new AcademyQuestionDto(q.Id, q.Order, q.Prompt, ParseOptions(q.OptionsJson))).ToList(),
            index > 0 ? ordered[index - 1].Key : null,
            index < ordered.Count - 1 ? ordered[index + 1].Key : null);
    }

    public async Task<LessonCompletedDto> Handle(CompleteLessonCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = Me;

        var lesson = await _db.AcademyLessons.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.LessonId, ct)
            ?? throw new NotFoundException(nameof(AcademyLesson), request.LessonId);

        await UpsertProgressAsync(me, lesson.Id, LessonStatus.Completed, request.SecondsSpent, ct);
        await _db.SaveChangesAsync(ct);

        // Reading every lesson finishes the course, quiz or no quiz. Issuing only on a passed quiz
        // would mean a learner who worked through an entire course earned nothing for it — and not
        // every lesson has a quiz to pass in the first place.
        var (completed, serial) = await TryIssueCertificateAsync(me, lesson.CourseId, ct);
        return new LessonCompletedDto(completed, serial);
    }

    public async Task<QuizResultDto> Handle(SubmitQuizCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = Me;

        var lesson = await _db.AcademyLessons.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.LessonId, ct)
            ?? throw new NotFoundException(nameof(AcademyLesson), request.LessonId);

        var questions = await _db.AcademyQuizQuestions.AsNoTracking()
            .Where(q => q.LessonId == lesson.Id)
            .OrderBy(q => q.Order)
            .ToListAsync(ct);
        if (questions.Count == 0) throw new ConflictException("This lesson doesn't have a quiz.");

        // Grade here, on the server, against the stored key.
        var items = new List<QuizResultItemDto>(questions.Count);
        var score = 0;
        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var given = i < request.Answers.Count ? request.Answers[i] : -1;
            var correct = given == q.CorrectIndex;
            if (correct) score++;
            items.Add(new QuizResultItemDto(q.Id, correct, q.CorrectIndex, q.Explanation));
        }

        var passed = score >= (int)Math.Ceiling(questions.Count * PassMark);

        _db.AcademyQuizAttempts.Add(new AcademyQuizAttempt
        {
            AgencyId = _user.AgencyId ?? Guid.Empty,
            UserId = me,
            LessonId = lesson.Id,
            Score = score,
            Total = questions.Count,
            PassedAt = passed ? DateTime.UtcNow : null,
        });

        // Passing marks the lesson done — answering correctly IS the evidence it was understood, and
        // it saves the learner a second click that adds nothing.
        if (passed) await UpsertProgressAsync(me, lesson.Id, LessonStatus.Completed, 0, ct);
        await _db.SaveChangesAsync(ct);

        // Asked regardless of the result: the certificate is for finishing the course, so a learner
        // who already finished it shouldn't be told otherwise just because this attempt fell short.
        var (courseCompleted, serial) = await TryIssueCertificateAsync(me, lesson.CourseId, ct);

        return new QuizResultDto(score, questions.Count, passed, items, courseCompleted, serial);
    }

    public async Task<AcademyProgressDto> Handle(MyAcademyProgressQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = Me;

        var courses = await _db.AcademyCourses.AsNoTracking()
            .Where(c => c.IsPublished)
            .Include(c => c.Lessons.Where(l => l.IsPublished))
            .ToListAsync(ct);
        var mine = courses.Where(IsForMe).ToList();

        var lessons = mine.SelectMany(c => c.Lessons).ToList();
        var lessonIds = lessons.Select(l => l.Id).ToList();

        var progress = await _db.AcademyLessonProgress.AsNoTracking()
            .Where(p => p.UserId == me && lessonIds.Contains(p.LessonId))
            .Select(p => new { p.LessonId, p.Status, p.SecondsSpent })
            .ToListAsync(ct);

        var doneSet = progress.Where(p => p.Status == LessonStatus.Completed).Select(p => p.LessonId).ToHashSet();
        var certs = await _db.AcademyCertificates.AsNoTracking().CountAsync(x => x.UserId == me, ct);

        // "Carry on where you left off" — the first unread lesson in path order, which is the one
        // question a returning learner actually has.
        var next = mine
            .OrderBy(c => c.Level).ThenBy(c => c.Order)
            .SelectMany(c => c.Lessons.OrderBy(l => l.Order).Select(l => new { Course = c, Lesson = l }))
            .FirstOrDefault(x => !doneSet.Contains(x.Lesson.Id));

        return new AcademyProgressDto(
            mine.Count,
            mine.Count(c => c.Lessons.Count > 0 && c.Lessons.All(l => doneSet.Contains(l.Id))),
            lessons.Count,
            doneSet.Count,
            progress.Sum(p => p.SecondsSpent) / 60,
            certs,
            next?.Course.Key, next?.Lesson.Key, next?.Lesson.Title);
    }

    public async Task<IReadOnlyList<AcademyCertificateDto>> Handle(MyCertificatesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = Me;
        return await _db.AcademyCertificates.AsNoTracking()
            .Where(x => x.UserId == me)
            .OrderByDescending(x => x.IssuedAt)
            .Select(x => new AcademyCertificateDto(x.SerialNumber, x.LearnerName, x.CourseTitle, x.IssuedAt))
            .ToListAsync(ct);
    }

    public async Task<AcademyCertificateDto> Handle(VerifyCertificateQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var serial = (request.SerialNumber ?? "").Trim().ToUpperInvariant();

        // IgnoreQueryFilters: verification is the one read that must work across tenants — a manager
        // checking a certificate may not be in the agency that issued it. Only the three fields
        // printed on the certificate are returned, so this cannot be used to enumerate learners.
        var cert = await _db.AcademyCertificates.AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.SerialNumber == serial && !x.IsDeleted)
            .Select(x => new AcademyCertificateDto(x.SerialNumber, x.LearnerName, x.CourseTitle, x.IssuedAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Certificate", serial);

        return cert;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<AcademyCourse> LoadCourseAsync(string key, CancellationToken ct)
    {
        var course = await _db.AcademyCourses.AsNoTracking()
            .Where(c => c.Key == key && c.IsPublished)
            .Include(c => c.Lessons.Where(l => l.IsPublished))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(AcademyCourse), key);

        // A course aimed at other roles is not yours to read — reported as missing rather than
        // forbidden, so the response does not confirm what other paths exist.
        if (!IsForMe(course)) throw new NotFoundException(nameof(AcademyCourse), key);
        return course;
    }

    /// <summary>
    /// One progress row per learner per lesson, created on first touch. Completion is sticky: once
    /// done it stays done, so revisiting a lesson to look something up never costs the learner credit.
    /// </summary>
    private async Task UpsertProgressAsync(Guid userId, Guid lessonId, LessonStatus status, int seconds, CancellationToken ct)
    {
        var row = await _db.AcademyLessonProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId, ct);

        if (row is null)
        {
            row = new AcademyLessonProgress
            {
                AgencyId = _user.AgencyId ?? Guid.Empty,
                UserId = userId,
                LessonId = lessonId,
            };
            _db.AcademyLessonProgress.Add(row);
        }

        if (status == LessonStatus.Completed && row.Status != LessonStatus.Completed)
        {
            row.Status = LessonStatus.Completed;
            row.CompletedAt = DateTime.UtcNow;
        }
        else if (row.Status == LessonStatus.NotStarted)
        {
            row.Status = status;
        }

        row.SecondsSpent += Math.Clamp(seconds, 0, 24 * 60 * 60);
        row.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Issue a certificate if this was the last lesson of the course. Idempotent — the unique index
    /// on (UserId, CourseId) is the real guarantee, and re-finishing returns the existing serial
    /// rather than minting a second one.
    /// </summary>
    private async Task<(bool Completed, string? Serial)> TryIssueCertificateAsync(Guid userId, Guid courseId, CancellationToken ct)
    {
        var course = await _db.AcademyCourses.AsNoTracking()
            .Include(c => c.Lessons.Where(l => l.IsPublished))
            .FirstOrDefaultAsync(c => c.Id == courseId, ct);
        if (course is null || course.Lessons.Count == 0) return (false, null);

        var lessonIds = course.Lessons.Select(l => l.Id).ToList();
        var completed = await _db.AcademyLessonProgress.AsNoTracking()
            .CountAsync(p => p.UserId == userId && p.Status == LessonStatus.Completed
                          && lessonIds.Contains(p.LessonId), ct);
        if (completed < lessonIds.Count) return (false, null);

        var existing = await _db.AcademyCertificates.AsNoTracking()
            .Where(x => x.UserId == userId && x.CourseId == courseId)
            .Select(x => x.SerialNumber)
            .FirstOrDefaultAsync(ct);
        if (existing is not null) return (true, existing);

        var cert = new AcademyCertificate
        {
            AgencyId = _user.AgencyId ?? Guid.Empty,
            UserId = userId,
            CourseId = courseId,
            SerialNumber = NewSerial(),
            LearnerName = _user.UserName ?? "Learner",
            CourseTitle = course.Title,
        };
        _db.AcademyCertificates.Add(cert);
        await _db.SaveChangesAsync(ct);
        return (true, cert.SerialNumber);
    }

    /// <summary>
    /// A short, human-quotable serial like RC-4F2A-9C11. Random rather than sequential, so a serial
    /// reveals nothing about how many have been issued or to whom.
    /// </summary>
    private static string NewSerial()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var hex = Convert.ToHexString(bytes);
        return $"SMH-{hex[..4]}-{hex[4..]}";
    }

    private static IReadOnlyList<string> ParseOptions(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch { return new List<string>(); }   // malformed seed data must not break the lesson
    }
}
