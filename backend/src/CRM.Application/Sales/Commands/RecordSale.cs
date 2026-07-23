using CRM.Application.Common.Commission;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Workflow;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Sales.Commands;

public record RecordSaleDto(
    Guid LeadId,
    string Carrier,
    string? PolicyNumber,
    decimal MonthlyPremium,
    /// <summary>Bank routing number — validated server-side by Lyons to derive the banking code.</summary>
    string RoutingNumber,
    /// <summary>Bank account number — validated by Lyons; only the last four digits are persisted.</summary>
    string AccountNumber,
    /// <summary>Optional account type hint (checking/savings).</summary>
    string? AccountType,
    /// <summary>Opaque storage key from POST /api/sales/recording-upload. Required when Lyons returns banking code 198.</summary>
    string? RecordingKey);

public record RecordSaleCommand(RecordSaleDto Input) : IRequest<SaleDto>;

public record SaleDto(
    Guid Id, Guid LeadId, Guid CloserUserId, Guid? ValidatorUserId,
    string Carrier, string? PolicyNumber, decimal MonthlyPremium, decimal AnnualPremium,
    DateTime SoldAt, DateTime? ValidatedAt, DateTime? FundedAt,
    bool IsInternalSale, string? InternalSaleReason,
    int BankingCode, string? BankName, string? BankAccountLast4, string? LyonsReference);

/// <summary>
/// Banking-code submission policy. Centralised so the rule is testable and the
/// frontend can mirror the exact same constants.
/// </summary>
public static class BankingPolicy
{
    /// <summary>Clear — sale may be submitted as-is.</summary>
    public const int Clear = 103;
    /// <summary>Conditional — sale may be submitted only with a verification recording.</summary>
    public const int RequiresRecording = 198;

    public static bool IsSubmittable(int code) => code is Clear or RequiresRecording;
    public static bool NeedsRecording(int code) => code == RequiresRecording;
}

public class RecordSaleValidator : AbstractValidator<RecordSaleCommand>
{
    public RecordSaleValidator()
    {
        RuleFor(x => x.Input.LeadId).NotEmpty();
        RuleFor(x => x.Input.Carrier).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Input.MonthlyPremium).GreaterThan(0);
        RuleFor(x => x.Input.RoutingNumber).NotEmpty().WithMessage("Bank routing number is required for Lyons validation.");
        RuleFor(x => x.Input.AccountNumber).NotEmpty().WithMessage("Bank account number is required for Lyons validation.");
    }
}

