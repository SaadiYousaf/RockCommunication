using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CRM.Application.Leads;

/// <summary>
/// Administrative correction of a lead's own details, and of the policy recorded against it.
///
/// Until now a lead's ONLY editable field was its notes, and a sale could not be corrected at all —
/// so a mistyped phone number, a wrong carrier or a premium entered as 400 instead of 40 was
/// permanent. The only remedy was to delete the whole lead and re-key it, which destroys the sale,
/// the commission and the history along with the mistake.
///
/// Every field here is optional: null means "leave this alone", so a caller fixing one digit of a
/// phone number does not have to resend the entire record and risk overwriting a field someone else
/// changed in the meantime.
/// </summary>
public record AdminUpdateLeadCommand(
    Guid LeadId,
    string? FirstName = null,
    string? LastName = null,
    string? Email = null,
    string? PhoneNumber = null,
    string? Address = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null,
    string? MaritalStatus = null,
    DateTime? DateOfBirth = null,
    int? AgeYears = null) : IRequest<Unit>;

/// <summary>Correct the policy details recorded on a sale.</summary>
public record AdminUpdateSaleCommand(
    Guid SaleId,
    string? Carrier = null,
    string? PolicyNumber = null,
    decimal? MonthlyPremium = null,
    decimal? AnnualPremium = null,
    DateTime? SoldAt = null) : IRequest<Unit>;

public class AdminUpdateLeadValidator : AbstractValidator<AdminUpdateLeadCommand>
{
    public AdminUpdateLeadValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.FirstName!).MaximumLength(80).When(x => x.FirstName is not null);
        RuleFor(x => x.LastName!).MaximumLength(80).When(x => x.LastName is not null);
        RuleFor(x => x.Email!).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.PhoneNumber!).NotEmpty().MaximumLength(32).When(x => x.PhoneNumber is not null);
        RuleFor(x => x.State!).MaximumLength(64).When(x => x.State is not null);
        RuleFor(x => x.PostalCode!).MaximumLength(16).When(x => x.PostalCode is not null);
        RuleFor(x => x.AgeYears!.Value).InclusiveBetween(0, 120).When(x => x.AgeYears is not null);
        RuleFor(x => x.DateOfBirth!.Value)
            .LessThan(DateTime.UtcNow).WithMessage("A date of birth can't be in the future.")
            .When(x => x.DateOfBirth is not null);
    }
}

public class AdminUpdateSaleValidator : AbstractValidator<AdminUpdateSaleCommand>
{
    public AdminUpdateSaleValidator()
    {
        RuleFor(x => x.SaleId).NotEmpty();
        RuleFor(x => x.Carrier!).NotEmpty().MaximumLength(120).When(x => x.Carrier is not null);
        RuleFor(x => x.PolicyNumber!).MaximumLength(64).When(x => x.PolicyNumber is not null);
        // Zero is allowed (a correction down to nothing is meaningful); negative is not.
        RuleFor(x => x.MonthlyPremium!.Value).GreaterThanOrEqualTo(0).When(x => x.MonthlyPremium is not null);
        RuleFor(x => x.AnnualPremium!.Value).GreaterThanOrEqualTo(0).When(x => x.AnnualPremium is not null);
        RuleFor(x => x.SoldAt!.Value)
            .LessThan(DateTime.UtcNow.AddDays(1)).WithMessage("A sale can't have been made in the future.")
            .When(x => x.SoldAt is not null);
    }
}

