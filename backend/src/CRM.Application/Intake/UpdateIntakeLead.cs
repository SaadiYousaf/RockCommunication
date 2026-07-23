using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Intake;

/// <summary>
/// Fields a Verifier (or admin) can correct on a lead while it sits in the queue — the same
/// Jornaya intake fields the Fronter captured, so mistakes can be fixed before closing.
/// </summary>
public record UpdateIntakeLeadDto(
    string FirstName,
    string LastName,
    string? MaritalStatus,
    string? StreetAddress,
    string? City,
    string? State,
    string? Zipcode,
    string PhoneNumber,
    DateTime? BirthDate,
    int? AgeYears,
    string? Email,
    string? JornayaLeadId);

public record UpdateIntakeLeadCommand(Guid LeadId, UpdateIntakeLeadDto Input) : IRequest<IntakeLeadResult>;

public class UpdateIntakeLeadValidator : AbstractValidator<UpdateIntakeLeadCommand>
{
    public UpdateIntakeLeadValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.Input.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Input.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Input.PhoneNumber).NotEmpty().Matches(@"^[\d\-\+\(\)\s]{7,20}$");
        RuleFor(x => x.Input.AgeYears).InclusiveBetween(1, 129).When(x => x.Input.AgeYears is not null);
        RuleFor(x => x.Input.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Input.Email));
    }
}

public class UpdateIntakeLeadHandler : IRequestHandler<UpdateIntakeLeadCommand, IntakeLeadResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateIntakeLeadHandler(IApplicationDbContext db, ICurrentUser user)
    { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IntakeLeadResult> Handle(UpdateIntakeLeadCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        // The global filter already scopes to the caller's agency + call center.
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == request.LeadId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        var d = request.Input;
        lead.FirstName = d.FirstName.Trim();
        lead.LastName = d.LastName.Trim();
        lead.PhoneNumber = d.PhoneNumber.Trim();
        lead.Email = string.IsNullOrWhiteSpace(d.Email) ? lead.Email : d.Email.Trim();
        lead.Address = d.StreetAddress?.Trim();
        lead.City = d.City?.Trim();
        lead.State = d.State?.Trim();
        lead.PostalCode = d.Zipcode?.Trim();
        lead.MaritalStatus = d.MaritalStatus?.Trim();
        if (d.AgeYears is { } age) lead.AgeYears = age;
        if (d.BirthDate is { } bd) lead.DateOfBirth = DateTime.SpecifyKind(bd, DateTimeKind.Utc);
        if (!string.IsNullOrWhiteSpace(d.JornayaLeadId)) lead.JornayaLeadId = d.JornayaLeadId.Trim();
        lead.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return new IntakeLeadResult(lead.Id, lead.FirstName, lead.LastName, lead.Stage);
    }
}
