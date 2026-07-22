using CRM.Api.Authorization;
using CRM.Application.Auth.Dtos;
using CRM.Application.Common.Authorization;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Enums;
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

    public AuthController(IIdentityService identity, ICurrentUser user)
    {
        _identity = Guard.AgainstNull(identity);
        _user = Guard.AgainstNull(user);
    }

    /// <summary>AgencyId is optional — defaults to the calling admin's agency. Only a SuperAdmin can target a different agency.</summary>
    public record RegisterRequest(string Email, string UserName, string? Password, Guid? AgencyId, string[] Roles);
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
        return Ok(await _identity.RegisterAsync(req.Email, req.UserName, req.Password, agencyId, req.Roles, ct));
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

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        return Ok(await _identity.LoginAsync(req.UserNameOrEmail, req.Password, ct));
    }

    [HttpPost("2fa/verify")]
    public async Task<ActionResult<LoginResponse>> Verify2Fa([FromBody] TwoFactorVerifyRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        return Ok(await _identity.VerifyTwoFactorAsync(req.TwoFactorToken, req.Code, ct));
    }

    [Authorize]
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
    [HttpGet("2fa/status")]
    public async Task<ActionResult<TwoFactorStatusDto>> Get2FaStatus(CancellationToken ct)
    {
        if (_user.UserId is null) return Forbid();
        return Ok(await _identity.GetTwoFactorStatusAsync(_user.UserId.Value, ct));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<TokenResult>> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        var result = await _identity.RefreshTokenAsync(req.RefreshToken, ct);
        return result is null ? Unauthorized() : Ok(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        await _identity.LogoutAsync(req.RefreshToken, ct);
        return NoContent();
    }

    public record TwoFactorMethodBody(string Method);

    [Authorize]
    [HttpPut("2fa/method")]
    public async Task<IActionResult> SetTwoFactorMethod(
        [FromBody] TwoFactorMethodBody body,
        [FromServices] MediatR.IMediator mediator,
        CancellationToken ct)
    {
        Guard.AgainstNull(body);
        Guard.AgainstNull(mediator);
        if (_user.UserId is null) return Forbid();
        await mediator.Send(new CRM.Application.Users.Commands.SetPreferred2FaCommand(_user.UserId.Value, body.Method), ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("2fa/email/send-otp")]
    public async Task<IActionResult> SendEmailOtp(
        [FromServices] CRM.Infrastructure.Identity.SecondFactorRegistry registry,
        CancellationToken ct)
    {
        Guard.AgainstNull(registry);
        if (_user.UserId is null) return Forbid();
        var method = registry.Get(CRM.Application.Common.Interfaces.SecondFactorKind.EmailOtp);
        await method.ChallengeAsync(_user.UserId.Value, ct);
        return NoContent();
    }

    public record ForgotPasswordRequest(string Email);
    public record ResetPasswordRequest(string Email, string Token, string NewPassword);
    public record ConfirmEmailRequest(Guid UserId, string Token);
    public record ResendConfirmationRequest(string Email);

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        // Always 204 — never reveal whether the email is registered.
        await _identity.ForgotPasswordAsync(req.Email, ct);
        return NoContent();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        await _identity.ResetPasswordAsync(req.Email, req.Token, req.NewPassword, ct);
        return NoContent();
    }

    [HttpPost("email/confirm")]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        await _identity.ConfirmEmailAsync(req.UserId, req.Token, ct);
        return NoContent();
    }

    [HttpPost("email/resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequest req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        await _identity.SendEmailConfirmationAsync(req.Email, ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserSummaryDto>> Me(CancellationToken ct)
    {
        if (_user.UserId is null) return Forbid();
        var u = await _identity.GetUserAsync(_user.UserId.Value, ct);
        return u is null ? NotFound() : Ok(u);
    }
}
