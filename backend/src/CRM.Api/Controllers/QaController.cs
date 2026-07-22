using CRM.Api.Authorization;
using CRM.Application.Common.Authorization;
using CRM.Application.QA;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/qa")]
public class QaController : ControllerBase
{
    private readonly IMediator _mediator;
    public QaController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet("rubrics")]
    [HasPermission(Permissions.QaView)]
    public async Task<ActionResult<IReadOnlyList<RubricDto>>> ListRubrics(CancellationToken ct)
        => Ok(await _mediator.Send(new ListRubricsQuery(), ct));

    [HttpPost("rubrics")]
    [HasPermission(Permissions.QaWrite)]
    public async Task<ActionResult<RubricDto>> CreateRubric([FromBody] CreateRubricDto dto, CancellationToken ct)
    {
        Guard.AgainstNull(dto);
        return Ok(await _mediator.Send(new CreateRubricCommand(dto), ct));
    }

    [HttpPost("reviews")]
    [HasPermission(Permissions.QaSubmit)]
    public async Task<ActionResult<ReviewDto>> Submit([FromBody] SubmitReviewDto dto, CancellationToken ct)
    {
        Guard.AgainstNull(dto);
        return Ok(await _mediator.Send(new SubmitReviewCommand(dto), ct));
    }

    [HttpGet("reviews")]
    [HasPermission(Permissions.QaView)]
    public async Task<ActionResult<IReadOnlyList<QaReviewSummaryDto>>> ListReviews(
        [FromQuery] Guid? agentUserId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListQaReviewsQuery(agentUserId, from, to, take), ct));

    [HttpGet("scorecards")]
    [HasPermission(Permissions.QaView)]
    public async Task<ActionResult<IReadOnlyList<AgentScorecardDto>>> Scorecards(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
    {
        var f = from ?? DateTime.UtcNow.AddDays(-30);
        var t = to ?? DateTime.UtcNow.AddDays(1);
        return Ok(await _mediator.Send(new AgentScorecardQuery(f, t), ct));
    }
}
