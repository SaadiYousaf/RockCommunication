using CRM.Application.Common.Compliance;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

// ----- Wrap-up codes -----

public record WrapUpCodeDto(Guid Id, string Code, string Label, bool IsSale, bool IsContact, bool IsRetry, bool IsActive);
public record ListWrapUpCodesQuery() : IRequest<IReadOnlyList<WrapUpCodeDto>>;
public record UpsertWrapUpCodeCommand(Guid? Id, string Code, string Label, bool IsSale, bool IsContact, bool IsRetry, bool IsActive)
    : IRequest<WrapUpCodeDto>;

public class UpsertWrapUpCodeValidator : AbstractValidator<UpsertWrapUpCodeCommand>
{
    public UpsertWrapUpCodeValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(120);
    }
}

public class WrapUpCodeHandler :
    IRequestHandler<ListWrapUpCodesQuery, IReadOnlyList<WrapUpCodeDto>>,
    IRequestHandler<UpsertWrapUpCodeCommand, WrapUpCodeDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public WrapUpCodeHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<WrapUpCodeDto>> Handle(ListWrapUpCodesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        return await _db.WrapUpCodes
            .Where(w => w.AgencyId == _user.AgencyId)
            .OrderBy(w => w.Code)
            .Select(w => new WrapUpCodeDto(w.Id, w.Code, w.Label, w.IsSale, w.IsContact, w.IsRetry, w.IsActive))
            .ToListAsync(ct);
    }

    public async Task<WrapUpCodeDto> Handle(UpsertWrapUpCodeCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        WrapUpCode entry;
        if (request.Id is { } id)
        {
            entry = await _db.WrapUpCodes.FirstOrDefaultAsync(w => w.Id == id && w.AgencyId == _user.AgencyId, ct)
                ?? throw new NotFoundException(nameof(WrapUpCode), id);
        }
        else
        {
            entry = new WrapUpCode { AgencyId = _user.AgencyId!.Value };
            _db.WrapUpCodes.Add(entry);
        }
        entry.Code = request.Code.Trim();
        entry.Label = request.Label.Trim();
        entry.IsSale = request.IsSale;
        entry.IsContact = request.IsContact;
        entry.IsRetry = request.IsRetry;
        entry.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        return new WrapUpCodeDto(entry.Id, entry.Code, entry.Label, entry.IsSale, entry.IsContact, entry.IsRetry, entry.IsActive);
    }

    // Permission enforcement is at the controller via [HasPermission].
    private static void EnsureAdmin() { }
}

// ----- DNC -----

public record DncDto(Guid Id, string PhoneNormalized, string? Reason, string Source, DateTime? ExpiresAt);
public record ListDncQuery(int Skip = 0, int Take = 100) : IRequest<IReadOnlyList<DncDto>>;
public record AddDncCommand(string Phone, string? Reason, string Source = "Internal", DateTime? ExpiresAt = null) : IRequest<DncDto>;
public record RemoveDncCommand(Guid Id) : IRequest<Unit>;

public class DncHandler :
    IRequestHandler<ListDncQuery, IReadOnlyList<DncDto>>,
    IRequestHandler<AddDncCommand, DncDto>,
    IRequestHandler<RemoveDncCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPhoneNormalizer _normalizer;

    public DncHandler(IApplicationDbContext db, ICurrentUser user, IPhoneNormalizer normalizer)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _normalizer = Guard.AgainstNull(normalizer);
    }

    public async Task<IReadOnlyList<DncDto>> Handle(ListDncQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        return await _db.DncEntries
            .Where(d => d.AgencyId == _user.AgencyId)
            .OrderBy(d => d.PhoneNormalized)
            .Skip(request.Skip).Take(Math.Min(request.Take, 500))
            .Select(d => new DncDto(d.Id, d.PhoneNormalized, d.Reason, d.Source, d.ExpiresAt))
            .ToListAsync(ct);
    }

    public async Task<DncDto> Handle(AddDncCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        var norm = _normalizer.Normalize(request.Phone);
        if (string.IsNullOrEmpty(norm)) throw new ConflictException("Invalid phone number.");

        var existing = await _db.DncEntries.FirstOrDefaultAsync(
            d => d.AgencyId == _user.AgencyId && d.PhoneNormalized == norm, ct);
        if (existing is not null)
            return new DncDto(existing.Id, existing.PhoneNormalized, existing.Reason, existing.Source, existing.ExpiresAt);

        var entry = new DncEntry
        {
            AgencyId = _user.AgencyId!.Value,
            PhoneNormalized = norm,
            Reason = request.Reason,
            Source = request.Source,
            ExpiresAt = request.ExpiresAt
        };
        _db.DncEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return new DncDto(entry.Id, entry.PhoneNormalized, entry.Reason, entry.Source, entry.ExpiresAt);
    }

    public async Task<Unit> Handle(RemoveDncCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        var entry = await _db.DncEntries.FirstOrDefaultAsync(
            d => d.Id == request.Id && d.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(DncEntry), request.Id);
        _db.DncEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    // Permission enforcement is at the controller via [HasPermission].
    private static void EnsureAdmin() { }
}

