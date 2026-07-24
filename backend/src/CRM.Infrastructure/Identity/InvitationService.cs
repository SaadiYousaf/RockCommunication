using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Identity;

/// <summary>
/// Reusable onboarding invitation flow shared by Agency CEO and Call Center Admin
/// provisioning (and any future invited role).
///
/// Deliberately thin: user creation, the temporary password, the forced first-login flag
/// and the invitation email all come from <see cref="IIdentityService.RegisterAsync"/>
/// (called with a null password). This service only adds what didn't exist — invitation
/// status tracking and resend — so there is exactly one implementation of each concern.
/// </summary>
public class InvitationService : IInvitationService
{
    private readonly IIdentityService _identity;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AuthEmailSender _emailSender;
    private readonly AppDbContext _db;

    public InvitationService(
        IIdentityService identity,
        UserManager<ApplicationUser> users,
        AuthEmailSender emailSender,
        AppDbContext db)
    {
        _identity = Guard.AgainstNull(identity);
        _users = Guard.AgainstNull(users);
        _emailSender = Guard.AgainstNull(emailSender);
        _db = Guard.AgainstNull(db);
    }

    public async Task EnsureEmailAvailableAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ConflictException("An email address is required.");
        if (await _users.FindByEmailAsync(email) is not null)
            throw new ConflictException($"A user with the email \"{email}\" already exists.");
    }

    public async Task<InvitationResult> InviteAsync(InvitationRequest request, CancellationToken ct = default)
    {
        Guard.AgainstNull(request);

        var email = request.Email.Trim();
        await EnsureEmailAvailableAsync(email, ct);

        var userName = await DeriveUniqueUserNameAsync(email);
        var summary = await _identity.RegisterAsync(
            email,
            userName,
            password: null,                     // → temp password + MustChangePassword + invite email
            request.AgencyId,
            request.Roles,
            request.CallCenterId,
            new InviteContext(request.FullName?.Trim(), request.AgencyName, request.CallCenterName),
            ct);

        var sentAt = await _users.FindByIdAsync(summary.Id.ToString()) is { } created && created.InvitationSentAt is { } s
            ? s
            : DateTime.UtcNow;

        return new InvitationResult(summary.Id, summary.UserName, summary.Email, sentAt);
    }

    public async Task<InvitationResult> ResendAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);

        if (user.InvitationAcceptedAt is not null)
            throw new ConflictException("This invitation has already been accepted; reset the password instead.");

        // Mint a fresh temporary password through the same generator used at registration.
        var temporary = IdentityService.GenerateTemporaryPassword();
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var reset = await _users.ResetPasswordAsync(user, token, temporary);
        if (!reset.Succeeded)
            throw new ConflictException(string.Join("; ", reset.Errors.Select(e => e.Description)));

        user.MustChangePassword = true;         // re-arm the forced first-login change
        user.InvitationSentAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);

        var roles = (await _users.GetRolesAsync(user)).ToList();
        var (agencyName, callCenterName) = await ResolveOrgNamesAsync(user, ct);

        try
        {
            await _emailSender.SendInviteAsync(user.Email!, user.UserName!, temporary, roles, ct,
                user.DisplayName, agencyName, callCenterName);
        }
        catch { /* logged inside the sender; the invitation row is still stamped */ }

        return new InvitationResult(user.Id, user.UserName!, user.Email!, user.InvitationSentAt!.Value);
    }

    public async Task MarkAcceptedAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null || user.InvitationAcceptedAt is not null) return;   // idempotent
        user.InvitationAcceptedAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);
    }

    /// <summary>
    /// Identity requires a unique UserName. Derive it from the email's local part and
    /// suffix a counter on collision so onboarding never fails on a name clash.
    /// </summary>
    private async Task<string> DeriveUniqueUserNameAsync(string email)
    {
        var local = email.Split('@')[0];
        var basis = new string(local.Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());
        if (string.IsNullOrWhiteSpace(basis)) basis = "user";

        var candidate = basis;
        for (var i = 1; await _users.FindByNameAsync(candidate) is not null; i++)
            candidate = $"{basis}{i}";
        return candidate;
    }

    /// <summary>Agency / call-center display names for the invitation email.</summary>
    private async Task<(string? Agency, string? CallCenter)> ResolveOrgNamesAsync(ApplicationUser user, CancellationToken ct)
    {
        var agency = user.AgencyId == Guid.Empty
            ? null
            : await _db.Agencies.IgnoreQueryFilters()
                .Where(a => a.Id == user.AgencyId).Select(a => a.Name).FirstOrDefaultAsync(ct);

        var callCenter = user.CallCenterId is not { } ccId
            ? null
            : await _db.CallCenters.IgnoreQueryFilters()
                .Where(c => c.Id == ccId).Select(c => c.Name).FirstOrDefaultAsync(ct);

        return (agency, callCenter);
    }
}
