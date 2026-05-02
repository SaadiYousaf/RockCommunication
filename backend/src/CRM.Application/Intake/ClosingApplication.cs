using CRM.Application.Common.Compliance;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Sales.Commands;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Intake;

/// <summary>The closer's health / policy / banking application (all fields).</summary>
public record ClosingApplicationDto(
    string? HealthConditions,
    string Gender,
    int Age,
    string SmokerStatus,
    string Name,
    DateTime DateOfBirth,
    string Address,
    string Carrier,
    string Plan,
    decimal FaceAmount,
    decimal Premium,
    string Email,
    string Beneficiary,
    string? SecondBeneficiary,
    DateTime InitialDraftDate,
    DateTime? FutureDraftDate,
    string PhoneNumber,
    string? AltPhone,
    string PrimaryDoctor,
    string Social,
    string BornIn,
    string DriversLicense,
    string Height,
    string Weight,
    string AccountType,
    string BankName,
    string AccountNumber,
    string RoutingNumber);

public record SubmitClosingApplicationCommand(Guid LeadId, CloserStatus Status, ClosingApplicationDto Input)
    : IRequest<ClosingApplicationResult>;

public record ClosingApplicationResult(Guid LeadId, CloserStatus Status, WorkflowStage Stage, Guid? SaleId);

public class SubmitClosingApplicationValidator : AbstractValidator<SubmitClosingApplicationCommand>
{
    public SubmitClosingApplicationValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.Status).NotEqual(CloserStatus.None).WithMessage("Select a closer status.");

        // When the deal is sold the full application is mandatory (the banking
        // details also feed the Lyons validation on the created sale).
        When(x => x.Status == CloserStatus.CompleteAndSold, () =>
        {
            RuleFor(x => x.Input.Gender).NotEmpty();
            RuleFor(x => x.Input.Age).GreaterThan(0).LessThan(130);
            RuleFor(x => x.Input.SmokerStatus).NotEmpty();
            RuleFor(x => x.Input.Name).NotEmpty();
            RuleFor(x => x.Input.DateOfBirth).NotEmpty();
            RuleFor(x => x.Input.Address).NotEmpty();
            RuleFor(x => x.Input.Carrier).NotEmpty();
            RuleFor(x => x.Input.Plan).NotEmpty();
            RuleFor(x => x.Input.FaceAmount).GreaterThan(0);
            RuleFor(x => x.Input.Premium).GreaterThan(0);
            RuleFor(x => x.Input.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.Input.Beneficiary).NotEmpty();
            RuleFor(x => x.Input.InitialDraftDate).NotEmpty();
            RuleFor(x => x.Input.PhoneNumber).NotEmpty();
            RuleFor(x => x.Input.PrimaryDoctor).NotEmpty();
            RuleFor(x => x.Input.Social).NotEmpty();
            RuleFor(x => x.Input.BornIn).NotEmpty();
            RuleFor(x => x.Input.DriversLicense).NotEmpty();
            RuleFor(x => x.Input.Height).NotEmpty();
            RuleFor(x => x.Input.Weight).NotEmpty();
            RuleFor(x => x.Input.AccountType).NotEmpty();
            RuleFor(x => x.Input.BankName).NotEmpty();
            RuleFor(x => x.Input.AccountNumber).NotEmpty();
            RuleFor(x => x.Input.RoutingNumber).NotEmpty();
        });
    }
}

