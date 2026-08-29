using CRM.Api.Authorization;
using CRM.Application.Users.Commands;
using CRM.Infrastructure.Identity;
using CRM.Application.Auth.Dtos;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CRM.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]   // Brute-force protection: 5 attempts / minute / IP.
public class AuthController : ControllerBase
{
    private readonly IIdentityService _identity;
    private readonly ICurrentUser _user;
    private readonly IMediator _mediator;

    public AuthController(IIdentityService identity, ICurrentUser user, IMediator mediator)
    {
        _identity = Guard.AgainstNull(identity);
        _user = Guard.AgainstNull(user);
        _mediator = Guard.AgainstNull(mediator);
    }

    /// <summary>AgencyId is optional — defaults to the calling admin's agency. Only a SuperAdmin can target a different agency.</summary>
    /// <summary>
    /// Create a user. CallCenterId and TeamId are optional but strongly preferred: without them the
    /// admin had to create the account, find it in the list, then set its call centre and team as
    /// two more round trips — and a user sitting with neither is invisible to most of the app.
    /// </summary>
    public record RegisterRequest(
        string Email, string UserName, string? Password, Guid? AgencyId, string[] Roles,
        Guid? CallCenterId = null, Guid? TeamId = null);
    public record LoginRequest(string UserNameOrEmail, string Password);
    public record TwoFactorVerifyRequest(string TwoFactorToken, string Code);
    public record TwoFactorEnableRequest(string Code);
    public record RefreshRequest(string RefreshToken);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    [HasPermission(Permissions.UsersManage)]
    [HttpPost("register")]
    public async Task<ActionResult<UserSummaryDto>> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        // Cross-tenant defense: a regular Admin can only invite into their own agency.
        // A SuperAdmin (no agency in token) may target any agency explicitly.
        var isSuperAdmin = _user.Roles.Contains(Roles.SuperAdmin);

        // Privilege-escalation defense: only a SuperAdmin may mint a SuperAdmin. Otherwise
        // any agency admin (who holds UsersManage) could register a global cross-tenant admin.
        if (!isSuperAdmin && req.Roles.Any(r => string.Equals(r, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase)))
            return Forbid();

        // Same anti-escalation rule as the update-roles path: only SuperAdmin/Admin/CEO may create
        // an account that already holds an agency-admin-equivalent role. A plain users.manage holder
        // (ProgramManager / CallCenterAdmin / ...) must not mint agency-admin power via register either.
        if (!Roles.CanGrantElevated(_user.Roles) && Roles.GrantsElevated(req.Roles))
            return Forbid();

        Guid agencyId;
        if (req.AgencyId is { } requested)
        {
            if (!isSuperAdmin && _user.AgencyId is { } callerAgency && requested != callerAgency)
                return Forbid();
            agencyId = requested;
        }
        else
        {
            if (_user.AgencyId is null) return BadRequest(new { error = "agencyId is required." });
            agencyId = _user.AgencyId.Value;
        }
        // Confinement: a call-center-pinned caller (e.g. CallCenterAdmin) may only create users
        // inside their own call center — mirrors UserAdminService.AuthorizeTargetAsync. Agency-level
        // callers (CallCenterId == null) and SuperAdmin may name any call centre in the target
        // agency; SetUserCallCenterCommand re-validates that it actually belongs there.
        var pinned = isSuperAdmin ? null : _user.CallCenterId;
        var callCenterId = pinned ?? req.CallCenterId;
        if (pinned is { } p && req.CallCenterId is { } wanted && wanted != p) return Forbid();

        var created = await _identity.RegisterAsync(
            req.Email, req.UserName, req.Password, agencyId, req.Roles, callCenterId: callCenterId, ct: ct);

        // Team is a separate concern from identity, so it goes through the same command the admin
        // screen uses — which validates the team belongs to this agency and notifies the user.
        if (req.TeamId is { } teamId && teamId != Guid.Empty)
            created = await _mediator.Send(new SetUserTeamCommand(created.Id, teamId), ct);

