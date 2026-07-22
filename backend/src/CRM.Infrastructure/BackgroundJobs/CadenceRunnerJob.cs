using CRM.Application.Common.Integrations;
using CRM.Application.Common.Notifications;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire/recurring job — every minute.
/// Walks active enrollments due now, runs the current step, schedules next.
/// </summary>
public class CadenceRunnerJob
{
    private readonly AppDbContext _db;
    private readonly ISmsProvider _sms;
    private readonly IEmailProvider _email;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<CadenceRunnerJob> _logger;

    public CadenceRunnerJob(AppDbContext db, ISmsProvider sms, IEmailProvider email,
        INotificationDispatcher dispatcher, ILogger<CadenceRunnerJob> logger)
    {
        _db = Guard.AgainstNull(db); _sms = Guard.AgainstNull(sms); _email = Guard.AgainstNull(email); _dispatcher = Guard.AgainstNull(dispatcher); _logger = Guard.AgainstNull(logger);
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var due = await _db.CadenceEnrollments
            .Where(e => e.Status == "Active" && e.NextRunAt <= now)
            .Take(200)
            .ToListAsync(ct);

        foreach (var enr in due)
        {
            try
            {
                await ProcessOneAsync(enr, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cadence enrollment {Id} step failed", enr.Id);
            }
        }
        if (due.Count > 0) await _db.SaveChangesAsync(ct);
    }

    private async Task ProcessOneAsync(CadenceEnrollment enr, CancellationToken ct)
    {
        var cadence = await _db.Cadences.Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.Id == enr.CadenceId, ct);
        if (cadence is null || !cadence.IsActive) { enr.Status = "Stopped"; enr.StopReason = "cadence-inactive"; enr.CompletedAt = DateTime.UtcNow; return; }

        var ordered = cadence.Steps.OrderBy(s => s.Order).ToList();
        var nextIndex = enr.CurrentStepOrder;
        if (nextIndex >= ordered.Count) { enr.Status = "Completed"; enr.CompletedAt = DateTime.UtcNow; return; }

        var step = ordered[nextIndex];

        if (step.StopIfContacted)
        {
            var contacted = await _db.LeadActivities.AsNoTracking()
                .AnyAsync(a => a.LeadId == enr.LeadId && a.OccurredAt >= enr.EnrolledAt
                    && a.Disposition != Domain.Enums.LeadDisposition.None
                    && a.Disposition != Domain.Enums.LeadDisposition.NoAnswer
                    && a.Disposition != Domain.Enums.LeadDisposition.Voicemail, ct);
            if (contacted) { enr.Status = "Stopped"; enr.StopReason = "contact-made"; enr.CompletedAt = DateTime.UtcNow; return; }
        }

        var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == enr.LeadId, ct);
        if (lead is null) { enr.Status = "Stopped"; enr.StopReason = "lead-missing"; enr.CompletedAt = DateTime.UtcNow; return; }

        switch (step.StepKind.ToLowerInvariant())
        {
            case "sms":
                if (!string.IsNullOrEmpty(lead.PhoneNumber))
                {
                    var template = ExtractParam(step.ParametersJson, "template") ?? "Hi {{firstName}}, this is your CRM follow-up.";
                    var body = template.Replace("{{firstName}}", lead.FirstName).Replace("{{lastName}}", lead.LastName);
                    await _sms.SendAsync(new SmsMessage(lead.PhoneNumber, body), ct);
                }
                break;
            case "email":
                if (!string.IsNullOrEmpty(lead.Email))
                {
                    var subject = ExtractParam(step.ParametersJson, "subject") ?? "Following up";
                    var body = ExtractParam(step.ParametersJson, "body") ?? "Hi {{firstName}},\nFollowing up on your inquiry.";
                    body = body.Replace("{{firstName}}", lead.FirstName);
                    await _email.SendAsync(new EmailMessage(lead.Email, subject, body), ct);
                }
                break;
            case "call":
                if (lead.AssignedUserId is { } uid)
                {
                    await _dispatcher.DispatchAsync(new NotificationPayload(
                        cadence.AgencyId, uid, "Cadence call task",
                        $"Cadence '{cadence.Name}' step {step.Order}: call {lead.FirstName} {lead.LastName} at {lead.PhoneNumber}"),
                        new[] { NotificationChannelType.InApp }, ct);
                }
                break;
            case "wait":
                break;
        }

        enr.CurrentStepOrder = nextIndex + 1;
        if (enr.CurrentStepOrder >= ordered.Count)
        {
            enr.Status = "Completed";
            enr.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            var next = ordered[enr.CurrentStepOrder];
            enr.NextRunAt = DateTime.UtcNow.AddMinutes(next.DelayMinutes);
        }
    }

    private static string? ExtractParam(string? json, string key)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }
}
