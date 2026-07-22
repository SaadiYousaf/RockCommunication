using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Sales.Commands;

public record FundSaleCommand(Guid SaleId) : IRequest<SaleDto>;

public class FundSaleHandler : IRequestHandler<FundSaleCommand, SaleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IFundingProvider _funding;

    public FundSaleHandler(IApplicationDbContext db, ICurrentUser user, IFundingProvider funding)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _funding = Guard.AgainstNull(funding);
    }

    public async Task<SaleDto> Handle(FundSaleCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();

        var sale = await _db.Sales.FirstOrDefaultAsync(
            s => s.Id == request.SaleId && s.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Sale), request.SaleId);

        if (sale.ValidatedAt is null)
            throw new ConflictException("Sale must be validated before funding.");

        var result = await _funding.SubmitAsync(
            new FundingRequest(sale.Id, sale.PolicyNumber ?? string.Empty, sale.AnnualPremium, sale.Carrier), ct);

        if (result.Accepted)
        {
            sale.FundedAt = DateTime.UtcNow;
            var lead = await _db.Leads.FirstAsync(l => l.Id == sale.LeadId, ct);
            lead.Stage = WorkflowStage.Funded;
            await _db.SaveChangesAsync(ct);
        }

        return new SaleDto(sale.Id, sale.LeadId, sale.CloserUserId, sale.ValidatorUserId,
            sale.Carrier, sale.PolicyNumber, sale.MonthlyPremium, sale.AnnualPremium,
            sale.SoldAt, sale.ValidatedAt, sale.FundedAt, sale.IsInternalSale, sale.InternalSaleReason,
            sale.BankingCode, sale.BankName, sale.BankAccountLast4, sale.LyonsReference);
    }
}
