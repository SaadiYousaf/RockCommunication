using System.Globalization;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Interfaces;
using CRM.Application.Sales.Notifications;
using CRM.Domain.Common;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.Notifications;

/// <summary>
/// Builds and sends the customer policy-welcome email. Branding comes from the agency (name, logo,
/// reply-to); the send goes through the shared relay with the agency name as the From display name
/// so it always delivers, while replies route to the agency's own inbox. The logo is embedded inline
/// (cid:) so it renders even when the client blocks remote images. Wholly best-effort.
/// </summary>
public class PolicyWelcomeEmailSender : IPolicyWelcomeEmailSender
{
    private const string LogoContentId = "agencylogo";

    private readonly AppDbContext _db;
    private readonly IEmailProvider _email;
    private readonly IFileStorage _files;
    private readonly ILogger<PolicyWelcomeEmailSender> _logger;

    public PolicyWelcomeEmailSender(AppDbContext db, IEmailProvider email, IFileStorage files, ILogger<PolicyWelcomeEmailSender> logger)
    {
        _db = Guard.AgainstNull(db);
        _email = Guard.AgainstNull(email);
        _files = Guard.AgainstNull(files);
        _logger = Guard.AgainstNull(logger);
    }

    public async Task<bool> SendAsync(PolicyWelcomeEmailRequest request, CancellationToken ct = default)
    {
        Guard.AgainstNull(request);
        if (string.IsNullOrWhiteSpace(request.CustomerEmail)) return false;

        try
        {
            // Agency is not tenant-filtered (BaseEntity), so a direct read is safe even for a central
            // Submission Agent acting cross-agency.
            var agency = await _db.Agencies.AsNoTracking()
                .Where(a => a.Id == request.AgencyId)
                .Select(a => new { a.Name, a.SenderEmail, a.LogoKey })
                .FirstOrDefaultAsync(ct);
            if (agency is null) return false;

            var agencyName = string.IsNullOrWhiteSpace(agency.Name) ? "Your agency" : agency.Name;

            // Load the logo bytes (best-effort) for an inline cid: image.
            EmailAttachment? logo = null;
            string? logoHtml = null;
            if (!string.IsNullOrWhiteSpace(agency.LogoKey))
            {
                try
                {
                    await using var s = await _files.OpenReadAsync(agency.LogoKey!, ct);
                    using var ms = new MemoryStream();
                    await s.CopyToAsync(ms, ct);
                    logo = new EmailAttachment(
                        FileName: "logo" + Path.GetExtension(agency.LogoKey),
                        ContentType: ContentTypeFor(agency.LogoKey!),
                        Content: ms.ToArray(),
                        ContentId: LogoContentId);
                    logoHtml = $"<img src=\"cid:{LogoContentId}\" alt=\"{Html(agencyName)}\" style=\"max-height:48px;max-width:200px;display:block\">";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Welcome email: could not load agency logo {Key}", agency.LogoKey);
                }
            }

            var subject = $"Welcome — your {request.Carrier} policy is approved";
            var body = BuildHtml(agencyName, logoHtml, request);

            var result = await _email.SendAsync(new EmailMessage(
                To: request.CustomerEmail.Trim(),
                Subject: subject,
                Body: body,
                IsHtml: true,
                FromName: agencyName,
                Attachments: logo is null ? null : new[] { logo },
                ReplyTo: string.IsNullOrWhiteSpace(agency.SenderEmail) ? null : agency.SenderEmail!.Trim()), ct);

            if (!result.Sent)
                _logger.LogWarning("Welcome email to {To} not sent: {Reason}", request.CustomerEmail, result.Reason);
            return result.Sent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Welcome email failed for agency {AgencyId} to {To}", request.AgencyId, request.CustomerEmail);
            return false;   // never bubble up — the approval already succeeded
        }
    }

    private static string BuildHtml(string agencyName, string? logoHtml, PolicyWelcomeEmailRequest r)
    {
        var header = logoHtml ?? $"<h1 style='margin:0;font-size:22px;font-weight:700;color:#111827'>{Html(agencyName)}</h1>";
        var greeting = string.IsNullOrWhiteSpace(r.CustomerName) ? "Hello," : $"Hi {Html(r.CustomerName)},";

        var rows =
            Row("Carrier", Html(r.Carrier)) +
            (r.Plan is { Length: > 0 } ? Row("Plan", Html(r.Plan)) : "") +
            (r.Coverage is { } cov ? Row("Coverage", Money(cov)) : "") +
            (r.Premium is { } prem ? Row("Monthly premium", Money(prem)) : "");

        return $@"
<!doctype html><html><body style='margin:0;padding:0;background:#f3f4f6;font-family:-apple-system,Segoe UI,Arial,sans-serif'>
<table width='100%' cellpadding='0' cellspacing='0' style='background:#f3f4f6'>
<tr><td align='center' style='padding:32px 16px'>
<table width='600' cellpadding='0' cellspacing='0' style='max-width:600px;width:100%'>
<tr><td style='background:#ffffff;padding:24px 32px;border:1px solid #e5e7eb;border-bottom:none;border-radius:12px 12px 0 0'>{header}</td></tr>
<tr><td style='background:#fff;padding:8px 32px 28px;border-left:1px solid #e5e7eb;border-right:1px solid #e5e7eb;color:#374151;font-size:15px;line-height:1.7'>
  <p style='margin:16px 0 4px'>{greeting}</p>
  <p style='margin:0 0 8px'>Welcome, and congratulations — your policy has been <strong>approved</strong>. Here are your policy details:</p>
  <table width='100%' cellpadding='0' cellspacing='0' style='margin:16px 0;border:1px solid #e5e7eb;border-radius:10px;overflow:hidden'>{rows}</table>
  <p style='margin:12px 0 0'>We're glad to have you on board. If you have any questions, just reply to this email and our team will help.</p>
  <p style='margin:16px 0 0'>Warm regards,<br><strong>{Html(agencyName)}</strong></p>
</td></tr>
<tr><td style='background:#f9fafb;padding:16px 32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 12px 12px;text-align:center;color:#9ca3af;font-size:12px'>
  © {DateTime.UtcNow.Year} {Html(agencyName)}
</td></tr></table></td></tr></table></body></html>";
    }

    private static string Row(string label, string valueHtml) =>
        $@"<tr><td style='padding:12px 16px;border-bottom:1px solid #f0f1f3'><span style='color:#6b7280;font-size:12px;text-transform:uppercase;letter-spacing:0.04em'>{Html(label)}</span><br><span style='color:#111827;font-size:15px;font-weight:600'>{valueHtml}</span></td></tr>";

    private static string Money(decimal v) => v.ToString("C2", CultureInfo.GetCultureInfo("en-US"));

    private static string ContentTypeFor(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    private static string Html(string s) => System.Net.WebUtility.HtmlEncode(s);
}
