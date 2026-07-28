using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Confidential;

/// <summary>A stored portal login. Password is returned decrypted — the whole point of the
/// vault is to retrieve it — so every endpoint is gated to Admin / SuperAdmin.</summary>
public record PortalCredentialDto(
    Guid Id, string PortalName, string? Url, string Username, string Password, string? Notes,
    DateTime CreatedAt, DateTime? UpdatedAt);

public record ListPortalCredentialsQuery : IRequest<IReadOnlyList<PortalCredentialDto>>;

public record CreatePortalCredentialCommand(
    string PortalName, string? Url, string Username, string Password, string? Notes) : IRequest<PortalCredentialDto>;

public record UpdatePortalCredentialCommand(
    Guid Id, string PortalName, string? Url, string Username, string Password, string? Notes) : IRequest<PortalCredentialDto>;

public record DeletePortalCredentialCommand(Guid Id) : IRequest<Unit>;

public class CreatePortalCredentialValidator : AbstractValidator<CreatePortalCredentialCommand>
{
    public CreatePortalCredentialValidator()
    {
        RuleFor(x => x.PortalName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Username).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.Url).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class UpdatePortalCredentialValidator : AbstractValidator<UpdatePortalCredentialCommand>
{
    public UpdatePortalCredentialValidator()
    {
        RuleFor(x => x.PortalName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Username).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.Url).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class PortalCredentialHandlers :
    IRequestHandler<ListPortalCredentialsQuery, IReadOnlyList<PortalCredentialDto>>,
    IRequestHandler<CreatePortalCredentialCommand, PortalCredentialDto>,
    IRequestHandler<UpdatePortalCredentialCommand, PortalCredentialDto>,
    IRequestHandler<DeletePortalCredentialCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public PortalCredentialHandlers(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    // Admin (their agency) or SuperAdmin (all agencies, via the query-filter bypass) only.
    private void EnsureAdmin()
    {
        if (_user.IsSuperAdmin || _user.Roles.Contains(DomainRoles.Admin)) return;
        throw new ForbiddenAccessException();
    }

    public async Task<IReadOnlyList<PortalCredentialDto>> Handle(ListPortalCredentialsQuery request, CancellationToken ct)
    {
        EnsureAdmin();
        var rows = await _db.PortalCredentials.AsNoTracking().OrderBy(c => c.PortalName).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<PortalCredentialDto> Handle(CreatePortalCredentialCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        var entity = new PortalCredential
        {
            AgencyId = _user.AgencyId ?? Guid.Empty,
            PortalName = request.PortalName.Trim(),
            Url = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim(),
            Username = request.Username.Trim(),
            Password = request.Password,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedByUserId = _user.UserId ?? Guid.Empty,
        };
        _db.PortalCredentials.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<PortalCredentialDto> Handle(UpdatePortalCredentialCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        var entity = await _db.PortalCredentials.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(PortalCredential), request.Id);
        entity.PortalName = request.PortalName.Trim();
        entity.Url = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim();
        entity.Username = request.Username.Trim();
        entity.Password = request.Password;
        entity.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        entity.CreatedByUserId = _user.UserId ?? entity.CreatedByUserId;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<Unit> Handle(DeletePortalCredentialCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        var entity = await _db.PortalCredentials.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(PortalCredential), request.Id);
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    private static PortalCredentialDto Map(PortalCredential c) =>
        new(c.Id, c.PortalName, c.Url, c.Username, c.Password, c.Notes, c.CreatedAt, c.UpdatedAt);
}
