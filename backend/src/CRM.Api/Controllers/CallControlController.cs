using CRM.Api.Authorization;
using CRM.Application.CallCenter;
using CRM.Application.Common.Authorization;
using CRM.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[HasPermission(Permissions.AgentPanelUse)]
[Route("api/cc/calls")]
public class CallControlController : ControllerBase
{
    private readonly IMediator _mediator;
    public CallControlController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    [HttpGet("active")]
    public async Task<IActionResult> Active(CancellationToken ct)
    {
        var c = await _mediator.Send(new GetMyActiveCallQuery(), ct);
        return c is null ? NoContent() : Ok(c);
    }

    public record DialBody(Guid LeadId);
    [HttpPost("dial")]
    public async Task<IActionResult> Dial([FromBody] DialBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new StartOutboundCallCommand(body.LeadId), ct));
    }

    [HttpPost("{id:guid}/answer")]
    public async Task<IActionResult> Answer(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new AnswerCallCommand(id), ct));

    [HttpPost("{id:guid}/hangup")]
    public async Task<IActionResult> Hangup(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new HangupCallCommand(id), ct));

    public record HoldBody(bool Hold);
    [HttpPost("{id:guid}/hold")]
    public async Task<IActionResult> Hold(Guid id, [FromBody] HoldBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new ToggleHoldCommand(id, body.Hold), ct));
    }

    public record MuteBody(bool Mute);
    [HttpPost("{id:guid}/mute")]
    public async Task<IActionResult> Mute(Guid id, [FromBody] MuteBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new ToggleMuteCommand(id, body.Mute), ct));
    }

    public record DtmfBody(string Digits);
    [HttpPost("{id:guid}/dtmf")]
    public async Task<IActionResult> Dtmf(Guid id, [FromBody] DtmfBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        await _mediator.Send(new SendDtmfCommand(id, body.Digits), ct);
        return NoContent();
    }

    public record SmsBody(Guid LeadId, string Body);
    [HttpPost("sms")]
    public async Task<IActionResult> SendSms([FromBody] SmsBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        await _mediator.Send(new SendQuickSmsCommand(body.LeadId, body.Body), ct);
        return NoContent();
    }
}
