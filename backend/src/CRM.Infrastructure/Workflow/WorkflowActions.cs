using CRM.Application.Common.Assignment;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Notifications;
using CRM.Application.Common.Workflow;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CRM.Infrastructure.Workflow;

public class AssignAgentAction : IWorkflowAction
{
    public string ActionType => "assign-agent";
    private readonly AppDbContext _db;
    private readonly IAssignmentService _assignment;

    public AssignAgentAction(AppDbContext db, IAssignmentService assignment)
    {
        _db = db; _assignment = assignment;
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
    public MoveStageAction(AppDbContext db) => _db = db;

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
    public SendSmsWorkflowAction(ISmsProvider sms, AppDbContext db) { _sms = sms; _db = db; }

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
    public SendEmailWorkflowAction(IEmailProvider email, AppDbContext db) { _email = email; _db = db; }

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
    public CreateCallbackAction(AppDbContext db) => _db = db;

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
        _http = http;
        _logger = logger;
    }

    public async Task ExecuteAsync(IWorkflowEvent ev, IReadOnlyDictionary<string, object?> p, CancellationToken ct = default)
    {
        var url = p.TryGetValue("url", out var u) ? u?.ToString() : null;
        if (string.IsNullOrEmpty(url)) return;

        var payload = JsonSerializer.Serialize(new { eventType = ev.EventType, ev.AgencyId, facts = ev.Facts });
        var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        try
        {
            using var resp = await _http.PostAsync(url, content, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook action to {Url} failed", url);
            throw;
        }
    }
}

public class NotifyUserAction : IWorkflowAction
{
    public string ActionType => "notify-user";
    private readonly INotificationDispatcher _dispatcher;
    public NotifyUserAction(INotificationDispatcher d) => _dispatcher = d;

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
