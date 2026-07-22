using CRM.Application.Common.Assignment;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Notifications;
using CRM.Application.Common.Workflow;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace CRM.Infrastructure.Workflow;

public class AssignAgentAction : IWorkflowAction
{
    public string ActionType => "assign-agent";
    private readonly AppDbContext _db;
    private readonly IAssignmentService _assignment;

    public AssignAgentAction(AppDbContext db, IAssignmentService assignment)
    {
        _db = Guard.AgainstNull(db); _assignment = Guard.AgainstNull(assignment);
    }

    public async Task ExecuteAsync(IWorkflowEvent ev, IReadOnlyDictionary<string, object?> p, CancellationToken ct = default)
    {
        if (!ev.Facts.TryGetValue("leadId", out var v) || v is null) return;
        var leadId = Guid.Parse(v.ToString()!);
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return;

        var role = p.TryGetValue("role", out var r) ? r?.ToString() ?? Roles.Fronter : Roles.Fronter;
        var strategy = p.TryGetValue("strategy", out var s) ? s?.ToString() ?? "round-robin" : "round-robin";
        await _assignment.AssignAsync(lead, role, strategy, ct);
        await _db.SaveChangesAsync(ct);
    }
}

public class MoveStageAction : IWorkflowAction
{
    public string ActionType => "move-stage";
    private readonly AppDbContext _db;
    public MoveStageAction(AppDbContext db) => _db = Guard.AgainstNull(db);

    public async Task ExecuteAsync(IWorkflowEvent ev, IReadOnlyDictionary<string, object?> p, CancellationToken ct = default)
    {
        if (!ev.Facts.TryGetValue("leadId", out var v) || v is null) return;
        if (!p.TryGetValue("toStage", out var toRaw) || toRaw is null) return;
        if (!Enum.TryParse<WorkflowStage>(toRaw.ToString(), true, out var toStage)) return;

        var leadId = Guid.Parse(v.ToString()!);
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return;
        lead.Stage = toStage;
        await _db.SaveChangesAsync(ct);
    }
}

public class SendSmsWorkflowAction : IWorkflowAction
{
    public string ActionType => "send-sms";
    private readonly ISmsProvider _sms;
    private readonly AppDbContext _db;
    public SendSmsWorkflowAction(ISmsProvider sms, AppDbContext db) { _sms = Guard.AgainstNull(sms); _db = Guard.AgainstNull(db); }

    public async Task ExecuteAsync(IWorkflowEvent ev, IReadOnlyDictionary<string, object?> p, CancellationToken ct = default)
    {
        var template = p.TryGetValue("template", out var t) ? t?.ToString() ?? "" : "";
        if (string.IsNullOrEmpty(template)) return;

        var phone = p.TryGetValue("phone", out var ph) ? ph?.ToString() : null;
        if (string.IsNullOrEmpty(phone) && ev.Facts.TryGetValue("leadId", out var lid) && lid is not null)
        {
            var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == Guid.Parse(lid.ToString()!), ct);
            phone = lead?.PhoneNumber;
        }
        if (string.IsNullOrEmpty(phone)) return;

        var body = TemplateRenderer.Render(template, ev.Facts);
        await _sms.SendAsync(new SmsMessage(phone, body), ct);
    }
}

public class SendEmailWorkflowAction : IWorkflowAction
{
    public string ActionType => "send-email";
    private readonly IEmailProvider _email;
    private readonly AppDbContext _db;
    public SendEmailWorkflowAction(IEmailProvider email, AppDbContext db) { _email = Guard.AgainstNull(email); _db = Guard.AgainstNull(db); }

    public async Task ExecuteAsync(IWorkflowEvent ev, IReadOnlyDictionary<string, object?> p, CancellationToken ct = default)
    {
        var subject = p.TryGetValue("subject", out var s) ? s?.ToString() ?? "" : "";
        var body = p.TryGetValue("body", out var b) ? b?.ToString() ?? "" : "";
        var to = p.TryGetValue("to", out var t) ? t?.ToString() : null;
        if (string.IsNullOrEmpty(to) && ev.Facts.TryGetValue("leadId", out var lid) && lid is not null)
        {
            var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == Guid.Parse(lid.ToString()!), ct);
            to = lead?.Email;
        }
        if (string.IsNullOrEmpty(to)) return;
        await _email.SendAsync(new EmailMessage(to,
            TemplateRenderer.Render(subject, ev.Facts),
            TemplateRenderer.Render(body, ev.Facts)), ct);
    }
}

public class CreateCallbackAction : IWorkflowAction
{
    public string ActionType => "create-callback";
    private readonly AppDbContext _db;
    public CreateCallbackAction(AppDbContext db) => _db = Guard.AgainstNull(db);

    public async Task ExecuteAsync(IWorkflowEvent ev, IReadOnlyDictionary<string, object?> p, CancellationToken ct = default)
    {
        if (!ev.Facts.TryGetValue("leadId", out var v) || v is null) return;
        var leadId = Guid.Parse(v.ToString()!);
        var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead?.AssignedUserId is null) return;

