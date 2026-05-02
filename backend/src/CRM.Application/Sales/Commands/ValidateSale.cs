using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Interfaces;
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
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IFundingProvider _funding;
    private readonly ILogger<ValidateSaleHandler> _logger;

    public ValidateSaleHandler(
        IApplicationDbContext db,
        ICurrentUser user,
        IFundingProvider funding,
        ILogger<ValidateSaleHandler> logger)
    {
        _db = db;
        _user = user;
        _funding = funding;
        _logger = logger;
    }

    public async Task<SaleDto> Handle(ValidateSaleCommand request, CancellationToken ct)
    {
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
        }
        else
        {
            sale.ValidatorUserId = _user.UserId;
            sale.ValidatedAt = null;
            lead.Stage = WorkflowStage.Followup;
            lead.Disposition = LeadDisposition.NotQualified;
        }

        _db.LeadActivities.Add(new LeadActivity
        {
            AgencyId = sale.AgencyId,
            LeadId = sale.LeadId,
            UserId = _user.UserId.Value,
            FromStage = WorkflowStage.Closed,
            ToStage = lead.Stage,
            Disposition = lead.Disposition,
            Notes = request.Notes ?? (request.Approve ? "Validated" : "Rejected")
        });

        await _db.SaveChangesAsync(ct);

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
                        AgencyId = sale.AgencyId,
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
}
