using System.Globalization;
using System.Net;
using CRM.Application.Common.Integrations;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CRM.Application.Scheduling;

/// <summary>
/// Sends meeting invitation / cancellation emails — one rich HTML message per attendee.
/// Every send is <b>best-effort</b>: a failure for one recipient is logged and swallowed and
/// never propagates, so email trouble can never fail the command that scheduled the meeting.
/// </summary>
public interface IMeetingInviteSender
{
    Task SendInvitesAsync(Meeting meeting, string organizerName, CancellationToken ct = default);
    Task SendCancellationsAsync(Meeting meeting, string organizerName, CancellationToken ct = default);
}

/// <summary>
/// One-purpose helper that owns the invite/cancellation email format. It talks to
/// <see cref="IEmailProvider"/> directly (per the module's design) and guarantees graceful
/// failure: the public methods never throw.
/// </summary>
public class MeetingInviteSender : IMeetingInviteSender
{
    private readonly IEmailProvider _email;
    private readonly ILogger<MeetingInviteSender> _logger;

    public MeetingInviteSender(IEmailProvider email, ILogger<MeetingInviteSender> logger)
    {
        _email = Guard.AgainstNull(email);
        _logger = Guard.AgainstNull(logger);
    }

    public Task SendInvitesAsync(Meeting meeting, string organizerName, CancellationToken ct = default)
        => SendAllAsync(meeting, organizerName, cancelled: false, ct);

    public Task SendCancellationsAsync(Meeting meeting, string organizerName, CancellationToken ct = default)
        => SendAllAsync(meeting, organizerName, cancelled: true, ct);

    private async Task SendAllAsync(Meeting meeting, string organizerName, bool cancelled, CancellationToken ct)
    {
        // Defensive: this helper is invoked from a try/catch in the handler too, but guard here
        // so a null slip can never bubble a NullReferenceException out of a best-effort send.
        if (meeting is null) return;
        organizerName = string.IsNullOrWhiteSpace(organizerName) ? "The organizer" : organizerName.Trim();

        var subject = (cancelled ? SchedulingDefaults.CancellationSubjectPrefix : SchedulingDefaults.InviteSubjectPrefix)
            + meeting.Title;
        var body = BuildHtml(meeting, organizerName, cancelled);

        // A real iCalendar (.ics) attachment so the invite lands in the recipient's calendar app
        // ("Add to calendar" in Gmail/Outlook/Apple Mail). METHOD:REQUEST for a new invite,
        // METHOD:CANCEL for a cancellation — same UID so clients update the existing event.
        var ics = BuildIcs(meeting, organizerName, cancelled);
        var attachment = new EmailAttachment(
            cancelled ? "cancelled.ics" : "invite.ics",
            $"text/calendar; method={(cancelled ? "CANCEL" : "REQUEST")}; charset=utf-8",
            System.Text.Encoding.UTF8.GetBytes(ics));

        foreach (var attendee in meeting.Attendees)
        {
            if (string.IsNullOrWhiteSpace(attendee?.Email)) continue;
            try
            {
                var result = await _email.SendAsync(
                    new EmailMessage(attendee!.Email, subject, body, IsHtml: true,
                        FromName: SchedulingDefaults.ProductName, Attachments: new[] { attachment }), ct);
                if (!result.Sent)
                    _logger.LogWarning("Meeting {Kind} email to {To} for meeting {MeetingId} was not sent: {Reason}",
                        cancelled ? "cancellation" : "invite", attendee.Email, meeting.Id, result.Reason);
            }
            catch (Exception ex)
            {
                // Swallow — email is advisory; the meeting is already persisted.
                _logger.LogError(ex, "Failed to send meeting {Kind} email to {To} for meeting {MeetingId}",
                    cancelled ? "cancellation" : "invite", attendee.Email, meeting.Id);
            }
        }
    }

    // ── HTML builder ────────────────────────────────────────────────────────────