public class AdminEditHandler :
    IRequestHandler<AdminUpdateLeadCommand, Unit>,
    IRequestHandler<AdminUpdateSaleCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly INotificationDispatcher _notify;

    public AdminEditHandler(IApplicationDbContext db, ICurrentUser user, INotificationDispatcher notify)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _notify = Guard.AgainstNull(notify);
    }

    public async Task<Unit> Handle(AdminUpdateLeadCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var actingName = _user.UserName ?? "An administrator";

        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == request.LeadId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        var changes = new Dictionary<string, object?>();
        Set(changes, "firstName", lead.FirstName, request.FirstName, v => lead.FirstName = v);
        Set(changes, "lastName", lead.LastName, request.LastName, v => lead.LastName = v);
        Set(changes, "email", lead.Email, request.Email, v => lead.Email = v);
        Set(changes, "phoneNumber", lead.PhoneNumber, request.PhoneNumber, v => lead.PhoneNumber = v);
        Set(changes, "address", lead.Address, request.Address, v => lead.Address = v);
        Set(changes, "city", lead.City, request.City, v => lead.City = v);
        Set(changes, "state", lead.State, request.State, v => lead.State = v);
        Set(changes, "postalCode", lead.PostalCode, request.PostalCode, v => lead.PostalCode = v);
        Set(changes, "maritalStatus", lead.MaritalStatus, request.MaritalStatus, v => lead.MaritalStatus = v);
        SetValue(changes, "dateOfBirth", lead.DateOfBirth, request.DateOfBirth, v => lead.DateOfBirth = v);
        SetValue(changes, "ageYears", lead.AgeYears, request.AgeYears, v => lead.AgeYears = v);

        if (changes.Count == 0) return Unit.Value;

        WriteAudit(lead.AgencyId, nameof(Lead), lead.Id, "LeadEditedByAdmin", actingName, changes);
        await _db.SaveChangesAsync(ct);

        // The person working this lead needs to know its details moved under them — they may be
        // about to dial a number that is no longer the one they were given.
        if (lead.AssignedUserId is { } owner)
        {
            await NotifyAsync(lead.AgencyId, owner, actingName,
                "A lead you're working was edited",
                $"{actingName} corrected details on {lead.FirstName} {lead.LastName}.".Trim(),
                $"/leads/{lead.Id}", ct);
        }
        return Unit.Value;
    }

    public async Task<Unit> Handle(AdminUpdateSaleCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var actingName = _user.UserName ?? "An administrator";

        var sale = await _db.Sales.FirstOrDefaultAsync(s => s.Id == request.SaleId, ct)
            ?? throw new NotFoundException(nameof(Sale), request.SaleId);

        var changes = new Dictionary<string, object?>();
        Set(changes, "carrier", sale.Carrier, request.Carrier, v => sale.Carrier = v ?? string.Empty);
        Set(changes, "policyNumber", sale.PolicyNumber, request.PolicyNumber, v => sale.PolicyNumber = v);
        SetValue(changes, "monthlyPremium", sale.MonthlyPremium, request.MonthlyPremium, v => sale.MonthlyPremium = v);
        SetValue(changes, "annualPremium", sale.AnnualPremium, request.AnnualPremium, v => sale.AnnualPremium = v);
        SetValue(changes, "soldAt", sale.SoldAt, request.SoldAt, v => sale.SoldAt = v);

        if (changes.Count == 0) return Unit.Value;

        WriteAudit(sale.AgencyId, nameof(Sale), sale.Id, "SaleEditedByAdmin", actingName, changes);
        await _db.SaveChangesAsync(ct);

        // A premium change moves the closer's commission. Commission entries already written are
        // NOT recalculated here on purpose — rewriting money that may already have been paid out,
        // silently, as a side effect of a typo fix, is worse than leaving it to be settled
        // deliberately. The closer is told so the discrepancy is noticed now rather than at payroll.
        var premiumChanged = changes.ContainsKey("monthlyPremium") || changes.ContainsKey("annualPremium");
        await NotifyAsync(sale.AgencyId, sale.CloserUserId, actingName,
            "A sale of yours was edited",
            premiumChanged
                ? $"{actingName} corrected the premium on sale #{sale.SaleNumber}. Check your commission on it."
                : $"{actingName} corrected details on sale #{sale.SaleNumber}.",
            "/my-sales", ct);

        return Unit.Value;
    }

    /// <summary>Applies a nullable reference field and records the before/after when it moved.</summary>
    private static void Set(
        IDictionary<string, object?> changes, string name, string? current, string? incoming, Action<string?> apply)
    {
        if (incoming is null) return;                       // not supplied — leave alone
        var next = incoming.Trim();
        if (string.Equals(current ?? string.Empty, next, StringComparison.Ordinal)) return;
        changes[name] = new { from = current, to = next };
        apply(next);
    }

    /// <summary>The value-type form, for dates, numbers and the like.</summary>
    private static void SetValue<T>(
        IDictionary<string, object?> changes, string name, T current, T? incoming, Action<T> apply) where T : struct
    {
        if (incoming is null) return;
        if (EqualityComparer<T>.Default.Equals(current, incoming.Value)) return;
        changes[name] = new { from = current, to = incoming.Value };
        apply(incoming.Value);
    }

    /// <summary>The nullable-value-type form.</summary>
    private static void SetValue<T>(
        IDictionary<string, object?> changes, string name, T? current, T? incoming, Action<T?> apply) where T : struct
    {
        if (incoming is null) return;
        if (current is not null && EqualityComparer<T>.Default.Equals(current.Value, incoming.Value)) return;
        changes[name] = new { from = current, to = incoming.Value };
        apply(incoming.Value);
    }

    /// <summary>
    /// An explicit audit row naming every field that moved, with its old and new value.
    ///
    /// The interceptor's automatic "Updated" entry says only that the row changed, which for a
    /// correction to a customer's bank-adjacent record is not enough to answer "who changed the
    /// premium, and what was it before?".
    /// </summary>
    private void WriteAudit(
        Guid agencyId, string entityName, Guid entityId, string action, string actingName,
        Dictionary<string, object?> changes)
    {
        _db.AuditEntries.Add(new AuditEntry
        {
            AgencyId = agencyId == Guid.Empty ? null : agencyId,
            EntityName = entityName,
            EntityId = entityId.ToString(),
            Action = action,
            UserId = _user.UserId?.ToString(),
            UserName = actingName,
            Changes = JsonSerializer.Serialize(changes),
            IpAddress = _user.IpAddress,
        });
    }

    private async Task NotifyAsync(
        Guid agencyId, Guid userId, string actingName, string title, string body, string url, CancellationToken ct)
    {
        if (userId == Guid.Empty || userId == _user.UserId) return;
        try
        {
            await _notify.DispatchAsync(
                new NotificationPayload(agencyId, userId, title, body, url),
                new[] { NotificationChannelType.InApp }, ct);
        }
        catch { /* graceful — the correction stands either way */ }
    }
}
