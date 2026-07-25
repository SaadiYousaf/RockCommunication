using CRM.Api.Authorization;
using CRM.Application.Ai;
using CRM.Application.Common.Authorization;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ai")]
public class AiController : ControllerBase
{
    private readonly IMediator _mediator;
    public AiController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    // Governed by permission (these trigger paid LLM calls) — was bare [Authorize].
    [HttpPost("calls/{id:guid}/summary")]
    [HasPermission(Permissions.AgentPanelUse)]
    public async Task<IActionResult> SummarizeCall(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new SummarizeCallCommand(id), ct));

    [HttpGet("leads/{id:guid}/score")]
    [HasPermission(Permissions.LeadsRead)]
    public async Task<IActionResult> AiScoreLead(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new AiScoreLeadQuery(id), ct));

    [HttpGet("leads/{id:guid}/recommendations")]
    [HasPermission(Permissions.LeadsRead)]
    public async Task<IActionResult> Recommendations(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new RecommendForLeadQuery(id), ct));
}
