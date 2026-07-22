using CRM.Api.Authorization;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Integrations;
using CRM.Application.Leads.Commands;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/integrations")]
public class IntegrationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IDialerProvider _dialer;
    private readonly ICarrierRegistry _carriers;
    private readonly AppDbContext _db;

    public IntegrationsController(IMediator mediator, IDialerProvider dialer, ICarrierRegistry carriers, AppDbContext db)
    {
        _mediator = Guard.AgainstNull(mediator);
        _dialer = Guard.AgainstNull(dialer);
        _carriers = Guard.AgainstNull(carriers);
        _db = Guard.AgainstNull(db);
    }

    [HttpPost("jornaya/verify/{leadId:guid}")]
    public async Task<IActionResult> Verify(Guid leadId, CancellationToken ct)
        => Ok(await _mediator.Send(new VerifyJornayaCommand(leadId), ct));

    public record DialBody(Guid LeadId);

    [HttpPost("dialer/dial")]
    [HasPermission(Permissions.AgentPanelUse)]
    public async Task<IActionResult> Dial([FromBody] DialBody body, [FromServices] CRM.Application.Common.Interfaces.ICurrentUser user, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        Guard.AgainstNull(user);
        if (user.UserId is null || user.AgencyId is null) return Forbid();
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == body.LeadId && l.AgencyId == user.AgencyId, ct);
        if (lead is null) return NotFound();

        var result = await _dialer.DialAsync(user.UserId.Value, lead.PhoneNumber, lead.Id, ct);

        _db.CallRecords.Add(new CallRecord
        {
            AgencyId = lead.AgencyId,
            LeadId = lead.Id,
            AgentUserId = user.UserId.Value,
            Provider = _dialer.Name,
            ProviderCallId = result.CallId,
            Status = result.Status
        });
        await _db.SaveChangesAsync(ct);

        return Ok(result);
    }

    [HttpGet("carriers")]
    public IActionResult Carriers() => Ok(_carriers.AvailableCarriers);
}
