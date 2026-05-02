using CRM.Application.Common.Compliance;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Scoring;
using CRM.Application.Common.Workflow;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace CRM.Application.Lists;

public record PublicEndpointDto(Guid Id, string Slug, Guid? CampaignId, Guid? LeadSourceId, Guid? CadenceId, bool IsActive, int LeadCount, string? AllowedOrigins);
public record PublicEndpointWithSecretDto(Guid Id, string Slug, string Secret) : PublicEndpointDto(Id, Slug, null, null, null, true, 0, null);

public record CreatePublicEndpointCommand(string Slug, Guid? CampaignId, Guid? LeadSourceId, Guid? CadenceId, string? AllowedOrigins)
    : IRequest<PublicEndpointWithSecretDto>;
public record ListPublicEndpointsQuery() : IRequest<IReadOnlyList<PublicEndpointDto>>;

public record PublicLeadPayload(string FirstName, string LastName, string PhoneNumber, string? Email,
    string? State, string? PostalCode, string? Source, string? JornayaLeadId);

public record CapturePublicLeadCommand(string Slug, string Signature, PublicLeadPayload Payload, string? PayloadJsonForSig)
    : IRequest<Guid>;

public class CreatePublicEndpointValidator : AbstractValidator<CreatePublicEndpointCommand>
{
    public CreatePublicEndpointValidator() => RuleFor(x => x.Slug).NotEmpty().Matches(@"^[a-z0-9\-]{3,40}$");
}

public class PublicLeadCaptureHandler :
    IRequestHandler<CreatePublicEndpointCommand, PublicEndpointWithSecretDto>,
    IRequestHandler<ListPublicEndpointsQuery, IReadOnlyList<PublicEndpointDto>>,
    IRequestHandler<CapturePublicLeadCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPhoneNormalizer _phone;
    private readonly IDncChecker _dnc;
    private readonly ILeadScorer _scorer;
    private readonly IWorkflowEngine _workflow;

    public PublicLeadCaptureHandler(IApplicationDbContext db, ICurrentUser user, IPhoneNormalizer phone,
        IDncChecker dnc, ILeadScorer scorer, IWorkflowEngine workflow)
    {
        _db = db; _user = user; _phone = phone; _dnc = dnc; _scorer = scorer; _workflow = workflow;
    }

    public async Task<PublicEndpointWithSecretDto> Handle(CreatePublicEndpointCommand request, CancellationToken ct)
    {
        EnsureManager();
        if (await _db.PublicLeadCaptureEndpoints.AnyAsync(e => e.Slug == request.Slug, ct))
            throw new ConflictException("Slug already taken.");

        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = HashSecret(secret);
        var entry = new PublicLeadCaptureEndpoint
        {
            AgencyId = _user.AgencyId!.Value,
            Slug = request.Slug.Trim().ToLowerInvariant(),
            SecretHash = hash,
            CampaignId = request.CampaignId,
            LeadSourceId = request.LeadSourceId,
            CadenceId = request.CadenceId,
            AllowedOrigins = request.AllowedOrigins
        };
        _db.PublicLeadCaptureEndpoints.Add(entry);
        await _db.SaveChangesAsync(ct);
        return new PublicEndpointWithSecretDto(entry.Id, entry.Slug, secret);
    }

    public async Task<IReadOnlyList<PublicEndpointDto>> Handle(ListPublicEndpointsQuery request, CancellationToken ct)
    {
        EnsureManager();
        return await _db.PublicLeadCaptureEndpoints
            .Where(e => e.AgencyId == _user.AgencyId)
            .Select(e => new PublicEndpointDto(e.Id, e.Slug, e.CampaignId, e.LeadSourceId, e.CadenceId,
                e.IsActive, e.LeadCount, e.AllowedOrigins))
            .ToListAsync(ct);
    }

    public async Task<Guid> Handle(CapturePublicLeadCommand request, CancellationToken ct)
    {
        var endpoint = await _db.PublicLeadCaptureEndpoints
            .FirstOrDefaultAsync(e => e.Slug == request.Slug && e.IsActive, ct)
            ?? throw new NotFoundException("Endpoint", request.Slug);

        if (!string.IsNullOrEmpty(request.PayloadJsonForSig))
        {
            var expected = ComputeHmac(request.PayloadJsonForSig, endpoint.SecretHash);
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(request.Signature)))
                throw new ForbiddenAccessException("Invalid signature.");
        }

        var p = request.Payload;
        var normalized = _phone.Normalize(p.PhoneNumber);
        if (string.IsNullOrEmpty(normalized) || normalized.Length < 10)
            throw new ConflictException("Invalid phone number.");
        if (await _dnc.IsBlockedAsync(endpoint.AgencyId, normalized, ct))
            throw new ConflictException("Phone is on the DNC list.");

        var lead = new Lead
        {
            AgencyId = endpoint.AgencyId,
            FirstName = p.FirstName.Trim(),
            LastName = p.LastName.Trim(),
            PhoneNumber = normalized,
            Email = p.Email?.Trim(),
            State = p.State,
            PostalCode = p.PostalCode,
            Source = p.Source ?? endpoint.Slug,
            JornayaLeadId = p.JornayaLeadId,
            CampaignId = endpoint.CampaignId,
            LeadSourceId = endpoint.LeadSourceId,
            Stage = WorkflowStage.New,
            ConsentCaptured = true
        };
        _db.Leads.Add(lead);
        await _db.SaveChangesAsync(ct);

        var scoring = await _scorer.ScoreAsync(lead, ct);
        lead.Score = scoring.Score;

        if (endpoint.CadenceId is { } cadenceId)
        {
            var firstStep = await _db.CadenceSteps.AsNoTracking()
                .Where(s => s.CadenceId == cadenceId)
                .OrderBy(s => s.Order).Select(s => s.DelayMinutes).FirstOrDefaultAsync(ct);
            _db.CadenceEnrollments.Add(new CadenceEnrollment
            {
                AgencyId = lead.AgencyId,
                CadenceId = cadenceId,
                LeadId = lead.Id,
                CurrentStepOrder = 0,
                NextRunAt = DateTime.UtcNow.AddMinutes(firstStep)
            });
        }
        endpoint.LeadCount++;
        await _db.SaveChangesAsync(ct);

        await _workflow.PublishAsync(new LeadCreatedEvent
        {
            AgencyId = lead.AgencyId, LeadId = lead.Id, Phone = lead.PhoneNumber,
            State = lead.State, Source = lead.Source, CampaignId = lead.CampaignId, Score = lead.Score
        }, ct);

        return lead.Id;
    }

    private void EnsureManager()
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains("Admin") && !_user.Roles.Contains("ProgramManager"))
            throw new ForbiddenAccessException();
    }

    private static string HashSecret(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ComputeHmac(string payload, string secretHash)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secretHash));
        var bytes = h.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
