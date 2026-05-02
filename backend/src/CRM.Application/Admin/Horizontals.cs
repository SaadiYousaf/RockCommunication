using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Admin;

public record HorizontalDto(Guid Id, string Name, string? Description, bool IsActive);

public record ListHorizontalsQuery() : IRequest<IReadOnlyList<HorizontalDto>>;
public record CreateHorizontalCommand(string Name, string? Description) : IRequest<HorizontalDto>;
public record UpdateHorizontalCommand(Guid Id, string Name, string? Description, bool IsActive) : IRequest<HorizontalDto>;

public class CreateHorizontalValidator : AbstractValidator<CreateHorizontalCommand>
{
    public CreateHorizontalValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
}

public class HorizontalHandler :
    IRequestHandler<ListHorizontalsQuery, IReadOnlyList<HorizontalDto>>,
    IRequestHandler<CreateHorizontalCommand, HorizontalDto>,
    IRequestHandler<UpdateHorizontalCommand, HorizontalDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public HorizontalHandler(IApplicationDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<HorizontalDto>> Handle(ListHorizontalsQuery request, CancellationToken ct)
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        return await _db.Horizontals
            .Where(v => v.AgencyId == _user.AgencyId)
            .OrderBy(v => v.Name)
            .Select(v => new HorizontalDto(v.Id, v.Name, v.Description, v.IsActive))
            .ToListAsync(ct);
    }

    public async Task<HorizontalDto> Handle(CreateHorizontalCommand request, CancellationToken ct)
    {
        EnsureAdmin();
        var v = new Horizontal
        {
            AgencyId = _user.AgencyId!.Value,
            Name = request.Name.Trim(),
            Description = request.Description
        };
        _db.Horizontals.Add(v);
        await _db.SaveChangesAsync(ct);
        return new HorizontalDto(v.Id, v.Name, v.Description, v.IsActive);
    }

    public async Task<HorizontalDto> Handle(UpdateHorizontalCommand request, CancellationToken ct)
    {
        EnsureAdmin();
        var v = await _db.Horizontals.FirstOrDefaultAsync(
            x => x.Id == request.Id && x.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Horizontal), request.Id);
        v.Name = request.Name.Trim();
        v.Description = request.Description;
        v.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        return new HorizontalDto(v.Id, v.Name, v.Description, v.IsActive);
    }

    // Controller enforces [HasPermission]; backstop only verifies tenant context.
    private void EnsureAdmin()
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
    }
}