public class RecordSaleHandler : IRequestHandler<RecordSaleCommand, SaleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ICommissionEngine _commission;
    private readonly IInternalSaleChecker _checker;
    private readonly IWorkflowEngine _workflow;
    private readonly ILyonsBankingValidator _lyons;

    public RecordSaleHandler(IApplicationDbContext db, ICurrentUser user, ICommissionEngine commission,
        IInternalSaleChecker checker, IWorkflowEngine workflow, ILyonsBankingValidator lyons)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _commission = Guard.AgainstNull(commission);
        _checker = Guard.AgainstNull(checker);
        _workflow = Guard.AgainstNull(workflow);
        _lyons = Guard.AgainstNull(lyons);
    }

    public async Task<SaleDto> Handle(RecordSaleCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();

        var input = request.Input;
        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == input.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), input.LeadId);

        // 1) Duplicate check — a lead can only have one sale.
        var existing = await _db.Sales.AnyAsync(s => s.LeadId == lead.Id, ct);
        if (existing) throw new ConflictException("A sale already exists for this lead.");

        // 2) Banking gate — the banking code is derived from a Lyons validation of the
        //    bank account, never entered by the closer.
        var lyons = await _lyons.ValidateAsync(
            new LyonsValidationRequest(input.RoutingNumber, input.AccountNumber, input.AccountType,
                $"{lead.FirstName} {lead.LastName}".Trim()), ct);

        if (lyons.Status == BankValidationStatus.Blocked || !BankingPolicy.IsSubmittable(lyons.BankingCode))
            throw new ConflictException(
                $"Lyons blocked this bank account — the sale cannot be submitted. {lyons.Reason}".Trim());

        if (BankingPolicy.NeedsRecording(lyons.BankingCode) && string.IsNullOrWhiteSpace(input.RecordingKey))
            throw new ConflictException(
                $"Lyons flagged this account (banking code {BankingPolicy.RequiresRecording}) — a verification recording must be uploaded before the sale can be submitted.");

        var (isInternal, reason) = await _checker.CheckAsync(lead, _user.UserId.Value, ct);

        var accountDigits = new string((input.AccountNumber ?? "").Where(char.IsDigit).ToArray());
        var sale = new Sale
        {
            AgencyId = lead.AgencyId,
            CallCenterId = lead.CallCenterId,
            LeadId = lead.Id,
            CloserUserId = _user.UserId.Value,
            Carrier = input.Carrier.ToUpperInvariant(),
            PolicyNumber = input.PolicyNumber,
            MonthlyPremium = input.MonthlyPremium,
            AnnualPremium = Math.Round(input.MonthlyPremium * 12m, 2),
            SoldAt = DateTime.UtcNow,
            IsInternalSale = isInternal,
            InternalSaleReason = reason,
            BankingCode = lyons.BankingCode,
            RecordingUrl = BankingPolicy.NeedsRecording(lyons.BankingCode) ? input.RecordingKey : null,
            BankRoutingNumber = new string((input.RoutingNumber ?? "").Where(char.IsDigit).ToArray()),
            BankAccountLast4 = accountDigits.Length >= 4 ? accountDigits[^4..] : accountDigits,
            BankName = lyons.BankName,
            LyonsReference = lyons.Reference
        };
        _db.Sales.Add(sale);

        lead.Stage = WorkflowStage.Closed;
        lead.Disposition = LeadDisposition.Sold;
        lead.UpdatedAt = DateTime.UtcNow;
        _db.LeadActivities.Add(new LeadActivity
        {
            AgencyId = lead.AgencyId,
            CallCenterId = lead.CallCenterId,
            LeadId = lead.Id,
            UserId = _user.UserId.Value,
            FromStage = WorkflowStage.Verified,
            ToStage = WorkflowStage.Closed,
            Disposition = LeadDisposition.Sold,
            Notes = $"Sale recorded: {sale.Carrier} ${sale.MonthlyPremium}/mo"
        });

        await _db.SaveChangesAsync(ct);

        var lines = await _commission.CalculateForSaleAsync(sale, ct);
        foreach (var line in lines)
        {
            _db.CommissionEntries.Add(new CommissionEntry
            {
                AgencyId = sale.AgencyId,
                CallCenterId = sale.CallCenterId,
                SaleId = sale.Id,
                AgentUserId = line.AgentId,
                RuleName = line.RuleName,
                Amount = line.Amount,
                Note = line.Note
            });
        }
        await _db.SaveChangesAsync(ct);

        await _workflow.PublishAsync(new SaleClosedEvent
        {
            AgencyId = sale.AgencyId,
            SaleId = sale.Id,
            LeadId = sale.LeadId,
            CloserUserId = sale.CloserUserId,
            Carrier = sale.Carrier,
            MonthlyPremium = sale.MonthlyPremium,
            IsInternalSale = sale.IsInternalSale
        }, ct);

        return Map(sale);
    }

    private static SaleDto Map(Sale s) => new(s.Id, s.LeadId, s.CloserUserId, s.ValidatorUserId,
        s.Carrier, s.PolicyNumber, s.MonthlyPremium, s.AnnualPremium,
        s.SoldAt, s.ValidatedAt, s.FundedAt, s.IsInternalSale, s.InternalSaleReason,
        s.BankingCode, s.BankName, s.BankAccountLast4, s.LyonsReference);
}

public interface IInternalSaleChecker
{
    Task<(bool IsInternal, string? Reason)> CheckAsync(Lead lead, Guid closerUserId, CancellationToken ct = default);
}
