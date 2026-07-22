using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

public record InboundQueueDto(Guid Id, string Name, string? PhoneNumber, string? RequiredSkillCode,
    Guid? CampaignId, string Strategy, int MaxWaitSeconds, Guid? OverflowQueueId, Guid? VoicemailAssetId, bool IsActive);

public record IvrOptionDto(Guid Id, string DigitOrSpeech, string Label, string ActionType, string? ActionTargetId, int Order);
public record IvrMenuDto(Guid Id, Guid InboundQueueId, string Name, string Greeting, string? GreetingAudioUrl, IReadOnlyList<IvrOptionDto> Options);

public record ListInboundQueuesQuery() : IRequest<IReadOnlyList<InboundQueueDto>>;

public record UpsertInboundQueueCommand(Guid? Id, string Name, string? PhoneNumber, string? RequiredSkillCode,
    Guid? CampaignId, string Strategy, int MaxWaitSeconds, Guid? OverflowQueueId, Guid? VoicemailAssetId, bool IsActive)
    : IRequest<InboundQueueDto>;

public record GetIvrMenuQuery(Guid InboundQueueId) : IRequest<IvrMenuDto?>;
public record UpsertIvrMenuCommand(Guid InboundQueueId, string Name, string Greeting, string? GreetingAudioUrl,
    IReadOnlyList<UpsertIvrOptionDto> Options) : IRequest<IvrMenuDto>;
public record UpsertIvrOptionDto(string DigitOrSpeech, string Label, string ActionType, string? ActionTargetId, int Order);

public class UpsertInboundQueueValidator : AbstractValidator<UpsertInboundQueueCommand>
{
    public UpsertInboundQueueValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.MaxWaitSeconds).GreaterThan(0);
    }
}

public class InboundQueueHandler :
    IRequestHandler<ListInboundQueuesQuery, IReadOnlyList<InboundQueueDto>>,
    IRequestHandler<UpsertInboundQueueCommand, InboundQueueDto>,
    IRequestHandler<GetIvrMenuQuery, IvrMenuDto?>,
    IRequestHandler<UpsertIvrMenuCommand, IvrMenuDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public InboundQueueHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<InboundQueueDto>> Handle(ListInboundQueuesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        return await _db.InboundQueues.Where(q => q.AgencyId == _user.AgencyId)
            .OrderBy(q => q.Name)
            .Select(q => new InboundQueueDto(q.Id, q.Name, q.PhoneNumber, q.RequiredSkillCode, q.CampaignId,
                q.Strategy, q.MaxWaitSeconds, q.OverflowQueueId, q.VoicemailAssetId, q.IsActive))
            .ToListAsync(ct);
    }

    public async Task<InboundQueueDto> Handle(UpsertInboundQueueCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        InboundQueue q;
        if (request.Id is { } id)
            q = await _db.InboundQueues.FirstOrDefaultAsync(x => x.Id == id && x.AgencyId == _user.AgencyId, ct)
                ?? throw new NotFoundException(nameof(InboundQueue), id);
        else { q = new InboundQueue { AgencyId = _user.AgencyId!.Value }; _db.InboundQueues.Add(q); }
        q.Name = request.Name.Trim();
        q.PhoneNumber = request.PhoneNumber;
        q.RequiredSkillCode = request.RequiredSkillCode;
        q.CampaignId = request.CampaignId;
        q.Strategy = request.Strategy;
        q.MaxWaitSeconds = request.MaxWaitSeconds;
        q.OverflowQueueId = request.OverflowQueueId;
        q.VoicemailAssetId = request.VoicemailAssetId;
        q.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        return new InboundQueueDto(q.Id, q.Name, q.PhoneNumber, q.RequiredSkillCode, q.CampaignId,
            q.Strategy, q.MaxWaitSeconds, q.OverflowQueueId, q.VoicemailAssetId, q.IsActive);
    }

    public async Task<IvrMenuDto?> Handle(GetIvrMenuQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var menu = await _db.IvrMenus.Include(m => m.Options)
            .FirstOrDefaultAsync(m => m.InboundQueueId == request.InboundQueueId && m.AgencyId == _user.AgencyId, ct);
        return menu is null ? null : MapMenu(menu);
    }

    public async Task<IvrMenuDto> Handle(UpsertIvrMenuCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        var menu = await _db.IvrMenus.Include(m => m.Options)
            .FirstOrDefaultAsync(m => m.InboundQueueId == request.InboundQueueId && m.AgencyId == _user.AgencyId, ct);
        if (menu is null)
        {
            menu = new IvrMenu { AgencyId = _user.AgencyId!.Value, InboundQueueId = request.InboundQueueId };
            _db.IvrMenus.Add(menu);
        }
        else
        {
            _db.IvrOptions.RemoveRange(menu.Options);
            menu.Options.Clear();
        }
        menu.Name = request.Name;
        menu.Greeting = request.Greeting;
        menu.GreetingAudioUrl = request.GreetingAudioUrl;

        foreach (var o in request.Options.OrderBy(x => x.Order))
        {
            menu.Options.Add(new IvrOption
            {
                AgencyId = menu.AgencyId,
                DigitOrSpeech = o.DigitOrSpeech,
                Label = o.Label,
                ActionType = o.ActionType,
                ActionTargetId = o.ActionTargetId,
                Order = o.Order
            });
        }
        await _db.SaveChangesAsync(ct);
        return MapMenu(menu);
    }

    private void EnsureManager()
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains("Admin") && !_user.Roles.Contains("ProgramManager"))
            throw new ForbiddenAccessException();
    }

    private static IvrMenuDto MapMenu(IvrMenu m) => new(m.Id, m.InboundQueueId, m.Name, m.Greeting, m.GreetingAudioUrl,
        m.Options.OrderBy(o => o.Order).Select(o => new IvrOptionDto(o.Id, o.DigitOrSpeech, o.Label, o.ActionType, o.ActionTargetId, o.Order)).ToList());
}
