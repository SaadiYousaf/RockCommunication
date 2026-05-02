using CRM.Api.Authorization;
using CRM.Application.Common.Authorization;
using CRM.Application.Knowledge;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/kb")]
public class KnowledgeController : ControllerBase
{
    private readonly IMediator _mediator;
    public KnowledgeController(IMediator mediator) => _mediator = mediator;

    [HttpGet("articles")]
    [HasPermission(Permissions.KnowledgeView)]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] string? category,
        [FromQuery] bool publishedOnly = true, [FromQuery] int take = 20, CancellationToken ct = default)
        => Ok(await _mediator.Send(new SearchKbQuery(q, category, publishedOnly, take), ct));

    [HttpGet("articles/{slug}")]
    [HasPermission(Permissions.KnowledgeView)]
    public async Task<IActionResult> Get(string slug, CancellationToken ct)
    {
        var a = await _mediator.Send(new GetKbArticleQuery(slug), ct);
        return a is null ? NotFound() : Ok(a);
    }

    [HttpPut("articles")]
    [HasPermission(Permissions.KnowledgeWrite)]
    public async Task<IActionResult> Upsert([FromBody] UpsertKbArticleCommand cmd, CancellationToken ct)
        => Ok(await _mediator.Send(cmd, ct));
}
