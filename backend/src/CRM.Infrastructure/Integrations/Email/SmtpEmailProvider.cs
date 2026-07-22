using CRM.Application.Common.Integrations;
using CRM.Domain.Common;
using CRM.Infrastructure.Integrations;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CRM.Infrastructure.Integrations.Email;

public class SmtpEmailProvider : IEmailProvider
{
    private readonly EmailOptions _opts;
    private readonly ILogger<SmtpEmailProvider> _logger;

    public string Name => "Smtp";

    public SmtpEmailProvider(IOptions<IntegrationOptions> opts, ILogger<SmtpEmailProvider> logger)
    {
        _opts = Guard.AgainstNull(opts).Value.Email;
        _logger = Guard.AgainstNull(logger);
    }

    public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Guard.AgainstNull(message);
        if (string.IsNullOrEmpty(_opts.SmtpHost) || string.IsNullOrEmpty(_opts.Username))
        {
            // Dev fallback: surface confirm/reset links to the log so a developer can click them
            // without configuring SMTP. Matches the pattern from MandiOnline.
            var linkMatches = System.Text.RegularExpressions.Regex.Matches(
                message.Body, @"https?://[^\s""'<>]+(?:confirm-email|reset-password)[^\s""'<>]*");
            if (linkMatches.Count > 0)
            {
                foreach (System.Text.RegularExpressions.Match m in linkMatches)
                    _logger.LogWarning("📧 [DEV] Email to {To} — {Subject} — link: {Link}",
                        message.To, message.Subject, m.Value);
            }
            else
            {
                _logger.LogWarning("📧 [DEV] Email to {To} — {Subject} (SMTP not configured — set Integrations:Email:SmtpHost, Username, Password)",
                    message.To, message.Subject);
            }
            return new EmailResult(true, Guid.NewGuid().ToString("N"), "smtp-unconfigured");
        }

        try
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(message.FromName ?? _opts.FromName, _opts.FromAddress));
            msg.To.Add(MailboxAddress.Parse(message.To));
            msg.Subject = message.Subject;
            msg.Body = message.IsHtml
                ? new TextPart("html") { Text = message.Body }
                : new TextPart("plain") { Text = message.Body };

            using var client = new SmtpClient();
            // Skip cert-revocation check — common dev networks block the OCSP endpoint and
            // MailKit hard-fails on incomplete revocation status. Mandi does the same.
            client.CheckCertificateRevocation = false;
            client.ServerCertificateValidationCallback = (s, c, h, e) => true;

            // Port 465 = implicit SSL (Brevo, Gmail). 587 = STARTTLS. Auto-pick.
            var sslOpts = _opts.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(_opts.SmtpHost, _opts.SmtpPort, sslOpts, ct);
            await client.AuthenticateAsync(_opts.Username, _opts.Password ?? string.Empty, ct);
            await client.SendAsync(msg, ct);
            await client.DisconnectAsync(true, ct);

            _logger.LogInformation("Email sent to {To}: {Subject}", message.To, message.Subject);
            return new EmailResult(true, Guid.NewGuid().ToString("N"), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP send failed to {To}", message.To);
            return new EmailResult(false, null, ex.Message);
        }
    }
}
