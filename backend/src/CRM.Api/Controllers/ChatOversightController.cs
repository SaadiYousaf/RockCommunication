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

    [HttpGet("rooms")]
    public async Task<IActionResult> Rooms([FromQuery] Guid? agencyId, CancellationToken ct)
        => Ok(await _mediator.Send(new ListAllChatRoomsQuery(agencyId), ct));

    [HttpGet("rooms/{roomId:guid}/messages")]
    public async Task<IActionResult> Messages(Guid roomId, CancellationToken ct)
        => Ok(await _mediator.Send(new GetChatRoomTranscriptQuery(roomId), ct));
}
