using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Hr;

// ── DTOs ────────────────────────────────────────────────────────────────────

/// <summary>Full employee profile (HR-only view — includes decrypted CNIC / bank account).</summary>
public record EmployeeDto(
    Guid Id, string FullName, string AgentCode, string? PhoneNumber, string? OfficialEmail,
    EmployeeDesignation Designation, Guid? CallCenterId, string? CallCenterName, Guid? UserId,
    string? Cnic, Gender Gender, MaritalStatus MaritalStatus, DateTime? DateOfBirth,
    string? GuardianPhone, GuardianRelationship? GuardianRelationship,
    string? CurrentAddress, string? PermanentAddress,
    DateTime? DateOfJoining, EmploymentStatus EmploymentStatus,
    Guid? ReportingToEmployeeId, string? ReportingToName, string? WorkHours,
    string? BankName, string? BankAccountTitle, string? BankAccountNumber, string? BankIban,
    string? PhotoKey, string? IdCardFrontKey, string? IdCardBackKey, DateTime CreatedAt);

/// <summary>Roster row — no sensitive fields.</summary>
public record EmployeeListItemDto(
    Guid Id, string FullName, string AgentCode, string? PhoneNumber, string? OfficialEmail,
    EmployeeDesignation Designation, EmploymentStatus EmploymentStatus,
    Guid? CallCenterId, string? CallCenterName, DateTime? DateOfJoining, DateTime? DateOfBirth);

/// <summary>All writable fields, shared by create + update.</summary>
public record EmployeeInput(
    string FullName, string AgentCode, string? PhoneNumber, string? OfficialEmail,
    EmployeeDesignation Designation, Guid? CallCenterId, Guid? UserId,
    string? Cnic, Gender Gender, MaritalStatus MaritalStatus, DateTime? DateOfBirth,
    string? GuardianPhone, GuardianRelationship? GuardianRelationship,
    string? CurrentAddress, string? PermanentAddress,
    DateTime? DateOfJoining, EmploymentStatus EmploymentStatus,
    Guid? ReportingToEmployeeId, string? WorkHours,
    string? BankName, string? BankAccountTitle, string? BankAccountNumber, string? BankIban,
    string? PhotoKey, string? IdCardFrontKey, string? IdCardBackKey);

// ── Requests ────────────────────────────────────────────────────────────────

public record ListEmployeesQuery(string? Search = null, Guid? CallCenterId = null,
    EmployeeDesignation? Designation = null) : IRequest<IReadOnlyList<EmployeeListItemDto>>;

public record GetEmployeeQuery(Guid Id) : IRequest<EmployeeDto>;

public record CreateEmployeeCommand(EmployeeInput Input) : IRequest<EmployeeDto>;

public record UpdateEmployeeCommand(Guid Id, EmployeeInput Input) : IRequest<EmployeeDto>;

public record DeleteEmployeeCommand(Guid Id) : IRequest<Unit>;

// ── Validation ──────────────────────────────────────────────────────────────

public class EmployeeInputValidator : AbstractValidator<EmployeeInput>
{
    public EmployeeInputValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AgentCode).NotEmpty().MaximumLength(60);
        RuleFor(x => x.PhoneNumber).MaximumLength(40);
        RuleFor(x => x.OfficialEmail).MaximumLength(200)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.OfficialEmail));
        RuleFor(x => x.Designation).IsInEnum();
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.MaritalStatus).IsInEnum();
        RuleFor(x => x.EmploymentStatus).IsInEnum();
    }
}

public class CreateEmployeeValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeValidator() => RuleFor(x => x.Input).SetValidator(new EmployeeInputValidator());
}

public class UpdateEmployeeValidator : AbstractValidator<UpdateEmployeeCommand>
{
    public UpdateEmployeeValidator() => RuleFor(x => x.Input).SetValidator(new EmployeeInputValidator());
}

// ── Handlers ────────────────────────────────────────────────────────────────

