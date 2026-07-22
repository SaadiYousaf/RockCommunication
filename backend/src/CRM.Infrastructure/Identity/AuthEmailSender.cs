using CRM.Application.Common.Integrations;
using CRM.Domain.Common;
using CRM.Infrastructure.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CRM.Infrastructure.Identity;

/// <summary>Builds and dispatches transactional auth emails (confirm, reset).</summary>
public class AuthEmailSender
{
    private readonly IEmailProvider _email;
    private readonly EmailOptions _opts;
    private readonly ILogger<AuthEmailSender> _logger;

    public AuthEmailSender(IEmailProvider email, IOptions<IntegrationOptions> opts, ILogger<AuthEmailSender> logger)
    {
        _email = Guard.AgainstNull(email);
        _opts = Guard.AgainstNull(opts).Value.Email;
        _logger = Guard.AgainstNull(logger);
    }

    public async Task SendEmailConfirmationAsync(string to, string userName, Guid userId, string token, CancellationToken ct)
    {
        var link = $"{_opts.AppUrl.TrimEnd('/')}/confirm-email?userId={userId}&token={Uri.EscapeDataString(token)}";
        var subject = $"Confirm your {_opts.FromName} account";
        var body = Layout(subject, $@"
<p>Hi <strong>{Html(userName)}</strong>,</p>
<p>Welcome to {Html(_opts.FromName)}. Please confirm your email address to activate your account:</p>
{Button("Confirm email", link, "#1f7eff")}
<p style='color:#6b7280;font-size:13px'>This link expires in <strong>24 hours</strong>. If you didn't create this account, you can safely ignore this email.</p>
<p style='color:#9ca3af;font-size:12px;word-break:break-all'>Or copy this link: {link}</p>");
        var result = await _email.SendAsync(new EmailMessage(to, subject, body, IsHtml: true, FromName: _opts.FromName), ct);
        LogResult("confirmation", to, link, result);
    }

    public async Task SendPasswordResetAsync(string to, string userName, string email, string token, CancellationToken ct)
    {
        var link = $"{_opts.AppUrl.TrimEnd('/')}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        var subject = $"Reset your {_opts.FromName} password";
        var body = Layout(subject, $@"
<p>Hi <strong>{Html(userName)}</strong>,</p>
<p>We received a request to reset your password. Click the button below to choose a new one:</p>
{Button("Reset password", link, "#dc2626")}
<p style='color:#6b7280;font-size:13px'>This link expires in <strong>30 minutes</strong>. If you didn't request this, ignore this email — your password will not change.</p>
<p style='color:#9ca3af;font-size:12px;word-break:break-all'>Or copy this link: {link}</p>");
        var result = await _email.SendAsync(new EmailMessage(to, subject, body, IsHtml: true, FromName: _opts.FromName), ct);
        LogResult("reset", to, link, result);
    }

    public async Task SendInviteAsync(string to, string userName, string temporaryPassword, IEnumerable<string> roles, CancellationToken ct)
    {
        var loginLink = $"{_opts.AppUrl.TrimEnd('/')}/login";
        var subject = $"You're invited to {_opts.FromName}";
        var roleList = string.Join(", ", roles.Select(Html));
        var body = Layout(subject, $@"
<p>Hi <strong>{Html(userName)}</strong>,</p>
<p>An admin has created an account for you on <strong>{Html(_opts.FromName)}</strong>{(string.IsNullOrEmpty(roleList) ? "" : $" with the role(s) <strong>{roleList}</strong>")}.</p>
<p>Sign in using the temporary password below — you'll be asked to choose a new password the first time you log in.</p>
<table cellpadding='0' cellspacing='0' style='width:100%;margin:20px 0;background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px'>
  <tr><td style='padding:14px 18px;border-bottom:1px solid #e5e7eb'><span style='color:#6b7280;font-size:12px;text-transform:uppercase;letter-spacing:0.04em'>Username</span><br><strong style='font-size:15px'>{Html(userName)}</strong></td></tr>
  <tr><td style='padding:14px 18px'><span style='color:#6b7280;font-size:12px;text-transform:uppercase;letter-spacing:0.04em'>Temporary password</span><br><span style='font-family:ui-monospace,Menlo,monospace;font-size:15px;background:#fff;border:1px solid #e5e7eb;padding:6px 10px;border-radius:6px;display:inline-block;margin-top:4px'>{Html(temporaryPassword)}</span></td></tr>
</table>
{Button("Sign in", loginLink, "#1f7eff")}
<p style='color:#6b7280;font-size:13px'>For your security, change this password immediately after signing in. If you weren't expecting this invitation, please ignore it.</p>");
        var result = await _email.SendAsync(new EmailMessage(to, subject, body, IsHtml: true, FromName: _opts.FromName), ct);
        LogResult("invite", to, loginLink, result);
    }

    private void LogResult(string kind, string to, string link, EmailResult result)
    {
        if (result.Sent)
            _logger.LogInformation("Auth email ({Kind}) dispatched to {To} (provider id {Id})", kind, to, result.ProviderMessageId);
        else
            _logger.LogError("Auth email ({Kind}) FAILED to {To}: {Error} — link was: {Link}", kind, to, result.Reason, link);
    }

    private string Layout(string subject, string content) => $@"
<!doctype html><html><body style='margin:0;padding:0;background:#f3f4f6;font-family:-apple-system,Segoe UI,Arial,sans-serif'>
<table width='100%' cellpadding='0' cellspacing='0' style='background:#f3f4f6'>
<tr><td align='center' style='padding:32px 16px'>
<table width='600' cellpadding='0' cellspacing='0' style='max-width:600px;width:100%'>
<tr><td style='background:linear-gradient(135deg,#1f7eff,#6d28d9);padding:24px 32px;border-radius:12px 12px 0 0;color:#fff'>
  <h1 style='margin:0;font-size:20px;font-weight:700'>{Html(_opts.FromName)}</h1>
</td></tr>
<tr><td style='background:#fff;padding:32px;border:1px solid #e5e7eb;color:#374151;font-size:15px;line-height:1.7'>{content}</td></tr>
<tr><td style='background:#f9fafb;padding:16px 32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 12px 12px;text-align:center;color:#9ca3af;font-size:12px'>
  © {DateTime.UtcNow.Year} {Html(_opts.FromName)} · <a href='mailto:{Html(_opts.SupportEmail)}' style='color:#9ca3af'>{Html(_opts.SupportEmail)}</a>
</td></tr></table></td></tr></table></body></html>";

    private static string Button(string text, string url, string color) =>
        $"<div style='text-align:center;margin:28px 0'><a href='{url}' style='background:{color};color:#fff;text-decoration:none;padding:14px 32px;border-radius:8px;font-size:15px;font-weight:700;display:inline-block'>{text}</a></div>";

    private static string Html(string s) => System.Net.WebUtility.HtmlEncode(s);
}
