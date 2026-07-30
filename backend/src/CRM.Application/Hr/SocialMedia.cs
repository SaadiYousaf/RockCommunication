using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Hr;

public record SocialMediaReportDto(
    Guid Id, DateTime Date, Guid? EmployeeId, string? EmployeeName, string? Platform,
    int PostsMade, int QueriesAnswered, string? Notes, DateTime CreatedAt);

public record SocialMediaInput(
    DateTime Date, Guid? EmployeeId, string? Platform, int PostsMade, int QueriesAnswered, string? Notes);

public record ListSocialReportsQuery(string? Search = null) : IRequest<IReadOnlyList<SocialMediaReportDto>>;
public record CreateSocialReportCommand(SocialMediaInput Input) : IRequest<SocialMediaReportDto>;
public record UpdateSocialReportCommand(Guid Id, SocialMediaInput Input) : IRequest<SocialMediaReportDto>;
public record DeleteSocialReportCommand(Guid Id) : IRequest<Unit>;

public class SocialMediaInputValidator : AbstractValidator<SocialMediaInput>
{
    public SocialMediaInputValidator()
    {
        RuleFor(x => x.PostsMade).GreaterThanOrEqualTo(0);
        RuleFor(x => x.QueriesAnswered).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Platform).MaximumLength(80);
    }
}
public class CreateSocialReportValidator : AbstractValidator<CreateSocialReportCommand>
{ public CreateSocialReportValidator() => RuleFor(x => x.Input).SetValidator(new SocialMediaInputValidator()); }
public class UpdateSocialReportValidator : AbstractValidator<UpdateSocialReportCommand>
{ public UpdateSocialReportValidator() => RuleFor(x => x.Input).SetValidator(new SocialMediaInputValidator()); }

public class SocialMediaHandlers :
    IRequestHandler<ListSocialReportsQuery, IReadOnlyList<SocialMediaReportDto>>,
    IRequestHandler<CreateSocialReportCommand, SocialMediaReportDto>,
    IRequestHandler<UpdateSocialReportCommand, SocialMediaReportDto>,
    IRequestHandler<DeleteSocialReportCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public SocialMediaHandlers(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    public async Task<IReadOnlyList<SocialMediaReportDto>> Handle(ListSocialReportsQuery request, CancellationToken ct)
    {
        HrAccess.EnsureHr(_user);
        var q = _db.SocialMediaReports.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            q = q.Where(r => (r.Platform != null && r.Platform.Contains(s)) || (r.Notes != null && r.Notes.Contains(s)));
        }
        var rows = await q.OrderByDescending(r => r.Date).ToListAsync(ct);
        var names = await EmployeeNamesAsync(rows.Select(r => r.EmployeeId), ct);
        return rows.Select(r => Map(r, NameOrNull(names, r.EmployeeId))).ToList();
    }

    public async Task<SocialMediaReportDto> Handle(CreateSocialReportCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        HrAccess.EnsureHr(_user);
        var r = new SocialMediaReport { AgencyId = _user.AgencyId ?? Guid.Empty };
        Apply(r, request.Input);
        _db.SocialMediaReports.Add(r);
        await _db.SaveChangesAsync(ct);
        return Map(r, null);
    }

    public async Task<SocialMediaReportDto> Handle(UpdateSocialReportCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        HrAccess.EnsureHr(_user);
        var r = await _db.SocialMediaReports.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(SocialMediaReport), request.Id);
        Apply(r, request.Input);
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Map(r, null);
    }

    public async Task<Unit> Handle(DeleteSocialReportCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        HrAccess.EnsureHr(_user);
        var r = await _db.SocialMediaReports.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(SocialMediaReport), request.Id);
        r.IsDeleted = true;
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    private static void Apply(SocialMediaReport r, SocialMediaInput i)
    {
        r.Date = i.Date.Date;
        r.EmployeeId = i.EmployeeId;
        r.Platform = string.IsNullOrWhiteSpace(i.Platform) ? null : i.Platform.Trim();
        r.PostsMade = i.PostsMade;
        r.QueriesAnswered = i.QueriesAnswered;
        r.Notes = string.IsNullOrWhiteSpace(i.Notes) ? null : i.Notes.Trim();
    }

    private async Task<Dictionary<Guid, string>> EmployeeNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(x => x is { } g && g != Guid.Empty).Select(x => x!.Value).Distinct().ToList();
        if (set.Count == 0) return new();
        return (await _db.Employees.AsNoTracking().Where(e => set.Contains(e.Id))
                .Select(e => new { e.Id, e.FullName }).ToListAsync(ct))
            .ToDictionary(e => e.Id, e => e.FullName);
    }

    private static string? NameOrNull(Dictionary<Guid, string> names, Guid? id) =>
        id is { } g && names.TryGetValue(g, out var n) ? n : null;

    private static SocialMediaReportDto Map(SocialMediaReport r, string? employeeName) => new(
        r.Id, r.Date, r.EmployeeId, employeeName, r.Platform, r.PostsMade, r.QueriesAnswered, r.Notes, r.CreatedAt);
}
