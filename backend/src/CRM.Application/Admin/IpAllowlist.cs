using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Admin;

public record IpAllowlistDto(Guid Id, string CidrOrIp, string? Note);

public record ListIpAllowlistQuery() : IRequest<IReadOnlyList<IpAllowlistDto>>;
public record AddIpAllowlistCommand(string CidrOrIp, string? Note) : IRequest<IpAllowlistDto>;
public record RemoveIpAllowlistCommand(Guid Id) : IRequest<Unit>;

public class AddIpAllowlistValidator : AbstractValidator<AddIpAllowlistCommand>
{
    public AddIpAllowlistValidator()
    {
        RuleFor(x => x.CidrOrIp).NotEmpty().Matches(@"^[0-9a-fA-F\.:]+(/\d+)?$");
    }
}

public class IpAllowlistHandler :
    IRequestHandler<ListIpAllowlistQuery, IReadOnlyList<IpAllowlistDto>>,
    IRequestHandler<AddIpAllowlistCommand, IpAllowlistDto>,
    IRequestHandler<RemoveIpAllowlistCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public IpAllowlistHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<IpAllowlistDto>> Handle(ListIpAllowlistQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        return await _db.IpAllowlist
            .Where(e => e.AgencyId == _user.AgencyId)
            .OrderBy(e => e.CidrOrIp)
            .Select(e => new IpAllowlistDto(e.Id, e.CidrOrIp, e.Note))
            .ToListAsync(ct);
    }

    public async Task<IpAllowlistDto> Handle(AddIpAllowlistCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        var entry = new IpAllowlistEntry
        {
            AgencyId = _user.AgencyId!.Value,
            CidrOrIp = request.CidrOrIp.Trim(),
            Note = request.Note
        };
        _db.IpAllowlist.Add(entry);
        await _db.SaveChangesAsync(ct);
        return new IpAllowlistDto(entry.Id, entry.CidrOrIp, entry.Note);
    }

    public async Task<Unit> Handle(RemoveIpAllowlistCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        var entry = await _db.IpAllowlist.FirstOrDefaultAsync(
            e => e.Id == request.Id && e.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(IpAllowlistEntry), request.Id);
        _db.IpAllowlist.Remove(entry);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    // Controller enforces [HasPermission]; backstop only verifies tenant context.
    private void EnsureAdmin()
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
    }
}