// ----- Campaigns -----

public record CampaignDto(Guid Id, string Code, string Name, Guid? VerticalId, bool IsActive, DateTime? StartsAt, DateTime? EndsAt);
public record ListCampaignsQuery() : IRequest<IReadOnlyList<CampaignDto>>;
public record UpsertCampaignCommand(Guid? Id, string Code, string Name, Guid? VerticalId, bool IsActive, DateTime? StartsAt, DateTime? EndsAt)
    : IRequest<CampaignDto>;

public class UpsertCampaignValidator : AbstractValidator<UpsertCampaignCommand>
{
    public UpsertCampaignValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class CampaignHandler :
    IRequestHandler<ListCampaignsQuery, IReadOnlyList<CampaignDto>>,
    IRequestHandler<UpsertCampaignCommand, CampaignDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public CampaignHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<CampaignDto>> Handle(ListCampaignsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        return await _db.Campaigns.Where(c => c.AgencyId == _user.AgencyId)
            .OrderBy(c => c.Code)
            .Select(c => new CampaignDto(c.Id, c.Code, c.Name, c.VerticalId, c.IsActive, c.StartsAt, c.EndsAt))
            .ToListAsync(ct);
    }

    public async Task<CampaignDto> Handle(UpsertCampaignCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        Campaign c;
        if (request.Id is { } id)
        {
            c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id && x.AgencyId == _user.AgencyId, ct)
                ?? throw new NotFoundException(nameof(Campaign), id);
        }
        else
        {
            c = new Campaign { AgencyId = _user.AgencyId!.Value };
            _db.Campaigns.Add(c);
        }
        c.Code = request.Code.Trim();
        c.Name = request.Name.Trim();
        c.VerticalId = request.VerticalId;
        c.IsActive = request.IsActive;
        c.StartsAt = request.StartsAt;
        c.EndsAt = request.EndsAt;
        await _db.SaveChangesAsync(ct);
        return new CampaignDto(c.Id, c.Code, c.Name, c.VerticalId, c.IsActive, c.StartsAt, c.EndsAt);
    }

    // Permission enforcement is at the controller via [HasPermission].
    private static void EnsureAdmin() { }
}

// ----- Lead Sources -----

public record LeadSourceDto(Guid Id, string Code, string Name, Guid? CampaignId, decimal CostPerLead, bool IsActive);
public record ListLeadSourcesQuery() : IRequest<IReadOnlyList<LeadSourceDto>>;
public record UpsertLeadSourceCommand(Guid? Id, string Code, string Name, Guid? CampaignId, decimal CostPerLead, bool IsActive)
    : IRequest<LeadSourceDto>;

public class LeadSourceHandler :
    IRequestHandler<ListLeadSourcesQuery, IReadOnlyList<LeadSourceDto>>,
    IRequestHandler<UpsertLeadSourceCommand, LeadSourceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public LeadSourceHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<LeadSourceDto>> Handle(ListLeadSourcesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        return await _db.LeadSources.Where(s => s.AgencyId == _user.AgencyId)
            .OrderBy(s => s.Code)
            .Select(s => new LeadSourceDto(s.Id, s.Code, s.Name, s.CampaignId, s.CostPerLead, s.IsActive))
            .ToListAsync(ct);
    }

    public async Task<LeadSourceDto> Handle(UpsertLeadSourceCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        LeadSource s;
        if (request.Id is { } id)
        {
            s = await _db.LeadSources.FirstOrDefaultAsync(x => x.Id == id && x.AgencyId == _user.AgencyId, ct)
                ?? throw new NotFoundException(nameof(LeadSource), id);
        }
        else
        {
            s = new LeadSource { AgencyId = _user.AgencyId!.Value };
            _db.LeadSources.Add(s);
        }
        s.Code = request.Code.Trim();
        s.Name = request.Name.Trim();
        s.CampaignId = request.CampaignId;
        s.CostPerLead = request.CostPerLead;
        s.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        return new LeadSourceDto(s.Id, s.Code, s.Name, s.CampaignId, s.CostPerLead, s.IsActive);
    }

    // Permission enforcement is at the controller via [HasPermission].
    private static void EnsureAdmin() { }
}

// ----- Skills -----

public record SkillDto(Guid Id, string Code, string Name, bool IsActive);
public record ListSkillsQuery() : IRequest<IReadOnlyList<SkillDto>>;
public record UpsertSkillCommand(Guid? Id, string Code, string Name, bool IsActive) : IRequest<SkillDto>;
public record AssignAgentSkillCommand(Guid UserId, Guid SkillId, int Proficiency) : IRequest<Unit>;
public record RemoveAgentSkillCommand(Guid UserId, Guid SkillId) : IRequest<Unit>;
public record GetAgentSkillsQuery(Guid UserId) : IRequest<IReadOnlyList<AgentSkillDto>>;
public record AgentSkillDto(Guid SkillId, string Code, string Name, int Proficiency);

