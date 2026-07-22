using CRM.Application.Common.Compliance;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Scoring;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Lists;

public record LeadListDto(Guid Id, string Name, Guid? CampaignId, Guid? LeadSourceId, bool IsActive, int LeadCount);
public record ImportBatchDto(Guid Id, Guid LeadListId, string FileName, int TotalRows, int Imported, int Duplicates, int DncScrubbed, int Errors, string Status, DateTime? CompletedAt);

public record CsvLeadRow(string FirstName, string LastName, string PhoneNumber, string? Email,
    string? State, string? PostalCode, string? Source, string? JornayaLeadId);

public record ListLeadListsQuery() : IRequest<IReadOnlyList<LeadListDto>>;
public record UpsertLeadListCommand(Guid? Id, string Name, Guid? CampaignId, Guid? LeadSourceId, bool IsActive)
    : IRequest<LeadListDto>;
public record ImportLeadsCommand(Guid LeadListId, IReadOnlyList<CsvLeadRow> Rows, string FileName) : IRequest<ImportBatchDto>;
public record ListImportBatchesQuery(Guid LeadListId, int Take = 20) : IRequest<IReadOnlyList<ImportBatchDto>>;

public class UpsertLeadListValidator : AbstractValidator<UpsertLeadListCommand>
{
    public UpsertLeadListValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public class LeadListHandler :
    IRequestHandler<ListLeadListsQuery, IReadOnlyList<LeadListDto>>,
    IRequestHandler<UpsertLeadListCommand, LeadListDto>,
    IRequestHandler<ImportLeadsCommand, ImportBatchDto>,
    IRequestHandler<ListImportBatchesQuery, IReadOnlyList<ImportBatchDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPhoneNormalizer _phone;
    private readonly IDncChecker _dnc;
    private readonly ILeadScorer _scorer;

    public LeadListHandler(IApplicationDbContext db, ICurrentUser user, IPhoneNormalizer phone, IDncChecker dnc, ILeadScorer scorer)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _phone = Guard.AgainstNull(phone); _dnc = Guard.AgainstNull(dnc); _scorer = Guard.AgainstNull(scorer);
    }

    public async Task<IReadOnlyList<LeadListDto>> Handle(ListLeadListsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        return await _db.LeadLists.Where(l => l.AgencyId == _user.AgencyId)
            .OrderBy(l => l.Name)
            .Select(l => new LeadListDto(l.Id, l.Name, l.CampaignId, l.LeadSourceId, l.IsActive, l.LeadCount))
            .ToListAsync(ct);
    }

    public async Task<LeadListDto> Handle(UpsertLeadListCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        LeadList list;
        if (request.Id is { } id)
            list = await _db.LeadLists.FirstOrDefaultAsync(x => x.Id == id && x.AgencyId == _user.AgencyId, ct)
                ?? throw new NotFoundException(nameof(LeadList), id);
        else { list = new LeadList { AgencyId = _user.AgencyId!.Value }; _db.LeadLists.Add(list); }
        list.Name = request.Name.Trim();
        list.CampaignId = request.CampaignId;
        list.LeadSourceId = request.LeadSourceId;
        list.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        return new LeadListDto(list.Id, list.Name, list.CampaignId, list.LeadSourceId, list.IsActive, list.LeadCount);
    }

    public async Task<ImportBatchDto> Handle(ImportLeadsCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        var list = await _db.LeadLists.FirstOrDefaultAsync(l => l.Id == request.LeadListId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(LeadList), request.LeadListId);

        var batch = new LeadImportBatch
        {
            AgencyId = list.AgencyId,
            LeadListId = list.Id,
            FileName = request.FileName,
            TotalRows = request.Rows.Count,
            InitiatedByUserId = _user.UserId!.Value,
            Status = "Running"
        };
        _db.LeadImportBatches.Add(batch);
        await _db.SaveChangesAsync(ct);

        var existingPhones = await _db.Leads.AsNoTracking()
            .Where(l => l.AgencyId == list.AgencyId)
            .Select(l => l.PhoneNumber).ToListAsync(ct);
        var existingSet = existingPhones.ToHashSet();

        foreach (var r in request.Rows)
        {
            try
            {
                var normalized = _phone.Normalize(r.PhoneNumber);
                if (string.IsNullOrEmpty(normalized) || normalized.Length < 10) { batch.Errors++; continue; }
                if (existingSet.Contains(r.PhoneNumber) || existingSet.Contains(normalized)) { batch.Duplicates++; continue; }
                if (await _dnc.IsBlockedAsync(list.AgencyId, normalized, ct)) { batch.DncScrubbed++; continue; }

                var lead = new Lead
                {
                    AgencyId = list.AgencyId,
                    FirstName = r.FirstName.Trim(),
                    LastName = r.LastName.Trim(),
                    PhoneNumber = normalized,
                    Email = r.Email?.Trim(),
                    State = r.State,
                    PostalCode = r.PostalCode,
                    Source = r.Source,
                    JornayaLeadId = r.JornayaLeadId,
                    CampaignId = list.CampaignId,
                    LeadSourceId = list.LeadSourceId,
                    Stage = Domain.Enums.WorkflowStage.New
                };
                _db.Leads.Add(lead);
                await _db.SaveChangesAsync(ct);

                var score = await _scorer.ScoreAsync(lead, ct);
                lead.Score = score.Score;

                _db.LeadListMemberships.Add(new LeadListMembership
                {
                    AgencyId = list.AgencyId, LeadListId = list.Id, LeadId = lead.Id
                });
                existingSet.Add(normalized);
                batch.Imported++;
            }
            catch
            {
                batch.Errors++;
            }
        }

        list.LeadCount += batch.Imported;
        batch.Status = "Completed";
        batch.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Map(batch);
    }

    public async Task<IReadOnlyList<ImportBatchDto>> Handle(ListImportBatchesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        return await _db.LeadImportBatches
            .Where(b => b.AgencyId == _user.AgencyId && b.LeadListId == request.LeadListId)
            .OrderByDescending(b => b.CreatedAt).Take(request.Take)
            .Select(b => Map(b))
            .ToListAsync(ct);
    }

    private void EnsureManager()
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains("Admin") && !_user.Roles.Contains("ProgramManager") && !_user.Roles.Contains("TeamLead"))
            throw new ForbiddenAccessException();
    }

    private static ImportBatchDto Map(LeadImportBatch b) =>
        new(b.Id, b.LeadListId, b.FileName, b.TotalRows, b.Imported, b.Duplicates, b.DncScrubbed, b.Errors, b.Status, b.CompletedAt);
}