        return Ok(created);
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        if (_user.UserId is null) return Forbid();
        await _identity.ChangePasswordAsync(_user.UserId.Value, req.CurrentPassword, req.NewPassword, ct);
        return NoContent();
    }

    [AllowAnonymous]

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        // This endpoint is anonymous and bypasses MediatR, so nothing else validates it: a null or
        // absurdly long field used to reach the identity layer and 500 on an unauthenticated route.
        // The message stays deliberately generic — it must not hint at what a valid input looks like.
        if (string.IsNullOrWhiteSpace(req.UserNameOrEmail) || string.IsNullOrWhiteSpace(req.Password)
            || req.UserNameOrEmail.Length > 256 || req.Password.Length > 256)
            return BadRequest(new { title = "Sign-in failed", status = 400, detail = "Enter your username and password." });

        return Ok(await _identity.LoginAsync(req.UserNameOrEmail, req.Password, ct));
    }

    [AllowAnonymous]

    [HttpPost("2fa/verify")]
    public async Task<ActionResult<LoginResponse>> Verify2Fa([FromBody] TwoFactorVerifyRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        return Ok(await _identity.VerifyTwoFactorAsync(req.TwoFactorToken, req.Code, ct));
    }

    [Authorize]
    [DisableRateLimiting]
    [HttpPost("2fa/setup")]
    public async Task<ActionResult<TwoFactorSetupResponse>> Setup2Fa(CancellationToken ct)
    {
        if (_user.UserId is null) return Forbid();
        return Ok(await _identity.SetupTwoFactorAsync(_user.UserId.Value, ct));
    }

    [Authorize]
    [HttpPost("2fa/enable")]
    public async Task<IActionResult> Enable2Fa([FromBody] TwoFactorEnableRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        if (_user.UserId is null) return Forbid();
        await _identity.EnableTwoFactorAsync(_user.UserId.Value, req.Code, ct);
        return NoContent();
    }

    [Authorize]
    [HttpDelete("2fa")]
    public async Task<IActionResult> Disable2Fa(CancellationToken ct)
    {
        if (_user.UserId is null) return Forbid();
        await _identity.DisableTwoFactorAsync(_user.UserId.Value, ct);
        return NoContent();
    }

    [Authorize]
    [DisableRateLimiting]
    [HttpGet("2fa/status")]
    public async Task<ActionResult<TwoFactorStatusDto>> Get2FaStatus(CancellationToken ct)
    {
        if (_user.UserId is null) return Forbid();
        return Ok(await _identity.GetTwoFactorStatusAsync(_user.UserId.Value, ct));
    }

    [DisableRateLimiting]

    [AllowAnonymous]

    [HttpPost("refresh")]
    public async Task<ActionResult<TokenResult>> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        var result = await _identity.RefreshTokenAsync(req.RefreshToken, ct);
        return result is null ? Unauthorized() : Ok(result);
    }

    [DisableRateLimiting]

    [AllowAnonymous]

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        await _identity.LogoutAsync(req.RefreshToken, ct);
        return NoContent();
    }

    public record SetContextRequest(Guid? AgencyId, Guid? CallCenterId);

    /// <summary>
    /// Admin context picker: re-scope the current session to a chosen agency/call-center. The service
    /// validates the choice against the caller's own reach; null agency/center = "all". Returns a fresh
    /// LoginResponse (scoped tokens + a UserSummary reflecting the chosen scope).
    /// </summary>
    [Authorize]
    [DisableRateLimiting]
    [HttpPost("context")]
    public async Task<ActionResult<LoginResponse>> SetContext([FromBody] SetContextRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        if (_user.UserId is not { } uid) return Forbid();
        return Ok(await _identity.SetContextAsync(uid, req.AgencyId, req.CallCenterId, ct));
    }

    public record TwoFactorMethodBody(string Method);

    [Authorize]
    [DisableRateLimiting]
    [HttpPut("2fa/method")]
    public async Task<IActionResult> SetTwoFactorMethod(
        [FromBody] TwoFactorMethodBody body,
        [FromServices] MediatR.IMediator mediator,
        CancellationToken ct)
    {
        Guard.AgainstNull(body);
        Guard.AgainstNull(mediator);
        if (_user.UserId is null) return Forbid();
        await mediator.Send(new SetPreferred2FaCommand(_user.UserId.Value, body.Method), ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("2fa/email/send-otp")]
    public async Task<IActionResult> SendEmailOtp(
        [FromServices] SecondFactorRegistry registry,
        CancellationToken ct)
    {
        Guard.AgainstNull(registry);
        if (_user.UserId is null) return Forbid();
        var method = registry.Get(SecondFactorKind.EmailOtp);
        await method.ChallengeAsync(_user.UserId.Value, ct);
        return NoContent();
    }

    public record ForgotPasswordRequest(string Email);
    public record ResetPasswordRequest(string Email, string Token, string NewPassword);
    public record ConfirmEmailRequest(Guid UserId, string Token);
    public record ResendConfirmationRequest(string Email);

    [AllowAnonymous]

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        // Always 204 — never reveal whether the email is registered.
        await _identity.ForgotPasswordAsync(req.Email, ct);
        return NoContent();
    }

    [AllowAnonymous]

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        await _identity.ResetPasswordAsync(req.Email, req.Token, req.NewPassword, ct);
        return NoContent();
    }

    [AllowAnonymous]

    [HttpPost("email/confirm")]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        await _identity.ConfirmEmailAsync(req.UserId, req.Token, ct);
        return NoContent();
    }

    [AllowAnonymous]

    [HttpPost("email/resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        await _identity.SendEmailConfirmationAsync(req.Email, ct);
        return NoContent();
    }

    [Authorize]
    [DisableRateLimiting]
    [HttpGet("me")]
    public async Task<ActionResult<UserSummaryDto>> Me(CancellationToken ct)
    {
        if (_user.UserId is null) return Forbid();
        var u = await _identity.GetUserAsync(_user.UserId.Value, ct);
        return u is null ? NotFound() : Ok(u);
    }
}
