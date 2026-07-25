using CRM.Application.Common.Integrations;
using CRM.Domain.Common;
using CRM.Infrastructure.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainRoles = CRM.Domain.Enums.Roles;

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

    /// <summary>
    /// Onboarding invitation. <paramref name="displayName"/>, <paramref name="agencyName"/> and
    /// <paramref name="callCenterName"/> are optional context rendered into the template — they
    /// are appended as optional parameters so existing callers are unaffected.
    /// </summary>
    public async Task SendInviteAsync(string to, string userName, string temporaryPassword, IEnumerable<string> roles, CancellationToken ct,
        string? displayName = null, string? agencyName = null, string? callCenterName = null)
    {
        var loginLink = $"{_opts.AppUrl.TrimEnd('/')}/login";
        var greeting = string.IsNullOrWhiteSpace(displayName) ? userName : displayName!;

        // Tailor the invite to the recipient's role ("level") — its label, a plain-language
        // description of what they can do, and the accent colour of the role badge.
        var (label, blurb, accent) = RoleProfile(roles);
        var subject = string.IsNullOrEmpty(label)
            ? $"You're invited to {_opts.FromName}"
            : $"You're invited to {_opts.FromName} — {label}";

        var labelRow = string.IsNullOrEmpty(label) ? "" : Row("Role", $"<strong style='font-size:15px'>{Html(label)}</strong>");
        var orgRow = string.IsNullOrWhiteSpace(agencyName) ? "" : Row("Agency", $"<strong style='font-size:15px'>{Html(agencyName!)}</strong>");
        var ccRow = string.IsNullOrWhiteSpace(callCenterName) ? "" : Row("Call center", $"<strong style='font-size:15px'>{Html(callCenterName!)}</strong>");
        var emailRow = Row("Email", $"<strong style='font-size:15px'>{Html(to)}</strong>");
        var usernameRow = Row("Username", $"<strong style='font-size:15px'>{Html(userName)}</strong>");
        var passwordRow = Row("Temporary password",
            $"<span style='font-family:ui-monospace,Menlo,monospace;font-size:15px;background:#fff;border:1px solid #e5e7eb;padding:6px 10px;border-radius:6px;display:inline-block;margin-top:4px'>{Html(temporaryPassword)}</span>",
            last: true);

        var badge = string.IsNullOrEmpty(label) ? "" :
            $"<div style='margin:2px 0 14px'><span style='display:inline-block;background:#f8fafc;color:{accent};border:1px solid {accent};border-radius:999px;padding:6px 14px;font-size:13px;font-weight:700'>{Html(label)}</span></div>";
        var intro = string.IsNullOrEmpty(label)
            ? $"<p>An account has been created for you on <strong>{Html(_opts.FromName)}</strong>.</p>"
            : $"<p>You've been invited to <strong>{Html(_opts.FromName)}</strong> as {Article(label)} <strong>{Html(label)}</strong>.</p>";
        var blurbHtml = string.IsNullOrEmpty(blurb) ? "" :
            $"<p style='color:#4b5563;background:#f9fafb;border-left:3px solid {accent};padding:12px 16px;border-radius:0 8px 8px 0;margin:16px 0'>{Html(blurb)}</p>";

        var body = Layout(subject, $@"
<p>Hi <strong>{Html(greeting)}</strong>,</p>
{badge}
{intro}
{blurbHtml}
<p>Sign in with <strong>either your email or your username</strong> and the temporary password below — you'll be asked to set your own password the first time you log in.</p>
<table cellpadding='0' cellspacing='0' style='width:100%;margin:20px 0;background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px'>{labelRow}{orgRow}{ccRow}{emailRow}{usernameRow}{passwordRow}</table>
{Button("Sign in", loginLink, accent)}
<p style='color:#6b7280;font-size:13px'>For your security, please change this password immediately after signing in. If you weren't expecting this invitation, you can safely ignore this email.</p>");
        var result = await _email.SendAsync(new EmailMessage(to, subject, body, IsHtml: true, FromName: _opts.FromName), ct);
        LogResult("invite", to, loginLink, result);
    }

    /// <summary>Role → (display label, one-line description, accent colour). Picks the most
    /// significant role when several are present. Add rows here as new roles are introduced.</summary>
    private static (string Label, string Blurb, string Accent) RoleProfile(IEnumerable<string> roles)
    {
        var set = new HashSet<string>(roles ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var (role, label, blurb, accent) in Profiles)
            if (set.Contains(role)) return (label, blurb, accent);
        return ("", "", "#1f7eff");
    }

    // Ordered most-significant first — the first match wins as the headline role.
    private static readonly (string Role, string Label, string Blurb, string Accent)[] Profiles =
    {
        (DomainRoles.SuperAdmin, "Super Admin", "You have full, cross-agency administration of the platform.", "#6d28d9"),
        (DomainRoles.CEO, "Agency CEO", "You lead your agency — you can create and manage its call centres and their teams.", "#6d28d9"),
        (DomainRoles.Admin, "Administrator", "You administer your agency's users, teams and settings.", "#dc2626"),
        (DomainRoles.CallCenterAdmin, "Call Center Admin", "You manage the users and profile of your call centre.", "#0369a1"),
        (DomainRoles.LicenseAgent, "License Agent", "You're an agency-level licensed agent — approved sales can be assigned to you, and you'll see the commission you earn.", "#0891b2"),
        (DomainRoles.Validator, "Submission Agent", "You review and approve submitted sales, then assign each approved sale to a licensed agent.", "#1f7eff"),
        (DomainRoles.SelfValidator, "Submission Agent", "You can validate and approve your own submitted sales.", "#1f7eff"),
        (DomainRoles.ProgramManager, "Program Manager", "You oversee operations and performance across teams.", "#dc2626"),
        (DomainRoles.QAManager, "QA Manager", "You run quality assurance reviews and scorecards.", "#ca8a04"),
        (DomainRoles.ProjectManager, "Project Manager", "You manage campaigns, workflows and day-to-day operations.", "#dc2626"),
        (DomainRoles.TechLead, "Tech Lead", "You manage workflows, scripts and integrations.", "#334155"),
        (DomainRoles.TeamLead, "Team Lead", "You supervise your team's pipeline and performance.", "#ca8a04"),
        (DomainRoles.Closer, "Closer", "You close verified leads into sales.", "#16a34a"),
        (DomainRoles.JrCloser, "Junior Closer", "You assist on closing calls and hand off to a closer.", "#16a34a"),
        (DomainRoles.Verifier, "Verifier", "You verify fronted leads before they reach a closer.", "#1f7eff"),
        (DomainRoles.Fronter, "Fronter", "You work new leads and front them into the pipeline.", "#1f7eff"),
        (DomainRoles.Followups, "Follow-ups", "You work follow-up and callback leads.", "#334155"),
        (DomainRoles.Correspondence, "Correspondence", "You handle customer correspondence.", "#334155"),
        (DomainRoles.Winbacks, "Winbacks", "You re-engage lost and win-back leads.", "#334155"),
    };

    private static string Article(string word) =>
        !string.IsNullOrEmpty(word) && "AEIOU".IndexOf(char.ToUpperInvariant(word[0])) >= 0 ? "an" : "a";

    private static string Row(string label, string valueHtml, bool last = false) =>
        $@"<tr><td style='padding:14px 18px{(last ? "" : ";border-bottom:1px solid #e5e7eb")}'><span style='color:#6b7280;font-size:12px;text-transform:uppercase;letter-spacing:0.04em'>{Html(label)}</span><br>{valueHtml}</td></tr>";

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
