using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Roles = CRM.Domain.Enums.Roles;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Sales.Commands;

public record SelfValidateSaleCommand(Guid SaleId, string? Notes) : IRequest<SaleDto>;

public class SelfValidateSaleValidator : AbstractValidator<SelfValidateSaleCommand>
{
    public SelfValidateSaleValidator() => RuleFor(x => x.SaleId).NotEmpty();
}

public class SelfValidateSaleHandler : IRequestHandler<SelfValidateSaleCommand, SaleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public SelfValidateSaleHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user);
    }

    public async Task<SaleDto> Handle(SelfValidateSaleCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains(CRM.Domain.Enums.Roles.SelfValidator))
            throw new ForbiddenAccessException("SelfValidator role required.");

        var sale = await _db.Sales.FirstOrDefaultAsync(
            s => s.Id == request.SaleId && s.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Sale), request.SaleId);

        if (sale.CloserUserId != _user.UserId)
            throw new ForbiddenAccessException("Only the closer can self-validate their own sale.");

        if (sale.IsInternalSale)
            throw new ConflictException("Internal sales cannot be self-validated.");

        sale.ValidatorUserId = _user.UserId;
        sale.ValidatedAt = DateTime.UtcNow;
        var lead = await _db.Leads.FirstAsync(l => l.Id == sale.LeadId, ct);
        lead.Stage = WorkflowStage.Validated;

        _db.LeadActivities.Add(new LeadActivity
        {
            AgencyId = sale.AgencyId, CallCenterId = sale.CallCenterId,
            LeadId = sale.LeadId,
            UserId = _user.UserId.Value,
            FromStage = WorkflowStage.Closed,
            ToStage = WorkflowStage.Validated,
            Disposition = LeadDisposition.Sold,
            Notes = request.Notes ?? "Self-validated"
        });

        await _db.SaveChangesAsync(ct);

        return new SaleDto(sale.Id, sale.LeadId, sale.CloserUserId, sale.ValidatorUserId,
            sale.Carrier, sale.PolicyNumber, sale.MonthlyPremium, sale.AnnualPremium,
            sale.SoldAt, sale.ValidatedAt, sale.FundedAt, sale.IsInternalSale, sale.InternalSaleReason,
            sale.BankingCode, sale.BankName, sale.BankAccountLast4, sale.LyonsReference);
    }
}
