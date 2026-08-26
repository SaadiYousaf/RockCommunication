using CRM.Api.Authorization;
using CRM.Application.Common.Authorization;
using CRM.Application.CommissionDesk;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Api.Controllers;

/// <summary>
/// The Commission Desk — a CROSS-AGENCY financial workspace that sits after submission and before
/// retention. The Commission Agent reviews submitted sales, sets their financial status, reconciles
/// charged-back amounts, and manages the global carrier advancing rules.
/// </summary>
[ApiController]
[Authorize]
[Route("api/commission-desk")]
public class CommissionDeskController : ControllerBase
{
    private readonly IMediator _mediator;
    public CommissionDeskController(IMediator mediator) => _mediator = Guard.AgainstNull(mediator);

    /// <summary>Cross-agency sales with the desk's filters (agency, call centre, carrier, status, dates).</summary>
    [HasPermission(Permissions.CommissionDeskView)]
    [HttpGet("sales")]
    public async Task<IActionResult> Sales(
        [FromQuery] Guid? agencyId, [FromQuery] Guid? callCenterId, [FromQuery] string? carrier,
        [FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 50,
        CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListCommissionSalesQuery(
            agencyId, callCenterId, carrier, status, from, to, search, skip, take), ct));

    public record StatusBody(ValidatorStatus Status, string? Note);

    /// <summary>
    /// Set a sale's financial status. NSF / Bad Bank / Cancelled / Declined hand the policy to the
    /// Retention desk (which lists by exactly those statuses); Charged Back negates its amounts.
    /// </summary>
    [HasPermission(Permissions.CommissionDeskWork)]
    [HttpPost("sales/{saleId:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid saleId, [FromBody] StatusBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new SetCommissionStatusCommand(saleId, body.Status, body.Note), ct));
    }

    public record AmountBody(decimal Amount, string? Note);

    /// <summary>Edit one commission amount on a charged-back sale (unpaid lines only).</summary>
    [HasPermission(Permissions.CommissionDeskWork)]
    [HttpPut("sales/{saleId:guid}/amounts/{entryId:guid}")]
    public async Task<IActionResult> UpdateAmount(Guid saleId, Guid entryId, [FromBody] AmountBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpdateCommissionAmountCommand(saleId, entryId, body.Amount, body.Note), ct));
    }

    /// <summary>Expected advance + actuals by agency / call centre for one month.</summary>
    [HasPermission(Permissions.CommissionDeskView)]
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
        => Ok(await _mediator.Send(new CommissionDeskDashboardQuery(year, month), ct));

    // ---- Carrier advancing rules (global; the only place they're edited) ----

    [HasPermission(Permissions.CommissionDeskView)]
    [HttpGet("carrier-rules")]
    public async Task<IActionResult> CarrierRules(CancellationToken ct)
        => Ok(await _mediator.Send(new ListCarrierRulesQuery(), ct));

    public record CarrierRuleBody(Guid? Id, string Carrier, decimal CommissionRate, int AdvancedMonths,
        string? Notes, bool IsActive = true);

    [HasPermission(Permissions.CarrierRulesManage)]
    [HttpPost("carrier-rules")]
    public async Task<IActionResult> UpsertCarrierRule([FromBody] CarrierRuleBody body, CancellationToken ct)
    {
        Guard.AgainstNull(body);
        return Ok(await _mediator.Send(new UpsertCarrierRuleCommand(
            body.Id, body.Carrier, body.CommissionRate, body.AdvancedMonths, body.Notes, body.IsActive), ct));
    }

    [HasPermission(Permissions.CarrierRulesManage)]
    [HttpDelete("carrier-rules/{id:guid}")]
    public async Task<IActionResult> DeleteCarrierRule(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteCarrierRuleCommand(id), ct);
        return NoContent();
    }
}
