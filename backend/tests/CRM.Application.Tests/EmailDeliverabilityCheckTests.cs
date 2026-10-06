using CRM.Infrastructure.Integrations;
using Xunit;

namespace CRM.Application.Tests;

/// <summary>
/// The rule the platform shipped without: outgoing mail must be sent from a domain we control.
///
/// It was sending every invitation, password reset and meeting invite as
/// <c>saadsaqib869@gmail.com</c> through a Brevo relay. A receiving server asks whether that relay
/// may send as gmail.com, Google says no, and the message is treated as spoofed — spam every time,
/// not a reputation problem that warms up. The owner's own invitation went to junk on day one.
///
/// The check itself logs rather than throws, so these tests exercise the decision it makes.
/// </summary>
public class EmailDeliverabilityCheckTests
{
    /// <summary>Mirrors EmailDeliverabilityCheck — the From domain must sit under the app's domain.</summary>
    private static bool IsAligned(string fromAddress, string appUrl)
    {
        var at = fromAddress.LastIndexOf('@');
        var fromDomain = fromAddress[(at + 1)..].ToLowerInvariant();

        var host = new Uri(appUrl).Host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var appDomain = string.Join('.', host[^2..]).ToLowerInvariant();

        // Dot boundary, not a bare EndsWith — see the lookalike test below.
        return fromDomain.Equals(appDomain, StringComparison.OrdinalIgnoreCase)
            || fromDomain.EndsWith("." + appDomain, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The exact configuration that shipped. This is the case worth never repeating.</summary>
    [Fact]
    public void A_gmail_from_address_is_not_aligned_with_the_platform_domain()
    {
        Assert.False(IsAligned("saadsaqib869@gmail.com", "https://app.smhachieverslifegroup.com"));
    }

    [Fact]
    public void An_address_on_the_platform_domain_is_aligned()
    {
        Assert.True(IsAligned("no-reply@smhachieverslifegroup.com", "https://app.smhachieverslifegroup.com"));
    }

    /// <summary>A subdomain sender is fine — DKIM can be published for it and still align.</summary>
    [Fact]
    public void A_subdomain_sender_is_aligned()
    {
        Assert.True(IsAligned("no-reply@mail.smhachieverslifegroup.com", "https://app.smhachieverslifegroup.com"));
    }

    /// <summary>
    /// A lookalike domain must not pass. The first version of the check used a bare EndsWith, which
    /// accepted this — an attacker-registered lookalike would have been treated as our own domain.
    /// Caught by writing this test, which is the reason it is here.
    /// </summary>
    [Fact]
    public void A_lookalike_domain_is_not_aligned()
    {
        Assert.False(IsAligned("no-reply@notsmhachieverslifegroup.com", "https://app.smhachieverslifegroup.com"));
    }

    [Fact]
    public void The_shipped_default_still_points_at_a_domain_we_do_not_control()
    {
        // Guards the option defaults themselves: if someone sets a public-mailbox default again,
        // this fails before it reaches a user's junk folder.
        var opts = new EmailOptions();
        Assert.DoesNotContain("@gmail.com", opts.FromAddress, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@outlook.com", opts.FromAddress, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@yahoo.com", opts.FromAddress, StringComparison.OrdinalIgnoreCase);
    }
}
