using CRM.Api.Authorization;
using CRM.Application.Chat;
using CRM.Application.Common.Authorization;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// SuperAdmin oversight — read-only visibility into every agency's chat rooms and
/// transcripts. Gated to the AgenciesManage permission (SuperAdmin-only in the seed)
/// AND re-checked as SuperAdmin inside the handlers, since it exposes cross-tenant PII.
/// </summary>
[ApiController]
[Authorize]
[HasPermission(Permissions.AgenciesManage)]
[Route("api/admin/chat-oversight")]
public class ChatOversightController : ControllerBase
{
    private readonly IMediator _mediator;
    public ChatOversightController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    // Drill-down level 1: every agency + its chat-room count.
    [HttpGet("agencies")]
    public async Task<IActionResult> Agencies(CancellationToken ct)
        => Ok(await _mediator.Send(new ListOversightAgenciesQuery(), ct));

    // Drill-down level 2: the call centers of one agency (+ an "Agency-wide" bucket) with room counts.
    [HttpGet("agencies/{agencyId:guid}/call-centers")]
    public async Task<IActionResult> CallCenters(Guid agencyId, CancellationToken ct)
        => Ok(await _mediator.Send(new ListOversightCallCentersQuery(agencyId), ct));

    // Drill-down level 3: rooms, optionally scoped to an agency and a call center
    // (callCenterId = 00000000-… means the agency-wide bucket).
    [HttpGet("rooms")]
    public async Task<IActionResult> Rooms([FromQuery] Guid? agencyId, [FromQuery] Guid? callCenterId, CancellationToken ct)
        => Ok(await _mediator.Send(new ListAllChatRoomsQuery(agencyId, callCenterId), ct));

    [HttpGet("rooms/{roomId:guid}/messages")]
    public async Task<IActionResult> Messages(Guid roomId, CancellationToken ct)
        => Ok(await _mediator.Send(new GetChatRoomTranscriptQuery(roomId), ct));
}
