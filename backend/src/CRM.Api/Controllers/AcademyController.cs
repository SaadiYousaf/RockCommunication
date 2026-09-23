using CRM.Application.Academy;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// The Learning Academy.
///
/// Deliberately gated on [Authorize] alone and no permission: learning how to use the product is for
/// everyone who can sign in, and putting it behind a grant would mean the people most in need of it —
/// a new hire on their first morning — are the ones locked out. What each learner SEES is still
/// scoped: courses are filtered to their roles inside the handler, and progress is tenant-scoped.
/// </summary>
[ApiController]
[Authorize]
[Route("api/academy")]
public class AcademyController : ControllerBase
{
    private readonly IMediator _mediator;
    public AcademyController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    /// <summary>The courses in this learner's path, with their progress folded in.</summary>
    [HttpGet("courses")]
    public async Task<IActionResult> Courses(CancellationToken ct)
        => Ok(await _mediator.Send(new ListAcademyCoursesQuery(), ct));

    /// <summary>The lessons of one course.</summary>
    [HttpGet("courses/{courseKey}")]
    public async Task<IActionResult> Course(string courseKey, CancellationToken ct)
        => Ok(await _mediator.Send(new GetAcademyCourseQuery(courseKey), ct));

    /// <summary>One lesson, with its quiz. The answer key is never included.</summary>
    [HttpGet("courses/{courseKey}/lessons/{lessonKey}")]
    public async Task<IActionResult> Lesson(string courseKey, string lessonKey, CancellationToken ct)
        => Ok(await _mediator.Send(new GetAcademyLessonQuery(courseKey, lessonKey), ct));

    public record CompleteBody(int SecondsSpent);

    /// <summary>Mark a lesson read. Safe to call more than once.</summary>
    [HttpPost("lessons/{lessonId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid lessonId, [FromBody] CompleteBody? body, CancellationToken ct)
        => Ok(await _mediator.Send(new CompleteLessonCommand(lessonId, body?.SecondsSpent ?? 0), ct));

    public record QuizBody(IReadOnlyList<int> Answers);

    /// <summary>Submit a quiz. Graded on the server; returns what was right, wrong and why.</summary>
    [HttpPost("lessons/{lessonId:guid}/quiz")]
    public async Task<IActionResult> Quiz(Guid lessonId, [FromBody] QuizBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new SubmitQuizCommand(lessonId, body.Answers ?? Array.Empty<int>()), ct));
    }

    /// <summary>This learner's headline numbers.</summary>
    [HttpGet("progress")]
    public async Task<IActionResult> Progress(CancellationToken ct)
        => Ok(await _mediator.Send(new MyAcademyProgressQuery(), ct));

    /// <summary>Certificates this learner has earned.</summary>
    [HttpGet("certificates")]
    public async Task<IActionResult> Certificates(CancellationToken ct)
        => Ok(await _mediator.Send(new MyCertificatesQuery(), ct));

    /// <summary>Confirm a certificate is genuine from the serial printed on it.</summary>
    [HttpGet("certificates/{serial}")]
    public async Task<IActionResult> Verify(string serial, CancellationToken ct)
        => Ok(await _mediator.Send(new VerifyCertificateQuery(serial), ct));
}
