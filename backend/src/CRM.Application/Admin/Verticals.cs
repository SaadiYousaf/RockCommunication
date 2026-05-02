using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Admin;

public record VerticalDto(Guid Id, string Name, string? Description, bool IsActive);

public record ListVerticalsQuery() : IRequest<IReadOnlyList<VerticalDto>>;
public record CreateVerticalCommand(string Name, string? Description) : IRequest<VerticalDto>;
public record UpdateVerticalCommand(Guid Id, string Name, string? Description, bool IsActive) : IRequest<VerticalDto>;

public class CreateVerticalValidator : AbstractValidator<CreateVerticalCommand>
{
    public CreateVerticalValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
}

public class VerticalHandler :
    IRequestHandler<ListVerticalsQuery, IReadOnlyList<VerticalDto>>,
    IRequestHandler<CreateVerticalCommand, VerticalDto>,
    IRequestHandler<UpdateVerticalCommand, VerticalDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public VerticalHandler(IApplicationDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<VerticalDto>> Handle(ListVerticalsQuery request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        return await _db.Verticals
            .Where(v => v.AgencyId == _user.AgencyId)
            .OrderBy(v => v.Name)
            .Select(v => new VerticalDto(v.Id, v.Name, v.Description, v.IsActive))
            .ToListAsync(ct);
    }

    public async Task<VerticalDto> Handle(CreateVerticalCommand request, CancellationToken ct)
    {
        EnsureAdmin();
        var v = new Vertical
        {
            AgencyId = _user.AgencyId!.Value,
            Name = request.Name.Trim(),
            Description = request.Description
        };
        _db.Verticals.Add(v);
        await _db.SaveChangesAsync(ct);
        return new VerticalDto(v.Id, v.Name, v.Description, v.IsActive);
    }

    public async Task<VerticalDto> Handle(UpdateVerticalCommand request, CancellationToken ct)
    {
        EnsureAdmin();
        var v = await _db.Verticals.FirstOrDefaultAsync(
            x => x.Id == request.Id && x.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Vertical), request.Id);
        v.Name = request.Name.Trim();
        v.Description = request.Description;
        v.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        return new VerticalDto(v.Id, v.Name, v.Description, v.IsActive);
    }

    // Controller enforces [HasPermission]; backstop only verifies tenant context.
    private void EnsureAdmin()
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
    }
}
