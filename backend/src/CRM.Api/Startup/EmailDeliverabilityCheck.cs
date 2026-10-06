using CRM.Domain.Common;
using CRM.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace CRM.Api.Startup;

/// <summary>
/// Checks, at startup, that outgoing mail can actually be authenticated as coming from us.
///
/// WHY THIS EXISTS: the platform shipped sending every invitation, password reset and meeting
/// invite with <c>From: saadsaqib869@gmail.com</c> through a Brevo relay. A receiving server asks
/// "is this relay allowed to send as gmail.com?", Google says no, and the message lands in spam —
/// every time, by design, with no amount of warming up to fix it. The platform owner's own
/// invitation went to junk on day one and the cause went unnoticed for weeks, because nothing
/// anywhere said the From address belonged to a domain we do not control.
///
/// This is a warning, never a refusal. Mail that lands in spam is bad; an API that will not start
/// is worse, and an installation may legitimately be mid-migration between domains.
/// </summary>
public static class EmailDeliverabilityCheck
{
    public static void Run(IServiceProvider services, ILogger logger, IHostEnvironment env)
    {
        Guard.AgainstNull(services);
        Guard.AgainstNull(logger);
        Guard.AgainstNull(env);

        try
        {
            var opts = services.GetService<IOptions<IntegrationOptions>>()?.Value?.Email;
            if (opts is null) return;

            // A stub provider doesn't send anything, so none of this applies.
            if (string.Equals(opts.Provider, "Stub", StringComparison.OrdinalIgnoreCase)) return;

            var fromDomain = DomainOf(opts.FromAddress);
            var appDomain = RegistrableDomainOf(opts.AppUrl);
            if (fromDomain is null || appDomain is null) return;

            // Local development sends to a catcher; nobody is checking SPF on localhost.
            if (env.IsDevelopment()) return;

            // The dot boundary matters: a plain EndsWith would accept
            // "notsmhachieverslifegroup.com" as a match for "smhachieverslifegroup.com".
            var aligned = fromDomain.Equals(appDomain, StringComparison.OrdinalIgnoreCase)
                || fromDomain.EndsWith("." + appDomain, StringComparison.OrdinalIgnoreCase);

            if (!aligned)
            {
                logger.LogError(
                    "EMAIL DELIVERABILITY: mail is sent as {From} but this platform is {AppDomain}. " +
                    "A receiving server checks whether our relay is allowed to send as {FromDomain} — " +
                    "it is not, so SPF and DKIM cannot align and the message is treated as spoofed. " +
                    "Invitations, password resets and meeting invites will land in spam. " +
                    "Fix: authenticate {AppDomain} with the mail provider, then set " +
                    "Integrations:Email:FromAddress to an address on {AppDomain}. " +
                    "See docs/EMAIL-DELIVERABILITY.md.",
                    opts.FromAddress, appDomain, fromDomain, appDomain, appDomain);
            }

            if (!string.IsNullOrWhiteSpace(opts.SupportEmail))
            {
                var supportDomain = DomainOf(opts.SupportEmail);
                if (supportDomain is not null && !supportDomain.Equals(appDomain, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "EMAIL: the support address shown to users ({Support}) is not on {AppDomain}.",
                        opts.SupportEmail, appDomain);
                }
            }
        }
        catch (Exception ex)
        {
            // A configuration check must never be the reason the platform fails to come up.
            logger.LogWarning(ex, "Could not run the email deliverability check.");
        }
    }

    private static string? DomainOf(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var at = address.LastIndexOf('@');
        return at < 0 || at == address.Length - 1 ? null : address[(at + 1)..].Trim().ToLowerInvariant();
    }

    /// <summary>
    /// The registrable part of the app's host — "app.example.com" gives "example.com", so a From
    /// address on the bare domain still counts as matching.
    /// </summary>
    private static string? RegistrableDomainOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var parts = uri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        return string.Join('.', parts[^2..]).ToLowerInvariant();
    }
}