    private static string BuildHtml(Meeting m, string organizerName, bool cancelled)
    {
        var accent = cancelled ? "#dc2626" : "#1f7eff";
        var heading = cancelled ? "Meeting cancelled" : "You're invited";
        var when = FormatRange(m.StartsAt, m.EndsAt);

        var locationRow = !string.IsNullOrWhiteSpace(m.Location)
            ? Row("Where", Encode(m.Location!))
            : "";
        var onlineRow = !string.IsNullOrWhiteSpace(m.OnlineUrl)
            ? Row("Join online", $"<a href=\"{Encode(m.OnlineUrl!)}\" style=\"color:{accent};word-break:break-all\">{Encode(m.OnlineUrl!)}</a>")
            : "";
        var descBlock = !string.IsNullOrWhiteSpace(m.Description)
            ? $"<p style=\"color:#4b5563;background:#f9fafb;border-left:3px solid {accent};padding:12px 16px;border-radius:0 8px 8px 0;margin:16px 0;white-space:pre-wrap\">{Encode(m.Description!)}</p>"
            : "";
        var titleStyle = cancelled ? "text-decoration:line-through;color:#9ca3af" : "color:#111827";
        var calendarButton = cancelled ? "" :
            $"<div style=\"text-align:center;margin:24px 0 4px\"><a href=\"{GoogleCalendarLink(m)}\" style=\"background:{accent};color:#fff;text-decoration:none;padding:12px 26px;border-radius:8px;font-size:14px;font-weight:700;display:inline-block\">Add to Google Calendar</a></div>";

        return $@"
<!doctype html><html><body style=""margin:0;padding:0;background:#f3f4f6;font-family:-apple-system,Segoe UI,Arial,sans-serif"">
<table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f3f4f6"">
<tr><td align=""center"" style=""padding:32px 16px"">
<table width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%"">
<tr><td style=""background:{accent};padding:20px 32px;border-radius:12px 12px 0 0;color:#fff"">
  <div style=""font-size:12px;text-transform:uppercase;letter-spacing:0.12em;opacity:0.85"">{Encode(heading)}</div>
  <h1 style=""margin:6px 0 0;font-size:20px;font-weight:700;{titleStyle};color:#fff"">{Encode(m.Title)}</h1>
</td></tr>
<tr><td style=""background:#fff;padding:28px 32px;border:1px solid #e5e7eb;color:#374151;font-size:15px;line-height:1.7"">
  <p style=""margin:0 0 16px"">{(cancelled
        ? $"<strong>{Encode(organizerName)}</strong> has cancelled the following meeting."
        : $"<strong>{Encode(organizerName)}</strong> has invited you to a meeting.")}</p>
  <table cellpadding=""0"" cellspacing=""0"" style=""width:100%;background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px"">
    {Row("When", Encode(when))}
    {locationRow}
    {onlineRow}
    {Row("Organizer", Encode(organizerName), last: true)}
  </table>
  {descBlock}
  {calendarButton}
  <p style=""color:#9ca3af;font-size:12px;margin-top:20px"">Times are shown in UTC. Please convert to your local time zone.</p>
</td></tr>
<tr><td style=""background:#f9fafb;padding:14px 32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 12px 12px;text-align:center;color:#9ca3af;font-size:12px"">
  Sent by {Encode(SchedulingDefaults.ProductName)}
</td></tr></table></td></tr></table></body></html>";
    }

    private static string Row(string label, string valueHtml, bool last = false) =>
        $"<tr><td style=\"padding:12px 18px{(last ? "" : ";border-bottom:1px solid #e5e7eb")}\">" +
        $"<span style=\"color:#6b7280;font-size:12px;text-transform:uppercase;letter-spacing:0.04em\">{Encode(label)}</span><br>" +
        $"<span style=\"font-size:15px;color:#111827\">{valueHtml}</span></td></tr>";

