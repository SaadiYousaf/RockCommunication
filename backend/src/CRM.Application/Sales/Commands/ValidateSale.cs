using CRM.Application.Common.Commission;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CRM.Application.Sales.Commands;

public record ValidateSaleCommand(Guid SaleId, bool Approve, string? Notes) : IRequest<SaleDto>;

public class ValidateSaleValidator : AbstractValidator<ValidateSaleCommand>
{
    public ValidateSaleValidator() => RuleFor(x => x.SaleId).NotEmpty();
}

public class ValidateSaleHandler : IRequestHandler<ValidateSaleCommand, SaleDto>
{
    // User-facing notification copy, kept next to the sender and free of internal status names.
    private const string RejectedTitle = "Your sale was rejected";

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IFundingProvider _funding;
    private readonly INotificationDispatcher _notify;
    private readonly ILogger<ValidateSaleHandler> _logger;

    public ValidateSaleHandler(
        IApplicationDbContext db,
        ICurrentUser user,
        IFundingProvider funding,
        INotificationDispatcher notify,
        ILogger<ValidateSaleHandler> logger)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _funding = Guard.AgainstNull(funding);
        _notify = Guard.AgainstNull(notify);
        _logger = Guard.AgainstNull(logger);
    }

    public async Task<SaleDto> Handle(ValidateSaleCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();

        var sale = await _db.Sales.FirstOrDefaultAsync(
            s => s.Id == request.SaleId && s.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Sale), request.SaleId);

        if (sale.CloserUserId == _user.UserId)
            throw new ForbiddenAccessException("Closer cannot self-validate (use SelfValidator role flow).");

        var lead = await _db.Leads.FirstAsync(l => l.Id == sale.LeadId, ct);

        if (request.Approve)
        {
            sale.ValidatorUserId = _user.UserId;
            sale.ValidatedAt = DateTime.UtcNow;
            lead.Stage = WorkflowStage.Validated;

            // Symmetry with the reject branch below: a sale rejected earlier had its unpaid
            // commission voided, so approving it must bring that money back. Without this the
            // agent was silently paid nothing on a sale that ultimately passed. Also clear the
            // rejection's disposition so the lead doesn't stay marked Not Qualified.
            await CommissionLedger.ReviveUnpaidAsync(_db, sale, ct);
            if (lead.Disposition == LeadDisposition.NotQualified) lead.Disposition = LeadDisposition.Sold;
        }
        else
        {
            sale.ValidatorUserId = _user.UserId;
            sale.ValidatedAt = null;
            lead.Stage = WorkflowStage.Followup;
            lead.Disposition = LeadDisposition.NotQualified;

            // A rejected sale didn't pass validation — void its still-unpaid commission so payroll
            // never pays out for it. Already-paid entries are left untouched.
            await CommissionLedger.VoidUnpaidAsync(_db, sale, ct);
        }

        _db.LeadActivities.Add(new LeadActivity
        {
            AgencyId = sale.AgencyId, CallCenterId = sale.CallCenterId,
            LeadId = sale.LeadId,
            UserId = _user.UserId.Value,
            FromStage = WorkflowStage.Closed,
            ToStage = lead.Stage,
            Disposition = lead.Disposition,
            Notes = request.Notes ?? (request.Approve ? "Validated" : "Rejected")
        });

        await _db.SaveChangesAsync(ct);

        // A rejection kills someone else's sale and voids the commission on it — they must hear about
        // it. Self-validation is blocked above, so the closer is always another user.
        if (!request.Approve) await NotifyCloserOfRejectionAsync(sale, lead, request.Notes, ct);

        // Policy Funding Automation — when a sale is approved, immediately submit
        // it to the funding provider unless config has Sales:AutoFundOnValidate=false.
        // Funding failures must NOT roll back the validation, so we wrap the call
        // and only persist when the provider accepts.
        // Opt-out: set CRM_DISABLE_AUTOFUND=1 in env to skip the auto-funding step.
        var autoFundDisabled = string.Equals(
            Environment.GetEnvironmentVariable("CRM_DISABLE_AUTOFUND"), "1",
            StringComparison.Ordinal);
        if (!autoFundDisabled
            && request.Approve && sale.ValidatedAt is not null && sale.FundedAt is null)
        {
            try
            {
                var result = await _funding.SubmitAsync(
                    new FundingRequest(sale.Id, sale.PolicyNumber ?? string.Empty,
                        sale.AnnualPremium, sale.Carrier),
                    ct);

                if (result.Accepted)
                {
                    sale.FundedAt = DateTime.UtcNow;
                    lead.Stage = WorkflowStage.Funded;
                    _db.LeadActivities.Add(new LeadActivity
                    {
                        AgencyId = sale.AgencyId, CallCenterId = sale.CallCenterId,
                        LeadId = sale.LeadId,
                        UserId = _user.UserId.Value,
                        FromStage = WorkflowStage.Validated,
                        ToStage = WorkflowStage.Funded,
                        Disposition = lead.Disposition,
                        Notes = "Auto-funded after validation",
                    });
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation("Sale {SaleId} auto-funded after validation", sale.Id);
                }
                else
                {
                    _logger.LogWarning("Auto-funding declined for sale {SaleId}: {Reason}",
                        sale.Id, result.Reason ?? "unknown");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-funding failed for sale {SaleId}", sale.Id);
            }
        }

        return new SaleDto(sale.Id, sale.LeadId, sale.CloserUserId, sale.ValidatorUserId,
            sale.Carrier, sale.PolicyNumber, sale.MonthlyPremium, sale.AnnualPremium,
            sale.SoldAt, sale.ValidatedAt, sale.FundedAt, sale.IsInternalSale, sale.InternalSaleReason,
            sale.BankingCode, sale.BankName, sale.BankAccountLast4, sale.LyonsReference);
    }

    /// <summary>
    /// Best-effort in-app notice to the closer that their sale did not pass validation, carrying the
    /// validator's note as the reason when one was given. Never notifies the validator themselves.
    /// </summary>
    private async Task NotifyCloserOfRejectionAsync(Sale sale, Lead lead, string? notes, CancellationToken ct)
    {
        if (sale.CloserUserId == _user.UserId || sale.CloserUserId == Guid.Empty) return;
        var customer = $"{lead.FirstName} {lead.LastName}".Trim();
        if (customer.Length == 0) customer = "a customer";
        var reason = notes?.Trim();
        var body = string.IsNullOrWhiteSpace(reason)
            ? $"Your sale for {customer} did not pass validation, and the commission on it was cancelled."
            : $"Your sale for {customer} did not pass validation, and the commission on it was cancelled. Reason: {reason}";
        try
        {
            await _notify.DispatchAsync(
                new NotificationPayload(sale.AgencyId, sale.CloserUserId, RejectedTitle, body, $"/sales/{sale.Id}"),
                new[] { NotificationChannelType.InApp }, ct);
        }
        catch { /* graceful — the validation already succeeded */ }
    }
}
