using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Intake;

public record GetClosingApplicationQuery(Guid LeadId) : IRequest<ClosingApplicationView>;

/// <summary>Lead summary (for prefill) plus the saved application, if one exists.</summary>
public record ClosingApplicationView(
    Guid LeadId,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string? Email,
    string? Address,
    string? City,
    string? State,
    DateTime? DateOfBirth,
    WorkflowStage Stage,
    CloserStatus CloserStatus,
    LeadApplicationDto? Application);

public record LeadApplicationDto(
    string? HealthConditions, string? Gender, int? Age, string? SmokerStatus, string? Name,
    DateTime? DateOfBirth, string? Address, string? Carrier, string? Plan, decimal? FaceAmount,
    decimal? Premium, string? Email, string? Beneficiary, string? SecondBeneficiary,
    DateTime? InitialDraftDate, DateTime? FutureDraftDate, string? PhoneNumber, string? AltPhone,
    string? PrimaryDoctor, string? Social, string? BornIn, string? DriversLicense, string? Height,
    string? Weight, string? AccountType, string? BankName, string? AccountNumber, string? RoutingNumber,
    CloserStatus CloserStatus, Guid? SaleId);

public class GetClosingApplicationHandler : IRequestHandler<GetClosingApplicationQuery, ClosingApplicationView>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public GetClosingApplicationHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<ClosingApplicationView> Handle(GetClosingApplicationQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        var a = await _db.LeadApplications.FirstOrDefaultAsync(x => x.LeadId == lead.Id && x.AgencyId == _user.AgencyId, ct);

        LeadApplicationDto? appDto = a is null ? null : new LeadApplicationDto(
            a.HealthConditions, a.Gender, a.Age, a.SmokerStatus, a.Name, a.DateOfBirth, a.Address,
            a.Carrier, a.Plan, a.FaceAmount, a.Premium, a.Email, a.Beneficiary, a.SecondBeneficiary,
            a.InitialDraftDate, a.FutureDraftDate, a.PhoneNumber, a.AltPhone, a.PrimaryDoctor, a.Social,
            a.BornIn, a.DriversLicense, a.Height, a.Weight, a.AccountType, a.BankName, a.AccountNumber,
            a.RoutingNumber, a.CloserStatus, a.SaleId);

        return new ClosingApplicationView(
            lead.Id, lead.FirstName, lead.LastName, lead.PhoneNumber, lead.Email, lead.Address,
            lead.City, lead.State, lead.DateOfBirth, lead.Stage, a?.CloserStatus ?? CloserStatus.None, appDto);
    }
}
