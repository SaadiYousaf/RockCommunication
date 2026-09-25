using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.Persistence.Seed;

/// <summary>
/// Provisions the platform owner's SuperAdmin account on a live installation, and invites them.
///
/// Runs only once, immediately after <see cref="GoLiveReset"/> has emptied the database. It is the
/// counterpart to that class: the reset removes every way in, this creates the one that remains.
///
/// The account is created the same way every other invited user is — a generated temporary password,
/// a forced change on first sign-in, and an invitation email — so there is no privileged back door
/// with different rules. Two-factor enrolment is then compulsory because SuperAdmin is in
/// <see cref="Roles.RequireTwoFactor"/>, which the middleware enforces server-side.
/// </summary>
public static class OwnerAccountSeeder
{
    /// <summary>
    /// The file the temporary password is written to when the installation has no working mail
    /// setup. See <see cref="WriteBreakGlassFileAsync"/> for why this exists.
    /// </summary>
    private const string BreakGlassFileName = "go-live-owner-credentials.txt";

    public static async Task RunAsync(
        AppDbContext db,
        UserManager<ApplicationUser> users,
        AuthEmailSender emailSender,
        IConfiguration config,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        var email = config["GoLive:SuperAdminEmail"]?.Trim();
        if (string.IsNullOrWhiteSpace(email)) return;

        // Idempotent on the email, not on a flag: if the account is already there, this has run.
        if (await users.FindByEmailAsync(email) is not null) return;

        var displayName = config["GoLive:SuperAdminName"]?.Trim();
        if (string.IsNullOrWhiteSpace(displayName)) displayName = "Platform Owner";

        var userName = DeriveUserName(email);
        var temporary = IdentityService.GenerateTemporaryPassword();

        var owner = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true,          // the invitation to this address IS the confirmation
            // Guid.Empty is the established convention for a cross-tenant account: it is what makes
            // the global query filter step aside for an unscoped SuperAdmin.
            AgencyId = Guid.Empty,
            DisplayName = displayName,
            IsActive = true,
            MustChangePassword = true,      // the temporary password is single-use, by design
            InvitationSentAt = DateTime.UtcNow,
        };

        var created = await users.CreateAsync(owner, temporary);
        if (!created.Succeeded)
        {
            logger.LogError(
                "Could not create the owner SuperAdmin account for {Email}: {Errors}",
                email, string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        var roleResult = await users.AddToRoleAsync(owner, Roles.SuperAdmin);
        if (!roleResult.Succeeded)
        {
            logger.LogError(
                "Created {Email} but could not grant SuperAdmin: {Errors}",
                email, string.Join("; ", roleResult.Errors.Select(e => e.Description)));
        }

        var emailed = await TrySendInviteAsync(emailSender, email, userName, temporary, logger, ct);

        // If the invitation could not be delivered, this account is unreachable and it is the only
        // account that exists. So the password is also written to a file on the server, which is
        // the one place the operator can read it without the application's help. It is a deliberate
        // trade: a secret at rest on a box the owner controls, against an installation nobody can
        // sign in to. The password is single-use and two-factor enrolment is still compulsory.
        if (!emailed)
            await WriteBreakGlassFileAsync(userName, email, temporary, logger, ct);

        db.PlatformStates.Add(new PlatformState
        {
            Key = "go-live-owner-account",
            AppliedAt = DateTime.UtcNow,
            Detail = $"SuperAdmin {userName} <{email}> created. " +
                     (emailed ? "Invitation emailed." : $"Invitation email FAILED; credentials written to {BreakGlassFileName}."),
        });
        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Go-live owner account ready: {UserName} <{Email}>, SuperAdmin, must change password and enrol in 2FA. Invitation emailed: {Emailed}",
            userName, email, emailed);
    }

    /// <summary>
    /// Sends the standard invitation. Returns whether it was actually delivered — unlike the normal
    /// registration path, which treats delivery as best-effort, here the answer decides whether a
    /// fallback is needed.
    /// </summary>
    private static async Task<bool> TrySendInviteAsync(
        AuthEmailSender emailSender, string email, string userName, string temporary,
        ILogger logger, CancellationToken ct)
    {
        if (emailSender is null)
        {
            logger.LogError("No email sender is configured, so the owner invitation could not be sent.");
            return false;
        }

        try
        {
            await emailSender.SendInviteAsync(
                email, userName, temporary, new[] { Roles.SuperAdmin }, ct,
                displayName: null, agencyName: null, callCenterName: null);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not email the go-live invitation to {Email}.", email);
            return false;
        }
    }

    /// <summary>
    /// Writes the temporary password beside the application's own data, with the tightest file
    /// permissions the platform allows, and tells the operator to delete it once they are in.
    /// </summary>
    private static async Task WriteBreakGlassFileAsync(
        string userName, string email, string temporary, ILogger logger, CancellationToken ct)
    {
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "App_Data");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, BreakGlassFileName);

            await File.WriteAllTextAsync(path,
                $"""
                SMH Achievers Life Group — go-live owner account
                ================================================

                The invitation email could not be delivered, so the sign-in details are here instead.

                  Sign in with : {userName}
                  Email        : {email}
                  Temporary password: {temporary}

                You will be asked to set a new password, and then to enrol in two-factor
                authentication, before you can use anything. Both are compulsory.

                DELETE THIS FILE once you have signed in. The password stops working the moment
                you change it, but the file should not outlive that.
                """, ct);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            logger.LogWarning(
                "Owner sign-in details written to {Path} because the invitation email failed. Delete this file after first sign-in.",
                path);
        }
        catch (Exception ex)
        {
            // Nothing left to fall back on; make the failure impossible to miss in the log.
            logger.LogCritical(ex,
                "The go-live invitation email failed AND the fallback credentials file could not be written. " +
                "The owner account {UserName} exists but nobody can sign in. Reset its password manually.",
                userName);
        }
    }

    /// <summary>
    /// A username from the email's local part: people type a username to sign in, and the full
    /// address is awkward. Falls back to "owner" if the local part has nothing usable in it.
    /// </summary>
    private static string DeriveUserName(string email)
    {
        var local = email.Split('@')[0];
        var cleaned = new string(local.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "owner" : cleaned.ToLowerInvariant();
    }
}
