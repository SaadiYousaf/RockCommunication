using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.CallCenter;

public record VoicemailAssetDto(Guid Id, string Name, string Url, int DurationSeconds, Guid? CampaignId, bool IsActive);
public record VoicemailDropDto(Guid Id, Guid LeadId, Guid AgentUserId, string Status, DateTime? CompletedAt);

public record ListVoicemailAssetsQuery() : IRequest<IReadOnlyList<VoicemailAssetDto>>;
public record UpsertVoicemailAssetCommand(Guid? Id, string Name, string Url, int DurationSeconds, Guid? CampaignId, bool IsActive)
    : IRequest<VoicemailAssetDto>;
public record DropVoicemailCommand(Guid LeadId, Guid VoicemailAssetId, Guid? CallRecordId) : IRequest<VoicemailDropDto>;

public class UpsertVoicemailAssetValidator : AbstractValidator<UpsertVoicemailAssetCommand>
{
    public UpsertVoicemailAssetValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Url).NotEmpty();
    }
}

public class VoicemailHandler :
    IRequestHandler<ListVoicemailAssetsQuery, IReadOnlyList<VoicemailAssetDto>>,
    IRequestHandler<UpsertVoicemailAssetCommand, VoicemailAssetDto>,
    IRequestHandler<DropVoicemailCommand, VoicemailDropDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDialerProvider _dialer;

    public VoicemailHandler(IApplicationDbContext db, ICurrentUser user, IDialerProvider dialer)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _dialer = Guard.AgainstNull(dialer);
    }

    public async Task<IReadOnlyList<VoicemailAssetDto>> Handle(ListVoicemailAssetsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        return await _db.VoicemailAssets
            .Where(v => v.AgencyId == _user.AgencyId && v.IsActive)
            .Select(v => new VoicemailAssetDto(v.Id, v.Name, v.Url, v.DurationSeconds, v.CampaignId, v.IsActive))
            .ToListAsync(ct);
    }

    public async Task<VoicemailAssetDto> Handle(UpsertVoicemailAssetCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        VoicemailAsset v;
        if (request.Id is { } id)
            v = await _db.VoicemailAssets.FirstOrDefaultAsync(x => x.Id == id && x.AgencyId == _user.AgencyId, ct)
                ?? throw new NotFoundException(nameof(VoicemailAsset), id);
        else { v = new VoicemailAsset { AgencyId = _user.AgencyId!.Value, CreatedByUserId = _user.UserId!.Value }; _db.VoicemailAssets.Add(v); }
        v.Name = request.Name.Trim();
        v.Url = request.Url.Trim();
        v.DurationSeconds = request.DurationSeconds;
        v.CampaignId = request.CampaignId;
        v.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        return new VoicemailAssetDto(v.Id, v.Name, v.Url, v.DurationSeconds, v.CampaignId, v.IsActive);
    }

    public async Task<VoicemailDropDto> Handle(DropVoicemailCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        var asset = await _db.VoicemailAssets.FirstOrDefaultAsync(
            v => v.Id == request.VoicemailAssetId && v.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(VoicemailAsset), request.VoicemailAssetId);

        var drop = new VoicemailDrop
        {
            AgencyId = asset.AgencyId,
            VoicemailAssetId = asset.Id,
            LeadId = request.LeadId,
            AgentUserId = _user.UserId.Value,
            CallRecordId = request.CallRecordId,
            Status = "Queued"
        };
        _db.VoicemailDrops.Add(drop);
        await _db.SaveChangesAsync(ct);
        return new VoicemailDropDto(drop.Id, drop.LeadId, drop.AgentUserId, drop.Status, drop.CompletedAt);
    }

    private void EnsureManager()
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains("Admin") && !_user.Roles.Contains("ProgramManager"))
            throw new ForbiddenAccessException();
    }
}
