using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Queries;

/// <summary>
/// "Lead troubleshooting" diagnostic. Surfaces every reason a lead might be stuck:
///   - DNC / TCPA compliance posture
///   - Jornaya verification state
///   - Cadence enrollments + next run
///   - Recent call attempts and dispositions
///   - Active workflow rules that should fire on this lead's events
///   - Suggested next-best-actions
/// </summary>
public record LeadDiagnosticsQuery(Guid LeadId) : IRequest<LeadDiagnosticsDto>;

public record LeadDiagnosticsDto(
    LeadSummary Lead,
    ComplianceStatus Compliance,
    JornayaStatus Jornaya,
    AssignmentStatus Assignment,
    CadenceStatus Cadence,
    CallActivityStatus CallActivity,
    WorkflowEvaluation Workflows,
    IReadOnlyList<DiagnosticIssue> Issues,
    IReadOnlyList<NextBestAction> Recommendations);

public record LeadSummary(Guid Id, string Name, string Phone, string? Email, string? State,
    WorkflowStage Stage, LeadDisposition Disposition, int Score, DateTime CreatedAt, int AgeDays);

public record ComplianceStatus(bool OnDnc, string? DncReason, DateTime? DncExpiresAt,
    bool ConsentCaptured, bool TcpaWindowOk, string? TcpaNote);

public record JornayaStatus(bool Verified, DateTime? VerifiedAt, string? LeadId);

public record AssignmentStatus(bool Assigned, Guid? AssignedUserId, string? AssignedUserName,
    Guid? TeamId, string? Team, string? RequiredSkill);

public record CadenceStatus(int ActiveEnrollments,
    IReadOnlyList<CadenceEnrollmentInfo> Enrollments);

public record CadenceEnrollmentInfo(Guid EnrollmentId, string CadenceName, int CurrentStep,
    int TotalSteps, DateTime NextRunAt, string Status);

public record CallActivityStatus(int TotalCalls, int AnsweredCalls, int UnwrappedCalls,
    DateTime? LastCallAt, string? LastWrapUp, IReadOnlyList<CallSummary> Recent);

public record CallSummary(Guid Id, DateTime InitiatedAt, string Direction, string Status,
    string? AgentName, string? WrapUpCode);

public record WorkflowEvaluation(IReadOnlyList<WorkflowMatch> ActiveRules,
    IReadOnlyList<WorkflowExecutionInfo> RecentExecutions);

public record WorkflowMatch(Guid RuleId, string Name, string EventType, bool Active);

public record WorkflowExecutionInfo(DateTime StartedAt, string EventType, string Status, string? Error);

public record DiagnosticIssue(string Severity, string Code, string Message);

public record NextBestAction(string Action, string Why);