    private static string FormatRange(DateTime startUtc, DateTime endUtc)
    {
        var start = startUtc.ToString("dddd, dd MMM yyyy · HH:mm", CultureInfo.InvariantCulture);
        // Same day → show only the end time; otherwise repeat the date.
        var end = startUtc.Date == endUtc.Date
            ? endUtc.ToString("HH:mm", CultureInfo.InvariantCulture)
            : endUtc.ToString("dd MMM yyyy · HH:mm", CultureInfo.InvariantCulture);
        return $"{start} – {end} UTC";
    }

    /// <summary>An "add to calendar" link for Google Calendar (opens a pre-filled event).</summary>
    private static string GoogleCalendarLink(Meeting m)
    {
        static string Stamp(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var details = m.Description ?? "";
        if (!string.IsNullOrWhiteSpace(m.OnlineUrl))
            details = string.IsNullOrWhiteSpace(details) ? $"Join: {m.OnlineUrl}" : $"{details}\n\nJoin: {m.OnlineUrl}";
        var url = "https://calendar.google.com/calendar/render?action=TEMPLATE"
            + $"&text={Uri.EscapeDataString(m.Title)}"
            + $"&dates={Stamp(m.StartsAt)}/{Stamp(m.EndsAt)}"
            + $"&details={Uri.EscapeDataString(details)}"
            + $"&location={Uri.EscapeDataString(m.Location ?? "")}";
        // The href sits inside a double-quoted HTML attribute, so encode the ampersands.
        return url.Replace("&", "&amp;");
    }

    private static string Encode(string s) => WebUtility.HtmlEncode(s);

    // ── iCalendar (.ics) builder ──────────────────────────────────────────────────

    /// <summary>Builds an RFC-5545 VEVENT (METHOD:REQUEST, or CANCEL when cancelled) for the meeting.</summary>
    private static string BuildIcs(Meeting m, string organizerName, bool cancelled)
    {
        static string Stamp(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        // RFC-5545 text escaping: backslash, semicolon, comma, and newlines.
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
            .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

        var sb = new System.Text.StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append($"PRODID:-//{Esc(SchedulingDefaults.ProductName)}//Scheduler//EN\r\n");
        sb.Append($"METHOD:{(cancelled ? "CANCEL" : "REQUEST")}\r\n");
        sb.Append("BEGIN:VEVENT\r\n");
        sb.Append($"UID:meeting-{m.Id}@rockcommunication\r\n");
        sb.Append($"DTSTAMP:{Stamp(DateTime.UtcNow)}\r\n");
        sb.Append($"DTSTART:{Stamp(m.StartsAt)}\r\n");
        sb.Append($"DTEND:{Stamp(m.EndsAt)}\r\n");
        sb.Append($"SUMMARY:{Esc(m.Title)}\r\n");
        if (!string.IsNullOrWhiteSpace(m.Description))
            sb.Append($"DESCRIPTION:{Esc(m.Description!)}\r\n");
        var location = !string.IsNullOrWhiteSpace(m.OnlineUrl) ? m.OnlineUrl! : (m.Location ?? "");
        if (!string.IsNullOrWhiteSpace(location))
            sb.Append($"LOCATION:{Esc(location)}\r\n");
        sb.Append($"ORGANIZER;CN={Esc(organizerName)}:mailto:noreply@meetings.local\r\n");
        foreach (var a in m.Attendees)
            if (!string.IsNullOrWhiteSpace(a?.Email))
                sb.Append($"ATTENDEE;CN={Esc(a!.Email)};RSVP=TRUE:mailto:{a.Email}\r\n");
        sb.Append($"STATUS:{(cancelled ? "CANCELLED" : "CONFIRMED")}\r\n");
        sb.Append($"SEQUENCE:{(cancelled ? 1 : 0)}\r\n");
        sb.Append("END:VEVENT\r\n");
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }
}
