using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using FluentValidation;
using MediatR;

namespace CRM.Application.Profile;

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>
/// A user's profile, modelled like an MS / Office account. The ORG-OWNED fields
/// (<see cref="DisplayName"/>, <see cref="UserName"/>, <see cref="Email"/>, <see cref="Roles"/>,
/// <see cref="Designation"/>, <see cref="TeamName"/>, <see cref="CallCenterName"/>,
/// <see cref="AgencyName"/>) are READ-ONLY to the user — managed by admins / HR — and are never
/// writable through any profile endpoint. Only <see cref="PhoneNumber"/>, <see cref="Location"/>,
/// <see cref="Bio"/> and the avatar are personal and self-editable.
/// </summary>
public record UserProfileDto(
    Guid Id,
    // ── Read-only, org-owned ──
    string UserName,
    string? DisplayName,
    string Email,
    IReadOnlyList<string> Roles,
    Guid AgencyId,
    string? AgencyName,
    Guid? CallCenterId,
    string? CallCenterName,
    Guid? TeamId,
    string? TeamName,
    string? Designation,
    // ── Editable, personal ──
    string? PhoneNumber,
    string? Location,
    string? Bio,
    bool HasAvatar);

/// <summary>The only fields a user may change on their own profile.</summary>
public record UpdateProfileInput(string? PhoneNumber, string? Location, string? Bio);

// ── Validation ────────────────────────────────────────────────────────────────

public class UpdateProfileInputValidator : AbstractValidator<UpdateProfileInput>
{
    public UpdateProfileInputValidator()
    {
        RuleFor(x => x.PhoneNumber).MaximumLength(ProfileConstants.MaxPhoneLength);
        RuleFor(x => x.Location).MaximumLength(ProfileConstants.MaxLocationLength);
        RuleFor(x => x.Bio).MaximumLength(ProfileConstants.MaxBioLength);
    }
}

public class UpdateMyProfileValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileValidator() => RuleFor(x => x.Input).SetValidator(new UpdateProfileInputValidator());
}

// ── Requests ──────────────────────────────────────────────────────────────────

/// <summary>The signed-in user's own profile (editable view).</summary>
public record GetMyProfileQuery : IRequest<UserProfileDto>;

/// <summary>Another user's profile — allowed only within the same agency (or SuperAdmin).</summary>
public record GetUserProfileQuery(Guid UserId) : IRequest<UserProfileDto>;

/// <summary>Update the caller's OWN editable personal fields. Self-only.</summary>
public record UpdateMyProfileCommand(UpdateProfileInput Input) : IRequest<UserProfileDto>;

/// <summary>Point the caller's OWN avatar at an already-stored file key. Self-only.</summary>
public record SetMyAvatarCommand(string StorageKey) : IRequest<Unit>;

/// <summary>The avatar storage key for a user (null if none). Same-agency access-scoped.</summary>
public record GetAvatarKeyQuery(Guid UserId) : IRequest<string?>;

// ── Handlers ────────────────────────────────────────────────────────────────

public class ProfileHandlers :
    IRequestHandler<GetMyProfileQuery, UserProfileDto>,
    IRequestHandler<GetUserProfileQuery, UserProfileDto>,
    IRequestHandler<UpdateMyProfileCommand, UserProfileDto>,
    IRequestHandler<SetMyAvatarCommand, Unit>,
    IRequestHandler<GetAvatarKeyQuery, string?>
{
    private readonly ICurrentUser _user;
    private readonly IUserProfileService _profiles;

    public ProfileHandlers(ICurrentUser user, IUserProfileService profiles)
    {
        _user = Guard.AgainstNull(user);
        _profiles = Guard.AgainstNull(profiles);
    }

    public Task<UserProfileDto> Handle(GetMyProfileQuery request, CancellationToken ct)
        => _profiles.GetProfileAsync(RequireUserId(), ct);

    public async Task<UserProfileDto> Handle(GetUserProfileQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = RequireUserId();
        var profile = await _profiles.GetProfileAsync(request.UserId, ct);
        // Own profile is always viewable; anyone else only within the same agency (or SuperAdmin).
        if (request.UserId != me) EnsureCanView(profile.AgencyId);
        return profile;
    }

    public async Task<UserProfileDto> Handle(UpdateMyProfileCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        // SELF-ONLY: updates only ever touch the caller's own record. The org-owned fields are
        // not part of UpdateProfileInput, so they can't be written here even by a crafted request.
        var me = RequireUserId();
        await _profiles.UpdateProfileAsync(me, request.Input, ct);
        return await _profiles.GetProfileAsync(me, ct);
    }

    public async Task<Unit> Handle(SetMyAvatarCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        // SELF-ONLY: a user may only set their own avatar.
        await _profiles.SetAvatarAsync(RequireUserId(), request.StorageKey, ct);
        return Unit.Value;
    }

    public async Task<string?> Handle(GetAvatarKeyQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var me = RequireUserId();
        if (request.UserId != me)
        {
            // Same-agency gate before we hand back a streamable key for someone else.
            var profile = await _profiles.GetProfileAsync(request.UserId, ct);
            EnsureCanView(profile.AgencyId);
        }
        return await _profiles.GetAvatarKeyAsync(request.UserId, ct);
    }

    private Guid RequireUserId() => _user.UserId ?? throw new ForbiddenAccessException();

    /// <summary>SuperAdmin sees everyone; everyone else is confined to their own agency.</summary>
    private void EnsureCanView(Guid targetAgencyId)
    {
        if (_user.IsSuperAdmin) return;
        if (_user.AgencyId is { } mine && mine == targetAgencyId) return;
        throw new ForbiddenAccessException();
    }
}
