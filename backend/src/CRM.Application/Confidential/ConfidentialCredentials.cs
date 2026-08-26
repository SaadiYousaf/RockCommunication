using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Confidential;

/// <summary>
/// A stored portal login WITHOUT its secret. The password is deliberately not part of this shape:
/// returning every vault password in one list response meant a single request (or a cached
/// response, or an open DevTools tab) exposed the whole vault. Fetch one secret at a time through
/// <see cref="RevealPortalCredentialQuery"/>, which is audited.
/// </summary>
public record PortalCredentialDto(
    Guid Id, string PortalName, string? Url, string Username, string? Notes,
    bool HasPassword, DateTime CreatedAt, DateTime? UpdatedAt);

/// <summary>The decrypted secret for ONE credential. Every reveal writes an audit entry.</summary>
public record RevealedCredentialDto(Guid Id, string Password);

public record ListPortalCredentialsQuery : IRequest<IReadOnlyList<PortalCredentialDto>>;

/// <summary>Reveal a single credential's password. Audited — this is the sensitive read.</summary>
public record RevealPortalCredentialQuery(Guid Id) : IRequest<RevealedCredentialDto>;

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
    IRequestHandler<RevealPortalCredentialQuery, RevealedCredentialDto>,
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

    /// <summary>
    /// Reveal one password, and RECORD it. Viewing a vault secret is the single most sensitive read
    /// in the app, and the write-path audit interceptor never sees a read — so the entry is written
    /// here explicitly, giving "who opened which credential, when, from where".
    /// </summary>
    public async Task<RevealedCredentialDto> Handle(RevealPortalCredentialQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        var entity = await _db.PortalCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(PortalCredential), request.Id);

        _db.AuditEntries.Add(new AuditEntry
        {
            AgencyId = entity.AgencyId == Guid.Empty ? null : entity.AgencyId,
            EntityName = nameof(PortalCredential),
            EntityId = entity.Id.ToString(),
            Action = "Reveal",
            UserId = _user.UserId?.ToString(),
            UserName = _user.UserName,
            Changes = null,                 // never log the secret itself
            IpAddress = _user.IpAddress,
        });
        await _db.SaveChangesAsync(ct);

        return new RevealedCredentialDto(entity.Id, entity.Password);
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
        new(c.Id, c.PortalName, c.Url, c.Username, c.Notes,
            !string.IsNullOrEmpty(c.Password), c.CreatedAt, c.UpdatedAt);
}