public class LeadDiagnosticsHandler : IRequestHandler<LeadDiagnosticsQuery, LeadDiagnosticsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;

    public LeadDiagnosticsHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
    }

    public async Task<LeadDiagnosticsDto> Handle(LeadDiagnosticsQuery req, CancellationToken ct)
    {
        Guard.AgainstNull(req);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == req.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), req.LeadId);

        // ---- Lead summary ----
        var ageDays = (int)(DateTime.UtcNow - lead.CreatedAt).TotalDays;
        var summary = new LeadSummary(
            lead.Id, $"{lead.FirstName} {lead.LastName}", lead.PhoneNumber, lead.Email, lead.State,
            lead.Stage, lead.Disposition, lead.Score, lead.CreatedAt, ageDays);

        // ---- Compliance ----
        var dnc = await _db.DncEntries.FirstOrDefaultAsync(
            d => d.AgencyId == _user.AgencyId && d.PhoneNormalized == lead.PhoneNumber, ct);
        var compliance = new ComplianceStatus(
            OnDnc: dnc is not null,
            DncReason: dnc?.Reason,
            DncExpiresAt: dnc?.ExpiresAt,
            ConsentCaptured: lead.ConsentCaptured,
            TcpaWindowOk: IsInTcpaWindow(lead.State),
            TcpaNote: IsInTcpaWindow(lead.State) ? null : "Outside TCPA call window for this state.");

        // ---- Jornaya ----
        var jornaya = new JornayaStatus(lead.JornayaVerified, lead.JornayaVerifiedAt, lead.JornayaLeadId);

        // ---- Assignment ----
        string? assignedName = null;
        if (lead.AssignedUserId.HasValue)
        {
            var u = await _identity.GetUserAsync(lead.AssignedUserId.Value, ct);
            assignedName = u?.UserName;
        }
        string? team = null;
        if (lead.TeamId.HasValue)
            team = await _db.Teams.Where(t => t.Id == lead.TeamId.Value).Select(t => t.Name).FirstOrDefaultAsync(ct);

        var assignment = new AssignmentStatus(lead.AssignedUserId.HasValue, lead.AssignedUserId,
            assignedName, lead.TeamId, team, lead.RequiredSkillCode);

        // ---- Cadence ----
        var allCadences = await _db.Cadences.Where(c => c.AgencyId == _user.AgencyId).ToListAsync(ct);
        var cadenceMap = allCadences.ToDictionary(c => c.Id, c => c);
        var stepCounts = await _db.CadenceSteps
            .Where(s => s.AgencyId == _user.AgencyId)
            .GroupBy(s => s.CadenceId)
            .Select(g => new { CadenceId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var stepCountMap = stepCounts.ToDictionary(x => x.CadenceId, x => x.Count);

        var enrollments = await _db.CadenceEnrollments
            .Where(e => e.LeadId == lead.Id)
            .OrderByDescending(e => e.EnrolledAt)
            .ToListAsync(ct);

        var enrollmentInfos = enrollments.Select(e => new CadenceEnrollmentInfo(
            e.Id,
            cadenceMap.TryGetValue(e.CadenceId, out var c) ? c.Name : "(unknown)",
            e.CurrentStepOrder,
            stepCountMap.TryGetValue(e.CadenceId, out var n) ? n : 0,
            e.NextRunAt, e.Status)).ToList();

        var cadence = new CadenceStatus(
            enrollmentInfos.Count(e => string.Equals(e.Status, "Active", StringComparison.OrdinalIgnoreCase)),
            enrollmentInfos);

        // ---- Call activity ----
        var calls = await _db.CallRecords.Where(c => c.LeadId == lead.Id)
            .OrderByDescending(c => c.InitiatedAt).Take(20).ToListAsync(ct);

        var agentIds = calls.Select(c => c.AgentUserId).Distinct().ToList();
        var allUsersInAgency = await _identity.ListUsersAsync(_user.AgencyId, ct);
        var agentNames = allUsersInAgency.Where(u => agentIds.Contains(u.Id))
            .ToDictionary(u => u.Id, u => u.UserName);

        var lastCall = calls.FirstOrDefault();
        var callActivity = new CallActivityStatus(
            TotalCalls: calls.Count,
            AnsweredCalls: calls.Count(c => c.AnsweredAt is not null),
            UnwrappedCalls: calls.Count(c => c.EndedAt is not null && string.IsNullOrEmpty(c.WrapUpCode)),
            LastCallAt: lastCall?.InitiatedAt,
            LastWrapUp: lastCall?.WrapUpCode,
            Recent: calls.Take(5).Select(c => new CallSummary(
                c.Id, c.InitiatedAt, c.Direction, c.Status,
                agentNames.TryGetValue(c.AgentUserId, out var n) ? n : null, c.WrapUpCode)).ToList());

        // ---- Workflows ----
        // Events likely to fire for this lead at its current stage.
        var likelyEvents = LikelyEventsFor(lead);
        var rules = await _db.WorkflowRules
            .Where(r => r.AgencyId == _user.AgencyId && likelyEvents.Contains(r.EventType))
            .Select(r => new WorkflowMatch(r.Id, r.Name, r.EventType, r.IsActive))
            .ToListAsync(ct);

        var executions = await _db.WorkflowExecutions
            .Where(e => e.PayloadJson != null && e.PayloadJson.Contains(lead.Id.ToString()))
            .OrderByDescending(e => e.StartedAt).Take(10)
            .Select(e => new WorkflowExecutionInfo(e.StartedAt, e.EventType, e.Status, e.Error))
            .ToListAsync(ct);

        var workflows = new WorkflowEvaluation(rules, executions);

        // ---- Issues ----
        var issues = new List<DiagnosticIssue>();
        if (compliance.OnDnc)
            issues.Add(new("error", "DNC", "This number is on the Do-Not-Call list. The dialer will block outbound attempts."));
        if (!compliance.ConsentCaptured)
            issues.Add(new("warning", "NO_CONSENT", "TCPA consent is not on file. Capture before SMS / outbound dialing."));
        if (!compliance.TcpaWindowOk)
            issues.Add(new("warning", "TCPA_WINDOW", compliance.TcpaNote ?? "Outside allowed call window."));
        if (!jornaya.Verified && lead.Stage is WorkflowStage.New or WorkflowStage.Fronted)
            issues.Add(new("warning", "JORNAYA_PENDING", "Lead is not Jornaya-verified yet — verify before transitioning to Closed."));
        if (!assignment.Assigned && lead.Stage != WorkflowStage.New)
            issues.Add(new("error", "UNASSIGNED", "Lead has progressed past New but is not assigned to anyone."));
        if (callActivity.UnwrappedCalls > 0)
            issues.Add(new("warning", "UNWRAPPED", $"{callActivity.UnwrappedCalls} call(s) ended without a wrap-up code."));
        if (cadence.ActiveEnrollments == 0 && lead.Stage is WorkflowStage.New or WorkflowStage.Followup)
            issues.Add(new("info", "NO_CADENCE", "Lead has no active cadence enrollment — consider enrolling in 'New lead — 7-touch'."));
        if (ageDays > 30 && lead.Stage is WorkflowStage.New or WorkflowStage.Fronted)
            issues.Add(new("warning", "STALE", $"Lead is {ageDays} days old and still in {lead.Stage}. Move to Followup or Lost."));
        if (executions.Any(e => string.Equals(e.Status, "Failed", StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("error", "WORKFLOW_FAILURE", "A workflow rule failed for this lead. Inspect 'Recent executions' below."));

        // ---- Recommendations (next best action) ----
        var recs = new List<NextBestAction>();
        if (compliance.OnDnc)
            recs.Add(new("Mark Lost (DNC)", "Number is on DNC; dialing will fail compliance pre-flight."));
        else if (!assignment.Assigned)
            recs.Add(new("Assign to a Fronter", "Stage is past New but no agent owns this lead."));
        else if (!jornaya.Verified && lead.Stage is WorkflowStage.New or WorkflowStage.Fronted)
            recs.Add(new("Run Jornaya verification", "Compliance gate before further outreach."));
        else if (callActivity.UnwrappedCalls > 0)
            recs.Add(new("Wrap up open call", "Agent must close out the prior call before dialing again."));
        else if (cadence.ActiveEnrollments == 0)
            recs.Add(new("Enroll in cadence", "Automated touches keep the lead warm."));
        else if (lead.Stage == WorkflowStage.Closed)
            recs.Add(new("Validate the sale", "Stage is Closed — push it through validation to fund."));
        else if (lead.Stage == WorkflowStage.Validated)
            recs.Add(new("Submit for funding", "Validation complete — submit to funding provider."));
        else
            recs.Add(new("Dial the lead", "No blockers detected; proceed with outreach."));

        return new LeadDiagnosticsDto(summary, compliance, jornaya, assignment, cadence,
            callActivity, workflows, issues, recs);
    }

    /// <summary>Crude TCPA window check — 8 AM to 9 PM in the lead's state.</summary>
    private static bool IsInTcpaWindow(string? state)
    {
        // Use UTC + crude state→offset mapping. If state is unknown, default to allowed
        // so we don't false-negative — the real ComplianceGuard does the precise check.
        var offset = state switch
        {
            "CA" or "WA" or "OR" or "NV" => -8,
            "AZ" or "UT" or "CO" or "NM" or "MT" or "WY" or "ID" => -7,
            "TX" or "IL" or "MO" or "AR" or "LA" or "MN" or "WI" or "OK" or "KS" or "NE" or "SD" or "ND" or "IA" or "AL" or "MS" or "TN" => -6,
            "NY" or "NJ" or "PA" or "MA" or "CT" or "RI" or "NH" or "VT" or "ME"
                or "MD" or "DC" or "VA" or "NC" or "SC" or "GA" or "FL" or "OH" or "MI" or "IN" or "KY" or "WV" => -5,
            _ => -5,
        };
        var localHour = (DateTime.UtcNow.Hour + offset + 24) % 24;
        return localHour >= 8 && localHour < 21;
    }

    private static string[] LikelyEventsFor(Lead lead) => lead.Stage switch
    {
        WorkflowStage.New      => new[] { "lead.created", "lead.assigned", "lead.scored" },
        WorkflowStage.Fronted  => new[] { "lead.transitioned", "lead.contacted", "call.completed" },
        WorkflowStage.Verified => new[] { "lead.transitioned", "call.completed" },
        WorkflowStage.Closed   => new[] { "sale.closed", "lead.transitioned" },
        WorkflowStage.Validated=> new[] { "sale.validated" },
        WorkflowStage.Funded   => new[] { "sale.funded" },
        WorkflowStage.Followup => new[] { "callback.due", "lead.transitioned" },
        WorkflowStage.Winback  => new[] { "lead.transitioned" },
        WorkflowStage.Lost     => new[] { "lead.transitioned" },
        _ => Array.Empty<string>(),
    };
}
