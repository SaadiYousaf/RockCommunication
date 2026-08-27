using CRM.Application.Auth.Dtos;
using DomainRoles = CRM.Domain.Enums.Roles;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Infrastructure.Persistence;
using ISecondFactorMethod = CRM.Application.Common.Interfaces.ISecondFactorMethod;
using SecondFactorKind = CRM.Application.Common.Interfaces.SecondFactorKind;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace CRM.Infrastructure.Identity;

public class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<ApplicationRole> _roles;
    private readonly IJwtTokenService _jwt;
    private readonly ITwoFactorService _twoFactor;
    private readonly AppDbContext _db;
    private readonly SecondFactorRegistry _factorRegistry;
    private readonly IModuleAccessService _moduleAccess;
    private readonly AuthEmailSender _emailSender;
    private readonly bool _enforce2Fa;

    public IdentityService(
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles,
        IJwtTokenService jwt,
        ITwoFactorService twoFactor,
        AppDbContext db,
        SecondFactorRegistry factorRegistry,
        IModuleAccessService moduleAccess,
        AuthEmailSender emailSender,
        IConfiguration config)
    {
        _users = Guard.AgainstNull(users);
        _roles = Guard.AgainstNull(roles);
        _jwt = Guard.AgainstNull(jwt);
        _twoFactor = Guard.AgainstNull(twoFactor);
        _db = Guard.AgainstNull(db);
        _factorRegistry = Guard.AgainstNull(factorRegistry);
        _moduleAccess = Guard.AgainstNull(moduleAccess);
        _emailSender = Guard.AgainstNull(emailSender);
        // Mandatory 2FA for privileged roles — enforced by default, switchable off for tests.
        _enforce2Fa = Guard.AgainstNull(config).GetValue("Security:EnforceMandatoryTwoFactor", true);
    }

    // A VALID ASP.NET Identity password hash, used only to equalise the timing of the
    // unknown-user login path. It must be a well-formed Identity hash — a malformed value
    // (e.g. a bcrypt string) makes VerifyHashedPassword throw, which both 500s and leaks
    // user existence via the error. Computed once at startup.
    private static readonly string DummyPasswordHash =
        new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), "timing-equalisation-placeholder");

    public async Task<UserSummaryDto> RegisterAsync(string email, string userName, string? password, Guid agencyId, IEnumerable<string> roles, Guid? callCenterId = null, InviteContext? invite = null, CancellationToken ct = default)
    {
        Guard.AgainstNull(roles);

        // If admin didn't supply a password, generate a strong temporary one and force change on first login.
        var supplied = !string.IsNullOrWhiteSpace(password);
        var effectivePassword = supplied ? password! : GenerateTemporaryPassword();
        var mustChange = !supplied;

        // Reject duplicate identity up front with a clear 409 — Identity returns this
        // too, but lets it leak in the joined error string.
        if (await _users.FindByEmailAsync(email) is not null)
            throw new ConflictException("A user with that email already exists.");
        if (await _users.FindByNameAsync(userName) is not null)
            throw new ConflictException("A user with that username already exists.");

        // Reject unknown roles up front — before creating the user, so a typo'd role can't leave an
        // orphaned, permission-less account or mint a junk global role. Mirrors UpdateRolesAsync;
        // every real role is seeded at startup (DbSeeder), so valid invites/registrations pass.
        var roleList = roles.ToList();
        foreach (var role in roleList)
            if (!await _roles.RoleExistsAsync(role))
                throw new ConflictException($"Role '{role}' does not exist.");

        var user = new ApplicationUser
        {
            Email = email,
            UserName = userName,
            AgencyId = agencyId,
            CallCenterId = callCenterId,
            DisplayName = invite?.DisplayName,
            EmailConfirmed = true,
            MustChangePassword = mustChange,
            // InvitationSentAt is stamped when the invite email goes out below.
        };
        var result = await _users.CreateAsync(user, effectivePassword);
        if (!result.Succeeded)
            throw new ConflictException(string.Join("; ", result.Errors.Select(e => e.Description)));

        foreach (var role in roleList)
            await _users.AddToRoleAsync(user, role);

        // Send the invite email — best-effort, errors are logged but don't fail registration.
        if (mustChange)
        {
            try { await _emailSender.SendInviteAsync(email, userName, effectivePassword, roleList, ct,
                    invite?.DisplayName, invite?.AgencyName, invite?.CallCenterName); }
            catch { /* logged inside sender */ }
            user.InvitationSentAt = DateTime.UtcNow;
            await _users.UpdateAsync(user);
        }

        var assigned = (await _users.GetRolesAsync(user)).ToList();
        var modules = await _moduleAccess.GetCodesForUserAsync(user.Id, ct);
        // We intentionally do NOT echo the temporary password back to the API caller.
        // The invite email is authoritative; making the admin's session a second carrier
        // doubles the leakage surface and lets a compromised admin pivot to all freshly-
        // invited users. Resend the invite if delivery fails.
        return new UserSummaryDto(
            user.Id, user.UserName!, user.Email!, user.AgencyId, assigned, modules,
            MustChangePassword: mustChange);
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);

        if (string.IsNullOrWhiteSpace(newPassword))
            throw new ConflictException("New password cannot be empty.");
        if (newPassword == currentPassword)
            throw new ConflictException("New password must differ from the current one.");

        var result = await _users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
            throw new ConflictException(string.Join("; ", result.Errors.Select(e => e.Description)));

        if (user.MustChangePassword)
        {
            // Completing the forced first-login change IS the invitation being accepted.
            user.MustChangePassword = false;
            user.InvitationAcceptedAt ??= DateTime.UtcNow;
            await _users.UpdateAsync(user);
        }
        // Force re-login on every device — refresh tokens stop working after a password change.
        await _jwt.RevokeAllForUserAsync(userId, ct);
    }

    /// <summary>
    /// Generates a 14-char password that always satisfies the password policy:
    /// at least one upper, lower, digit, and non-alphanumeric.
    /// </summary>
    /// <summary>Policy-compliant temp password. Internal so InvitationService can reuse it (no duplicate generator).</summary>
    internal static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // dropped I, O for readability
        const string lower = "abcdefghijkmnpqrstuvwxyz";   // dropped l, o
        const string digit = "23456789";                    // dropped 0, 1
        const string symbol = "!@#$%^&*?_-";

        var bytes = RandomNumberGenerator.GetBytes(14);
        char Pick(string set, int i) => set[bytes[i] % set.Length];

        // Guarantee one of each required class, then fill the rest from the union.
        var pwd = new char[14];
        pwd[0] = Pick(upper, 0);
        pwd[1] = Pick(lower, 1);
        pwd[2] = Pick(digit, 2);
        pwd[3] = Pick(symbol, 3);
        const string all = upper + lower + digit + symbol;
        for (var i = 4; i < pwd.Length; i++) pwd[i] = Pick(all, i);

        // Fisher–Yates shuffle so the guaranteed chars aren't always at the front.
        for (var i = pwd.Length - 1; i > 0; i--)
        {
            var j = bytes[i] % (i + 1);
            (pwd[i], pwd[j]) = (pwd[j], pwd[i]);
        }
        return new string(pwd);
    }

    public async Task<LoginResponse> LoginAsync(string userNameOrEmail, string password, CancellationToken ct = default)
    {
        // Generic message for every failure mode below — never reveal whether the user
        // exists, is locked, is disabled, or just typed the wrong password.
        const string generic = "Invalid credentials.";

        var user = await _users.FindByNameAsync(userNameOrEmail) ?? await _users.FindByEmailAsync(userNameOrEmail);

        if (user is null)
        {
            // Equalise timing with the real-user path: hashing a password takes tens of
            // milliseconds. Skipping it leaks user existence via response time.
            _users.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), DummyPasswordHash, password);
            throw new ForbiddenAccessException(generic);
        }

        // A locked account still early-returns (a locked user with the CORRECT password must not pass
        // CheckPasswordAsync), but with the SAME generic 403 as every other failure — a distinct 429
        // would reveal that the account exists / is locked, defeating the anti-enumeration invariant above.
        if (await _users.IsLockedOutAsync(user))
            throw new ForbiddenAccessException(generic);

        if (!await _users.CheckPasswordAsync(user, password))
        {
            // Identity tracks AccessFailedCount and locks the account at the configured threshold.
            // Even when THIS failure trips the lockout, return the generic 403 (not a 429) so the
            // response never reveals the account exists or is now locked.
            await _users.AccessFailedAsync(user);
            throw new ForbiddenAccessException(generic);
        }

        // Deactivated accounts are rejected only AFTER the password check runs — so a WRONG
        // password on an inactive account is still indistinguishable (same generic message +
        // timing) from any other bad login, which is what prevents account enumeration.
        // But once the CORRECT password is supplied we can safely name the reason (same policy
        // the tenant kill-switch below uses): a confusing "Invalid credentials." for someone
        // holding valid credentials just looks like a broken password. Tell them they're blocked.
        if (!user.IsActive)
            throw new ForbiddenAccessException(
                "Your account has been deactivated. Please contact your administrator.");

        // Company / call-center kill switch: if a SuperAdmin has disabled the user's agency
        // (or their call center), lock every user underneath it out of login — even though
        // their individual account is still active. Runs after the password check (so it can
        // safely reveal a specific reason) and before any 2FA challenge is sent.
        if (!await TenantLoginGate.IsTenantActiveAsync(_db, user.AgencyId, user.CallCenterId, ct))
            throw new ForbiddenAccessException(TenantLoginGate.DisabledMessage);

        // Onboarding invitations expire if never accepted within the policy window: a stale
        // temporary-password link must not grant access — an admin has to resend a fresh one.
        // Runs after the password check (so we can safely name the reason) and only affects
        // users still on their forced first-login password; established users are unaffected.
        if (InvitationPolicy.IsExpired(user.MustChangePassword, user.InvitationSentAt, user.InvitationAcceptedAt, DateTime.UtcNow))
            throw new ForbiddenAccessException(InvitationPolicy.ExpiredMessage);

        // Clean slate on every successful password check — even when 2FA still has to run.
        await _users.ResetAccessFailedCountAsync(user);

        if (user.TwoFactorEnabled)
        {
            var pending = new TwoFactorPendingToken
            {
                UserId = user.Id,
                Token = GenerateToken(),
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };
            _db.TwoFactorPendingTokens.Add(pending);
            await _db.SaveChangesAsync(ct);

            var kind = ParseKind(user.PreferredTwoFactorMethod);
            await _factorRegistry.Get(kind).ChallengeAsync(user.Id, ct);

            return new LoginResponse(string.Empty, string.Empty, default, true, pending.Token, null);
        }

        return await IssueLoginAsync(user, ct);
    }

    private const int MaxTwoFactorAttempts = 5;

    public async Task<LoginResponse> VerifyTwoFactorAsync(string twoFactorToken, string code, CancellationToken ct = default)
    {
        var pending = await _db.TwoFactorPendingTokens.FirstOrDefaultAsync(t => t.Token == twoFactorToken, ct);
        if (pending is null || pending.ExpiresAt < DateTime.UtcNow)
        {
            // Tidy up expired rows opportunistically.
            if (pending is not null) _db.TwoFactorPendingTokens.Remove(pending);
            await _db.SaveChangesAsync(ct);
            throw new ForbiddenAccessException("Two-factor session expired.");
        }

        var user = await _users.FindByIdAsync(pending.UserId.ToString())
            ?? throw new ForbiddenAccessException();

        var kind = ParseKind(user.PreferredTwoFactorMethod);
        var ok = await _factorRegistry.Get(kind).VerifyAsync(user.Id, code, ct);
        if (!ok)
        {
            pending.Attempts++;
            if (pending.Attempts >= MaxTwoFactorAttempts)
            {
                // Burn the pending session — attacker has to start over from /login,
                // which is itself rate-limited via the lockout policy.
                _db.TwoFactorPendingTokens.Remove(pending);
                await _db.SaveChangesAsync(ct);
                throw new TooManyRequestsException(
                    "Too many invalid codes. Please sign in again.",
                    TimeSpan.FromMinutes(1));
            }
            await _db.SaveChangesAsync(ct);
            throw new ForbiddenAccessException("Invalid two-factor code.");
        }

        // Single-use: a successful pending token cannot be reused, even within its TTL.
        _db.TwoFactorPendingTokens.Remove(pending);
        await _db.SaveChangesAsync(ct);

        return await IssueLoginAsync(user, ct);
    }

    private static SecondFactorKind ParseKind(string raw) =>
        Enum.TryParse<SecondFactorKind>(raw, ignoreCase: true, out var k) ? k : SecondFactorKind.Totp;

    public async Task<TwoFactorSetupResponse> SetupTwoFactorAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);

        var secret = await _users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(secret))
        {
            await _users.ResetAuthenticatorKeyAsync(user);
            secret = await _users.GetAuthenticatorKeyAsync(user);
        }

        var qr = _twoFactor.BuildQrUri(user.Email!, secret!);
        return new TwoFactorSetupResponse(secret!, qr);
    }

    public async Task EnableTwoFactorAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);

        // Verify against the user's SELECTED method (authenticator app OR email OTP). Previously this
        // always checked the TOTP authenticator key, so anyone enrolling via email OTP entered a
        // valid emailed code and still got "Invalid code" — 2FA email enrolment was impossible.
        var kind = ParseKind(user.PreferredTwoFactorMethod);
        var ok = await _factorRegistry.Get(kind).VerifyAsync(user.Id, (code ?? string.Empty).Trim(), ct);
        if (!ok)
            throw new ForbiddenAccessException("Invalid code.");

        await _users.SetTwoFactorEnabledAsync(user, true);
        // If this enrolment satisfied a MANDATORY-2FA requirement, the user's live tokens
        // still carry the stale "twofa_setup" claim — revoke them so their next sign-in is a
        // clean 2FA challenge. Voluntary enrollers (non-privileged) keep their session.
        var roles = await _users.GetRolesAsync(user);
        if (_enforce2Fa && DomainRoles.TwoFactorMandatory(roles))
            await _jwt.RevokeAllForUserAsync(userId, ct);
    }

    public async Task DisableTwoFactorAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);

        // 2FA is mandatory for privileged roles — they may not turn it off.
        var roles = await _users.GetRolesAsync(user);
        if (_enforce2Fa && DomainRoles.TwoFactorMandatory(roles))
            throw new ForbiddenAccessException("Two-factor authentication is mandatory for this role and cannot be disabled.");

        await _users.SetTwoFactorEnabledAsync(user, false);
        // Reset the authenticator key so re-enabling generates a fresh secret.
        await _users.ResetAuthenticatorKeyAsync(user);
        user.PreferredTwoFactorMethod = null;
        await _users.UpdateAsync(user);
    }

    public async Task<TwoFactorStatusDto> GetTwoFactorStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);
        return new TwoFactorStatusDto(user.TwoFactorEnabled, user.PreferredTwoFactorMethod);
    }

    public Task<TokenResult?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
        => _jwt.RefreshAsync(refreshToken, ct);

    public Task LogoutAsync(string refreshToken, CancellationToken ct = default)
        => _jwt.RevokeAsync(refreshToken, ct);

    public async Task<UserSummaryDto?> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return null;
        var roles = await _users.GetRolesAsync(user);
        var modules = await _moduleAccess.GetCodesForUserAsync(user.Id, ct);
        var money = await ResolveMoneyAsync(user.AgencyId, ct);
        return new UserSummaryDto(user.Id, user.UserName!, user.Email!, user.AgencyId, roles.ToList(), modules,
            MustChangePassword: user.MustChangePassword, TeamId: user.TeamId, IsActive: user.IsActive,
            CallCenterId: user.CallCenterId,
            TwoFactorSetupRequired: _enforce2Fa && !user.TwoFactorEnabled && DomainRoles.TwoFactorMandatory(roles),
            AgencyName: await ResolveAgencyNameAsync(user.AgencyId, ct),
            DisplayCurrency: money.Currency, ExchangeRate: money.Rate,
            CallCenterName: await ResolveCallCenterNameAsync(user.CallCenterId, ct),
            InvitationExpired: InvitationPolicy.IsExpired(user.MustChangePassword, user.InvitationSentAt, user.InvitationAcceptedAt, DateTime.UtcNow));
    }

    /// <summary>Agency display name for a user, or null for SuperAdmin / central users (Guid.Empty).</summary>

    /// <summary>The agency's money-display settings, so every screen formats the same way.</summary>
    private async Task<(string Currency, decimal Rate)> ResolveMoneyAsync(Guid agencyId, CancellationToken ct)
    {
        if (agencyId == Guid.Empty) return ("USD", 1m);
        var row = await _db.Agencies.AsNoTracking()
            .Where(a => a.Id == agencyId)
            .Select(a => new { a.DisplayCurrency, a.ExchangeRate })
            .FirstOrDefaultAsync(ct);
        return row is null ? ("USD", 1m) : (row.DisplayCurrency, row.ExchangeRate);
    }

    private Task<string?> ResolveAgencyNameAsync(Guid agencyId, CancellationToken ct)
        => agencyId == Guid.Empty
            ? Task.FromResult<string?>(null)
            : _db.Agencies.IgnoreQueryFilters().Where(a => a.Id == agencyId).Select(a => a.Name).FirstOrDefaultAsync(ct);

    /// <summary>Call-center display name for a user, or null when they're agency-level (no call center).</summary>
    private Task<string?> ResolveCallCenterNameAsync(Guid? callCenterId, CancellationToken ct)
        => callCenterId is not { } ccId || ccId == Guid.Empty
            ? Task.FromResult<string?>(null)
            : _db.CallCenters.IgnoreQueryFilters().Where(c => c.Id == ccId).Select(c => c.Name).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<UserSummaryDto>> ListUsersAsync(Guid? agencyId, CancellationToken ct = default)
    {
        var q = _users.Users.AsQueryable();
        if (agencyId is { } aid) q = q.Where(u => u.AgencyId == aid);
        var list = await q.OrderBy(u => u.UserName).ToListAsync(ct);

        var result = new List<UserSummaryDto>(list.Count);
        foreach (var u in list)
        {
            var roles = await _users.GetRolesAsync(u);
            var modules = await _moduleAccess.GetCodesForUserAsync(u.Id, ct);
            result.Add(new UserSummaryDto(u.Id, u.UserName!, u.Email!, u.AgencyId, roles.ToList(), modules,
                MustChangePassword: u.MustChangePassword, TeamId: u.TeamId, IsActive: u.IsActive,
                CallCenterId: u.CallCenterId,
                InvitationExpired: InvitationPolicy.IsExpired(u.MustChangePassword, u.InvitationSentAt, u.InvitationAcceptedAt, DateTime.UtcNow)));
        }
        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ListUserNamesAsync(Guid? agencyId, CancellationToken ct = default)
    {
        var q = _users.Users.AsNoTracking();
        if (agencyId is { } aid) q = q.Where(u => u.AgencyId == aid);
        var pairs = await q.Select(u => new { u.Id, u.UserName }).ToListAsync(ct);
        return pairs.ToDictionary(u => u.Id, u => u.UserName ?? string.Empty);
    }

    private async Task<LoginResponse> IssueLoginAsync(ApplicationUser user, CancellationToken ct)
    {
        // Re-check IsActive before minting any token — covers the 2FA-completion path, where the
        // account could have been deactivated during the pending-2FA window (after the password step).
        // Mirrors LoginAsync's own deactivation gate so the two paths stay in lockstep.
        if (!user.IsActive)
            throw new ForbiddenAccessException(
                "Your account has been deactivated. Please contact your administrator.");

        // Final tenant kill-switch check before any token is minted — also covers the
        // 2FA-completion path (VerifyTwoFactorAsync), which reaches token issuance here
        // without passing back through LoginAsync's earlier gate.
        if (!await TenantLoginGate.IsTenantActiveAsync(_db, user.AgencyId, user.CallCenterId, ct))
            throw new ForbiddenAccessException(TenantLoginGate.DisabledMessage);

        var roles = (await _users.GetRolesAsync(user)).ToList();
        var modules = await _moduleAccess.GetCodesForUserAsync(user.Id, ct);

        // Tag the access token when the user is forced to change their password. The
        // PasswordChangeRequiredFilter on the API side rejects every non-auth call until
        // the password is rotated, so a stolen "must-change" token can't be used.
        // Enforcement claims (forced password change + mandatory-2FA setup) computed via the shared
        // helper so login and token-refresh stay in lockstep — a refresh must not be able to drop them.
        var extra = AuthEnforcementClaims.Build(user, roles, _enforce2Fa);
        var require2Fa = extra?.ContainsKey(CustomJwtClaims.TwoFactorSetupRequired) ?? false;

        var token = await _jwt.IssueAsync(user.Id, user.UserName!, user.AgencyId, roles, user.CallCenterId, extra, ct: ct);
        var money = await ResolveMoneyAsync(user.AgencyId, ct);
        var summary = new UserSummaryDto(user.Id, user.UserName!, user.Email!, user.AgencyId, roles, modules,
            MustChangePassword: user.MustChangePassword, CallCenterId: user.CallCenterId,
            TwoFactorSetupRequired: require2Fa,
            AgencyName: await ResolveAgencyNameAsync(user.AgencyId, ct),
            DisplayCurrency: money.Currency, ExchangeRate: money.Rate,
            CallCenterName: await ResolveCallCenterNameAsync(user.CallCenterId, ct));
        return new LoginResponse(token.AccessToken, token.RefreshToken, token.ExpiresAt, false, null, summary);
    }

    public async Task<LoginResponse> SetContextAsync(Guid userId, Guid? agencyId, Guid? callCenterId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User", userId);
        if (!user.IsActive)
            throw new ForbiddenAccessException("Your account has been deactivated. Please contact your administrator.");

        var roles = (await _users.GetRolesAsync(user)).ToList();
        var isSuperAdmin = roles.Contains(DomainRoles.SuperAdmin);

        // Resolve the EFFECTIVE agency/call-center this session runs at, validated against what the
        // caller may actually reach, plus the scope to PERSIST so the choice survives token refresh.
        Guid effAgency;
        Guid? effCallCenter;
        Guid? scopeAgency;      // only a SuperAdmin's agency can differ from home — persist for refresh
        Guid? scopeCallCenter;

        if (isSuperAdmin)
        {
            if (agencyId is { } aid && aid != Guid.Empty)
            {
                var agencyOk = await _db.Agencies.IgnoreQueryFilters()
                    .AnyAsync(a => a.Id == aid && !a.IsDeleted && a.IsActive, ct);
                if (!agencyOk) throw new NotFoundException("Agency", aid);
                effAgency = aid;
                effCallCenter = await ValidatedCallCenterAsync(callCenterId, aid, ct);
                scopeAgency = aid;
                scopeCallCenter = effCallCenter;
            }
            else
            {
                // "All agencies" — the platform-wide view. A call center is meaningless without an agency.
                effAgency = Guid.Empty; effCallCenter = null; scopeAgency = null; scopeCallCenter = null;
            }
        }
        else if (user.CallCenterId is { } pinned)
        {
            // Call-center-pinned user: cannot change agency or center.
            if (callCenterId is { } cc && cc != pinned)
                throw new ForbiddenAccessException("You can only work within your own call center.");
            effAgency = user.AgencyId; effCallCenter = pinned; scopeAgency = null; scopeCallCenter = pinned;
        }
        else
        {
            // Agency-level admin: confined to their own agency; may pick a call center within it (null = agency-wide).
            if (agencyId is { } aid && aid != user.AgencyId)
                throw new ForbiddenAccessException("You can only work within your own agency.");
            effAgency = user.AgencyId;
            effCallCenter = await ValidatedCallCenterAsync(callCenterId, user.AgencyId, ct);
            scopeAgency = null; scopeCallCenter = effCallCenter;
        }

        if (!await TenantLoginGate.IsTenantActiveAsync(_db, effAgency, effCallCenter, ct))
            throw new ForbiddenAccessException(TenantLoginGate.DisabledMessage);

        var modules = await _moduleAccess.GetCodesForUserAsync(user.Id, ct);
        // Rebuild enforcement claims (a confined must-change-password / 2FA-setup session must not
        // shed its confinement by re-scoping) — same rule login and refresh follow.
        var extra = AuthEnforcementClaims.Build(user, roles, _enforce2Fa);
        var require2Fa = extra?.ContainsKey(CustomJwtClaims.TwoFactorSetupRequired) ?? false;

        var token = await _jwt.IssueAsync(user.Id, user.UserName!, effAgency, roles, effCallCenter, extra,
            scopeAgency, scopeCallCenter, ct);
        var money = await ResolveMoneyAsync(effAgency, ct);
        var summary = new UserSummaryDto(user.Id, user.UserName!, user.Email!, effAgency, roles, modules,
            MustChangePassword: user.MustChangePassword, TeamId: user.TeamId, IsActive: user.IsActive,
            CallCenterId: effCallCenter,
            TwoFactorSetupRequired: require2Fa,
            AgencyName: await ResolveAgencyNameAsync(effAgency, ct),
            DisplayCurrency: money.Currency, ExchangeRate: money.Rate,
            CallCenterName: await ResolveCallCenterNameAsync(effCallCenter, ct),
            InvitationExpired: InvitationPolicy.IsExpired(user.MustChangePassword, user.InvitationSentAt, user.InvitationAcceptedAt, DateTime.UtcNow));
        return new LoginResponse(token.AccessToken, token.RefreshToken, token.ExpiresAt, false, null, summary);
    }

    /// <summary>Validate a chosen call center belongs to the agency and is live; null/empty = "all call centers".</summary>
    private async Task<Guid?> ValidatedCallCenterAsync(Guid? callCenterId, Guid agencyId, CancellationToken ct)
    {
        if (callCenterId is not { } cc || cc == Guid.Empty) return null;
        var ok = await _db.CallCenters.IgnoreQueryFilters()
            .AnyAsync(c => c.Id == cc && c.AgencyId == agencyId && !c.IsDeleted && c.IsActive, ct);
        if (!ok) throw new NotFoundException("CallCenter", cc);
        return cc;
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    public async Task SendEmailConfirmationAsync(string email, CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(email);
        // Silent success: never reveal whether an email is registered.
        if (user is null || user.EmailConfirmed) return;

        var token = await _users.GenerateEmailConfirmationTokenAsync(user);
        await _emailSender.SendEmailConfirmationAsync(user.Email!, user.UserName ?? user.Email!, user.Id, token, ct);
    }

    public async Task ConfirmEmailAsync(Guid userId, string token, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new ForbiddenAccessException("Invalid or expired confirmation link.");
        var result = await _users.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
            throw new ForbiddenAccessException("Invalid or expired confirmation link.");
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(email);
        // Silent success — don't leak which emails exist.
        if (user is null || !user.IsActive) return;

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        await _emailSender.SendPasswordResetAsync(user.Email!, user.UserName ?? user.Email!, user.Email!, token, ct);
    }

    public async Task ResetPasswordAsync(string email, string token, string newPassword, CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(email)
            ?? throw new ForbiddenAccessException("Invalid or expired reset link.");
        var result = await _users.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            // Surface only password-policy errors (about the value the caller submitted).
            // Token / other errors must return the SAME generic message as the unknown-email
            // path, otherwise the differing error text lets an attacker enumerate accounts.
            var pwdErrors = result.Errors
                .Where(e => e.Code.StartsWith("Password", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Description).ToList();
            if (pwdErrors.Count > 0)
                throw new ConflictException(string.Join("; ", pwdErrors));
            throw new ForbiddenAccessException("Invalid or expired reset link.");
        }

        // Force re-login on every device — an attacker with a stolen refresh token
        // must not survive the legitimate user resetting their password.
        await _jwt.RevokeAllForUserAsync(user.Id, ct);
    }
}