        var minutes = p.TryGetValue("delayMinutes", out var m) && m is not null
            && int.TryParse(m.ToString(), out var mi) ? mi : 60;

        _db.ScheduledCallbacks.Add(new ScheduledCallback
        {
            AgencyId = lead.AgencyId,
            LeadId = lead.Id,
            AssignedUserId = lead.AssignedUserId.Value,
            ScheduledFor = DateTime.UtcNow.AddMinutes(minutes),
            Reason = p.TryGetValue("reason", out var r) ? r?.ToString() : "Auto-scheduled by workflow"
        });
        await _db.SaveChangesAsync(ct);
    }
}

public class WebhookWorkflowAction : IWorkflowAction
{
    public string ActionType => "webhook";
    private readonly HttpClient _http;
    private readonly ILogger<WebhookWorkflowAction> _logger;

    public WebhookWorkflowAction(HttpClient http, ILogger<WebhookWorkflowAction> logger)
    {
        _http = Guard.AgainstNull(http);
        _logger = Guard.AgainstNull(logger);
    }

    public async Task ExecuteAsync(IWorkflowEvent ev, IReadOnlyDictionary<string, object?> p, CancellationToken ct = default)
    {
        var url = p.TryGetValue("url", out var u) ? u?.ToString() : null;
        if (string.IsNullOrEmpty(url)) return;

        // SSRF guard: the URL is supplied by a tenant admin via the workflow rule, so it is
        // untrusted. Only allow absolute http(s) URLs whose resolved address is a public
        // host — never loopback, link-local (incl. 169.254.169.254 cloud metadata), or
        // private ranges. (Residual DNS-rebinding TOCTOU is accepted for now.)
        var target = await ResolveSafeAsync(url, ct);
        if (target is null)
        {
            _logger.LogWarning("Webhook action blocked unsafe or unresolvable URL");
            return;
        }

        var payload = JsonSerializer.Serialize(new { eventType = ev.EventType, ev.AgencyId, facts = ev.Facts });
        var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        try
        {
            using var resp = await _http.PostAsync(target, content, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook action failed");
            throw;
        }
    }

    private static async Task<Uri?> ResolveSafeAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        IPAddress[] addrs;
        if (IPAddress.TryParse(uri.Host, out var literal)) addrs = new[] { literal };
        else { try { addrs = await Dns.GetHostAddressesAsync(uri.Host, ct); } catch { return null; } }
        if (addrs.Length == 0 || addrs.Any(IsBlocked)) return null;
        return uri;
    }

    private static bool IsBlocked(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] == 0                                   // 0.0.0.0/8
                || b[0] == 10                                  // 10/8 private
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)  // 100.64/10 CGNAT
                || (b[0] == 169 && b[1] == 254)                // 169.254/16 link-local (metadata)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)   // 172.16/12 private
                || (b[0] == 192 && b[1] == 168)                // 192.168/16 private
                || b[0] >= 224;                                // multicast / reserved
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC;                      // fc00::/7 unique-local
        }
        return true;
    }
}

public class NotifyUserAction : IWorkflowAction
{
    public string ActionType => "notify-user";
    private readonly INotificationDispatcher _dispatcher;
    public NotifyUserAction(INotificationDispatcher d) => _dispatcher = Guard.AgainstNull(d);

    public async Task ExecuteAsync(IWorkflowEvent ev, IReadOnlyDictionary<string, object?> p, CancellationToken ct = default)
    {
        var title = TemplateRenderer.Render(p.TryGetValue("title", out var t) ? t?.ToString() ?? "" : "", ev.Facts);
        var body = TemplateRenderer.Render(p.TryGetValue("body", out var b) ? b?.ToString() ?? "" : "", ev.Facts);

        Guid? userId = null;
        if (p.TryGetValue("userIdFact", out var fk) && fk is not null
            && ev.Facts.TryGetValue(fk.ToString()!, out var fv)
            && Guid.TryParse(fv?.ToString(), out var uid))
            userId = uid;

        var channelsRaw = p.TryGetValue("channels", out var c) ? c?.ToString() ?? "InApp" : "InApp";
        var channels = channelsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Enum.TryParse<NotificationChannelType>(x, true, out var ct2) ? ct2 : NotificationChannelType.InApp)
            .Distinct().ToList();

        await _dispatcher.DispatchAsync(new NotificationPayload(ev.AgencyId, userId, title, body),
            channels, ct);
    }
}

internal static class TemplateRenderer
{
    /// <summary>Replaces {{key}} with the matching fact value.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, object?> facts)
    {
        if (string.IsNullOrEmpty(template)) return template;
        var result = template;
        foreach (var f in facts)
            result = result.Replace($"{{{{{f.Key}}}}}", f.Value?.ToString() ?? "");
        return result;
    }
}