public class EmployeeHandlers :
    IRequestHandler<ListEmployeesQuery, IReadOnlyList<EmployeeListItemDto>>,
    IRequestHandler<GetEmployeeQuery, EmployeeDto>,
    IRequestHandler<CreateEmployeeCommand, EmployeeDto>,
    IRequestHandler<UpdateEmployeeCommand, EmployeeDto>,
    IRequestHandler<DeleteEmployeeCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public EmployeeHandlers(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    /// <summary>HR module is HR / Admin / SuperAdmin only.</summary>
    private void EnsureHr()
    {
        if (_user.IsSuperAdmin
            || _user.Roles.Contains(DomainRoles.Admin)
            || _user.Roles.Contains(DomainRoles.HR)) return;
        throw new ForbiddenAccessException();
    }

    public async Task<IReadOnlyList<EmployeeListItemDto>> Handle(ListEmployeesQuery request, CancellationToken ct)
    {
        EnsureHr();
        var q = _db.Employees.AsNoTracking().AsQueryable();
        if (request.CallCenterId is { } cc) q = q.Where(e => e.CallCenterId == cc);
        if (request.Designation is { } d) q = q.Where(e => e.Designation == d);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            q = q.Where(e => e.FullName.Contains(s) || e.AgentCode.Contains(s)
                || (e.PhoneNumber != null && e.PhoneNumber.Contains(s))
                || (e.OfficialEmail != null && e.OfficialEmail.Contains(s)));
        }
        var rows = await q.OrderBy(e => e.FullName).ToListAsync(ct);
        var ccNames = await CallCenterNamesAsync(rows.Select(r => r.CallCenterId), ct);
        return rows.Select(e => new EmployeeListItemDto(
            e.Id, e.FullName, e.AgentCode, e.PhoneNumber, e.OfficialEmail, e.Designation, e.EmploymentStatus,
            e.CallCenterId, NameOrNull(ccNames, e.CallCenterId), e.DateOfJoining, e.DateOfBirth)).ToList();
    }

    public async Task<EmployeeDto> Handle(GetEmployeeQuery request, CancellationToken ct)
    {
        EnsureHr();
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Employee), request.Id);
        var ccNames = await CallCenterNamesAsync(new[] { e.CallCenterId }, ct);
        string? reportingToName = e.ReportingToEmployeeId is { } rid
            ? await _db.Employees.AsNoTracking().Where(x => x.Id == rid).Select(x => x.FullName).FirstOrDefaultAsync(ct)
            : null;
        return Map(e, NameOrNull(ccNames, e.CallCenterId), reportingToName);
    }

    public async Task<EmployeeDto> Handle(CreateEmployeeCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureHr();
        var i = request.Input;
        await EnsureAgentCodeFreeAsync(i.AgentCode, null, ct);
        var e = new Employee { AgencyId = _user.AgencyId ?? Guid.Empty };
        Apply(e, i);
        _db.Employees.Add(e);
        await _db.SaveChangesAsync(ct);
        return Map(e, null, null);
    }

    public async Task<EmployeeDto> Handle(UpdateEmployeeCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureHr();
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Employee), request.Id);
        await EnsureAgentCodeFreeAsync(request.Input.AgentCode, e.Id, ct);
        Apply(e, request.Input);
        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Map(e, null, null);
    }

    public async Task<Unit> Handle(DeleteEmployeeCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureHr();
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Employee), request.Id);
        e.IsDeleted = true;
        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    private async Task EnsureAgentCodeFreeAsync(string agentCode, Guid? excludeId, CancellationToken ct)
    {
        var code = agentCode.Trim();
        var clash = await _db.Employees.AnyAsync(
            e => e.AgentCode == code && (excludeId == null || e.Id != excludeId), ct);
        if (clash) throw new ConflictException($"Agent ID \"{code}\" is already in use.");
    }

    private static void Apply(Employee e, EmployeeInput i)
    {
        e.FullName = i.FullName.Trim();
        e.AgentCode = i.AgentCode.Trim();
        e.PhoneNumber = Clean(i.PhoneNumber);
        e.OfficialEmail = Clean(i.OfficialEmail);
        e.Designation = i.Designation;
        e.CallCenterId = i.CallCenterId;
        e.UserId = i.UserId;
        e.Cnic = Clean(i.Cnic);
        e.Gender = i.Gender;
        e.MaritalStatus = i.MaritalStatus;
        e.DateOfBirth = i.DateOfBirth;
        e.GuardianPhone = Clean(i.GuardianPhone);
        e.GuardianRelationship = i.GuardianRelationship;
        e.CurrentAddress = Clean(i.CurrentAddress);
        e.PermanentAddress = Clean(i.PermanentAddress);
        e.DateOfJoining = i.DateOfJoining;
        e.EmploymentStatus = i.EmploymentStatus;
        e.ReportingToEmployeeId = i.ReportingToEmployeeId;
        e.WorkHours = Clean(i.WorkHours);
        e.BankName = Clean(i.BankName);
        e.BankAccountTitle = Clean(i.BankAccountTitle);
        e.BankAccountNumber = Clean(i.BankAccountNumber);
        e.BankIban = Clean(i.BankIban);
        e.PhotoKey = Clean(i.PhotoKey);
        e.IdCardFrontKey = Clean(i.IdCardFrontKey);
        e.IdCardBackKey = Clean(i.IdCardBackKey);
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private async Task<Dictionary<Guid, string>> CallCenterNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(x => x is { } g && g != Guid.Empty).Select(x => x!.Value).Distinct().ToList();
        if (set.Count == 0) return new();
        return (await _db.CallCenters.AsNoTracking().IgnoreQueryFilters()
                .Where(c => set.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.Name);
    }

    private static string? NameOrNull(Dictionary<Guid, string> names, Guid? id) =>
        id is { } g && names.TryGetValue(g, out var n) ? n : null;

    private static EmployeeDto Map(Employee e, string? callCenterName, string? reportingToName) => new(
        e.Id, e.FullName, e.AgentCode, e.PhoneNumber, e.OfficialEmail, e.Designation, e.CallCenterId, callCenterName,
        e.UserId, e.Cnic, e.Gender, e.MaritalStatus, e.DateOfBirth, e.GuardianPhone, e.GuardianRelationship,
        e.CurrentAddress, e.PermanentAddress, e.DateOfJoining, e.EmploymentStatus, e.ReportingToEmployeeId,
        reportingToName, e.WorkHours, e.BankName, e.BankAccountTitle, e.BankAccountNumber, e.BankIban,
        e.PhotoKey, e.IdCardFrontKey, e.IdCardBackKey, e.CreatedAt);
}