public class SkillHandler :
    IRequestHandler<ListSkillsQuery, IReadOnlyList<SkillDto>>,
    IRequestHandler<UpsertSkillCommand, SkillDto>,
    IRequestHandler<AssignAgentSkillCommand, Unit>,
    IRequestHandler<RemoveAgentSkillCommand, Unit>,
    IRequestHandler<GetAgentSkillsQuery, IReadOnlyList<AgentSkillDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public SkillHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<SkillDto>> Handle(ListSkillsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        return await _db.Skills.Where(s => s.AgencyId == _user.AgencyId)
            .OrderBy(s => s.Code)
            .Select(s => new SkillDto(s.Id, s.Code, s.Name, s.IsActive))
            .ToListAsync(ct);
    }

    public async Task<SkillDto> Handle(UpsertSkillCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        Skill s;
        if (request.Id is { } id)
            s = await _db.Skills.FirstOrDefaultAsync(x => x.Id == id && x.AgencyId == _user.AgencyId, ct)
                ?? throw new NotFoundException(nameof(Skill), id);
        else
        {
            s = new Skill { AgencyId = _user.AgencyId!.Value };
            _db.Skills.Add(s);
        }
        s.Code = request.Code.Trim();
        s.Name = request.Name.Trim();
        s.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        return new SkillDto(s.Id, s.Code, s.Name, s.IsActive);
    }

    public async Task<Unit> Handle(AssignAgentSkillCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        var existing = await _db.AgentSkills.FirstOrDefaultAsync(
            a => a.UserId == request.UserId && a.SkillId == request.SkillId, ct);
        if (existing is null)
            _db.AgentSkills.Add(new AgentSkill
            {
                AgencyId = _user.AgencyId!.Value,
                UserId = request.UserId,
                SkillId = request.SkillId,
                Proficiency = request.Proficiency
            });
        else
            existing.Proficiency = request.Proficiency;
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    public async Task<Unit> Handle(RemoveAgentSkillCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        var existing = await _db.AgentSkills.FirstOrDefaultAsync(
            a => a.UserId == request.UserId && a.SkillId == request.SkillId, ct);
        if (existing is not null)
        {
            _db.AgentSkills.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
        return Unit.Value;
    }

    public async Task<IReadOnlyList<AgentSkillDto>> Handle(GetAgentSkillsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        return await (from a in _db.AgentSkills
                      join s in _db.Skills on a.SkillId equals s.Id
                      where a.UserId == request.UserId && s.AgencyId == _user.AgencyId
                      select new AgentSkillDto(s.Id, s.Code, s.Name, a.Proficiency))
                      .ToListAsync(ct);
    }

    // Permission enforcement is at the controller via [HasPermission].
    private static void EnsureAdmin() { }
}

// ----- Scripts -----

public record ScriptDto(Guid Id, string Name, WorkflowStage? Stage, string? Role, Guid? CampaignId, string Body, bool IsActive, int Version);
public record ListScriptsQuery(WorkflowStage? Stage, string? Role, Guid? CampaignId) : IRequest<IReadOnlyList<ScriptDto>>;
public record UpsertScriptCommand(Guid? Id, string Name, WorkflowStage? Stage, string? Role, Guid? CampaignId, string Body, bool IsActive)
    : IRequest<ScriptDto>;

public class ScriptHandler :
    IRequestHandler<ListScriptsQuery, IReadOnlyList<ScriptDto>>,
    IRequestHandler<UpsertScriptCommand, ScriptDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public ScriptHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<ScriptDto>> Handle(ListScriptsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var q = _db.Scripts.Where(s => s.AgencyId == _user.AgencyId && s.IsActive);
        if (request.Stage is { } st) q = q.Where(s => s.Stage == st);
        if (!string.IsNullOrEmpty(request.Role)) q = q.Where(s => s.Role == request.Role);
        if (request.CampaignId is { } cid) q = q.Where(s => s.CampaignId == cid);
        return await q.OrderByDescending(s => s.Version)
            .Select(s => new ScriptDto(s.Id, s.Name, s.Stage, s.Role, s.CampaignId, s.Body, s.IsActive, s.Version))
            .ToListAsync(ct);
    }

    public async Task<ScriptDto> Handle(UpsertScriptCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        Script s;
        if (request.Id is { } id)
            s = await _db.Scripts.FirstOrDefaultAsync(x => x.Id == id && x.AgencyId == _user.AgencyId, ct)
                ?? throw new NotFoundException(nameof(Script), id);
        else
        {
            var maxVersion = await _db.Scripts.Where(x => x.AgencyId == _user.AgencyId && x.Name == request.Name)
                .Select(x => (int?)x.Version).MaxAsync(ct) ?? 0;
            s = new Script { AgencyId = _user.AgencyId!.Value, Version = maxVersion + 1 };
            _db.Scripts.Add(s);
        }
        s.Name = request.Name.Trim();
        s.Stage = request.Stage;
        s.Role = request.Role;
        s.CampaignId = request.CampaignId;
        s.Body = request.Body;
        s.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        return new ScriptDto(s.Id, s.Name, s.Stage, s.Role, s.CampaignId, s.Body, s.IsActive, s.Version);
    }

    // Permission enforcement is at the controller via [HasPermission].
    private static void EnsureAdmin() { }
}
