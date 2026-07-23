using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
// Disambiguate from the CRM.Application.CallCenter (telephony) namespace.
using CcEntity = CRM.Domain.Entities.CallCenter;

namespace CRM.Application.Admin;

public record CallCenterDto(Guid Id, string Name, string? Code, bool IsActive, int LeadCount);

public record ListCallCentersQuery() : IRequest<IReadOnlyList<CallCenterDto>>;
public record CreateCallCenterCommand(string Name, string? Code) : IRequest<CallCenterDto>;
public record UpdateCallCenterCommand(Guid Id, string Name, string? Code, bool IsActive) : IRequest<CallCenterDto>;

public class CreateCallCenterValidator : AbstractValidator<CreateCallCenterCommand>
{
    public CreateCallCenterValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public class UpdateCallCenterValidator : AbstractValidator<UpdateCallCenterCommand>
{
    public UpdateCallCenterValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

/// <summary>
/// CRUD for the call centers within the caller's agency. CallCenter is a <see cref="TenantEntity"/>,
/// so the global query filter already scopes every read to the caller's agency; these handlers add
/// tenant-context guards as a backstop. Only agency-level roles reach here (controller [HasPermission]).
/// </summary>
public class CallCenterHandler :
    IRequestHandler<ListCallCentersQuery, IReadOnlyList<CallCenterDto>>,
    IRequestHandler<CreateCallCenterCommand, CallCenterDto>,
    IRequestHandler<UpdateCallCenterCommand, CallCenterDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public CallCenterHandler(IApplicationDbContext db, ICurrentUser user)
    { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<CallCenterDto>> Handle(ListCallCentersQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        return await _db.CallCenters
            .OrderBy(c => c.Name)
            .Select(c => new CallCenterDto(
                c.Id, c.Name, c.Code, c.IsActive,
                _db.Leads.Count(l => l.CallCenterId == c.Id)))
            .ToListAsync(ct);
    }

    public async Task<CallCenterDto> Handle(CreateCallCenterCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var name = request.Name.Trim();
        if (await _db.CallCenters.AnyAsync(c => c.Name == name, ct))
            throw new ConflictException($"A call center named \"{name}\" already exists.");

        var cc = new CcEntity
        {
            AgencyId = _user.AgencyId.Value,
            Name = name,
            Code = request.Code?.Trim(),
            IsActive = true
        };
        _db.CallCenters.Add(cc);
        await _db.SaveChangesAsync(ct);
        return new CallCenterDto(cc.Id, cc.Name, cc.Code, cc.IsActive, 0);
    }

    public async Task<CallCenterDto> Handle(UpdateCallCenterCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var cc = await _db.CallCenters.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException("CallCenter", request.Id);
        cc.Name = request.Name.Trim();
        cc.Code = request.Code?.Trim();
        cc.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        var leads = await _db.Leads.CountAsync(l => l.CallCenterId == cc.Id, ct);
        return new CallCenterDto(cc.Id, cc.Name, cc.Code, cc.IsActive, leads);
    }
}