public class SubmitClosingApplicationHandler : IRequestHandler<SubmitClosingApplicationCommand, ClosingApplicationResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IMediator _mediator;
    private readonly IPhoneNormalizer _phone;

    public SubmitClosingApplicationHandler(IApplicationDbContext db, ICurrentUser user, IMediator mediator, IPhoneNormalizer phone)
    {
        _db = db;
        _user = user;
        _mediator = mediator;
        _phone = phone;
    }

    public async Task<ClosingApplicationResult> Handle(SubmitClosingApplicationCommand request, CancellationToken ct)
    {
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();

        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        if (lead.Stage != WorkflowStage.Verified)
            throw new ConflictException("Only verified leads in the closer queue can be closed.");

        var d = request.Input;
        var from = lead.Stage;

        // "Complete and Sold" creates the sale first (runs Lyons + commission). If Lyons
        // blocks the account this throws before anything is persisted, so the closer can retry.
        Guid? saleId = null;
        if (request.Status == CloserStatus.CompleteAndSold)
        {
            var sale = await _mediator.Send(new RecordSaleCommand(new RecordSaleDto(
                LeadId: lead.Id,
                Carrier: d.Carrier,
                PolicyNumber: d.Plan,
                MonthlyPremium: d.Premium,
                RoutingNumber: d.RoutingNumber,
                AccountNumber: d.AccountNumber,
                AccountType: d.AccountType,
                RecordingKey: null)), ct);
            saleId = sale.Id;
            // RecordSale already advanced the lead to Closed and wrote its own activity.
        }
        else
        {
            (lead.Stage, lead.Disposition) = request.Status switch
            {
                CloserStatus.LostOnSocial => (WorkflowStage.Lost, LeadDisposition.NotQualified),
                CloserStatus.LostOnAccount => (WorkflowStage.Lost, LeadDisposition.NotQualified),
                CloserStatus.DncLead => (WorkflowStage.Lost, LeadDisposition.DoNotCall),
                CloserStatus.NotInterestedCallback => (WorkflowStage.Followup, LeadDisposition.CallBack),
                _ => (lead.Stage, lead.Disposition),
            };
            lead.UpdatedAt = DateTime.UtcNow;

            if (request.Status == CloserStatus.DncLead)
                await AddDncAsync(lead, ct);
            if (request.Status == CloserStatus.NotInterestedCallback)
                _db.ScheduledCallbacks.Add(new ScheduledCallback
                {
                    AgencyId = lead.AgencyId,
                    LeadId = lead.Id,
                    AssignedUserId = _user.UserId.Value,
                    ScheduledFor = DateTime.UtcNow.AddDays(1),
                    Reason = "Closer callback"
                });

            _db.LeadActivities.Add(new LeadActivity
            {
                AgencyId = lead.AgencyId,
                LeadId = lead.Id,
                UserId = _user.UserId.Value,
                FromStage = from,
                ToStage = lead.Stage,
                Disposition = lead.Disposition,
                Notes = $"Closer status: {request.Status}."
            });
        }

        // Upsert the application record (one per lead).
        var app = await _db.LeadApplications.FirstOrDefaultAsync(a => a.LeadId == lead.Id && a.AgencyId == lead.AgencyId, ct);
        if (app is null)
        {
            app = new LeadApplication { AgencyId = lead.AgencyId, LeadId = lead.Id };
            _db.LeadApplications.Add(app);
        }
        MapInto(app, d);
        app.CloserStatus = request.Status;
        app.SubmittedByUserId = _user.UserId;
        app.SubmittedAt = DateTime.UtcNow;
        app.SaleId = saleId;

        await _db.SaveChangesAsync(ct);
        return new ClosingApplicationResult(lead.Id, request.Status, lead.Stage, saleId);
    }

    private static void MapInto(LeadApplication a, ClosingApplicationDto d)
    {
        a.HealthConditions = d.HealthConditions;
        a.Gender = d.Gender;
        a.Age = d.Age;
        a.SmokerStatus = d.SmokerStatus;
        a.Name = d.Name;
        a.DateOfBirth = d.DateOfBirth == default ? null : DateTime.SpecifyKind(d.DateOfBirth, DateTimeKind.Utc);
        a.Address = d.Address;
        a.Carrier = d.Carrier;
        a.Plan = d.Plan;
        a.FaceAmount = d.FaceAmount;
        a.Premium = d.Premium;
        a.Email = d.Email;
        a.Beneficiary = d.Beneficiary;
        a.SecondBeneficiary = d.SecondBeneficiary;
        a.InitialDraftDate = d.InitialDraftDate == default ? null : DateTime.SpecifyKind(d.InitialDraftDate, DateTimeKind.Utc);
        a.FutureDraftDate = d.FutureDraftDate is { } fd ? DateTime.SpecifyKind(fd, DateTimeKind.Utc) : null;
        a.PhoneNumber = d.PhoneNumber;
        a.AltPhone = d.AltPhone;
        a.PrimaryDoctor = d.PrimaryDoctor;
        a.Social = d.Social;
        a.BornIn = d.BornIn;
        a.DriversLicense = d.DriversLicense;
        a.Height = d.Height;
        a.Weight = d.Weight;
        a.AccountType = d.AccountType;
        a.BankName = d.BankName;
        // Store only the last four of the account number at rest; full value flows to Lyons via the sale.
        var digits = new string((d.AccountNumber ?? "").Where(char.IsDigit).ToArray());
        a.AccountNumber = digits.Length >= 4 ? $"****{digits[^4..]}" : digits;
        a.RoutingNumber = d.RoutingNumber;
    }

    private async Task AddDncAsync(Lead lead, CancellationToken ct)
    {
        var norm = _phone.Normalize(lead.PhoneNumber);
        if (string.IsNullOrEmpty(norm)) return;
        if (await _db.DncEntries.AnyAsync(e => e.AgencyId == lead.AgencyId && e.PhoneNormalized == norm, ct)) return;
        _db.DncEntries.Add(new DncEntry
        {
            AgencyId = lead.AgencyId,
            PhoneNormalized = norm,
            Reason = "Marked DNC by closer",
            Source = "Closer"
        });
    }
}
