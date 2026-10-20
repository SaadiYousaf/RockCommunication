using CRM.Application.Common.Authorization;
using CRM.Application.Common.Compliance;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Leads.Dtos;
using CRM.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Queries;

public record LeadSearchQuery(string? Phone, string? Email, string? Name, int Take = 50)
    : IRequest<IReadOnlyList<LeadDto>>;

public class LeadSearchHandler : IRequestHandler<LeadSearchQuery, IReadOnlyList<LeadDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public LeadSearchHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<LeadDto>> Handle(LeadSearchQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        var scope = TenantScope.ConfinedTo(_user);

        var q = _db.Leads.AsQueryable();
        if (scope is { } agencyId) q = q.Where(l => l.AgencyId == agencyId);

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var normalized = new string(request.Phone.Where(char.IsDigit).ToArray());
            q = q.Where(l => l.PhoneNumber.Contains(normalized));
        }
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var em = request.Email.Trim().ToLower();
            q = q.Where(l => l.Email != null && l.Email.ToLower().Contains(em));
        }
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var n = request.Name.Trim().ToLower();
            q = q.Where(l => l.FirstName.ToLower().Contains(n) || l.LastName.ToLower().Contains(n));
        }

        return await q.OrderByDescending(l => l.CreatedAt)
            .Take(Math.Min(request.Take, 200))
            .Select(l => new LeadDto(l.Id, l.FirstName, l.LastName, l.PhoneNumber,
                l.Email, l.State, l.Stage, l.Disposition, l.AssignedUserId, l.TeamId, l.JornayaVerified, l.CreatedAt))
            .ToListAsync(ct);
    }
}

public record DuplicateGroup(string Key, IReadOnlyList<LeadDto> Leads);
public record DuplicateScanQuery() : IRequest<IReadOnlyList<DuplicateGroup>>;

public class DuplicateScanHandler : IRequestHandler<DuplicateScanQuery, IReadOnlyList<DuplicateGroup>>
{
    /// <summary>
    /// How many ids go into one IN clause. SQLite has a ceiling on bound parameters, and a tenant
    /// with thousands of duplicates would sail past it in a single query.
    /// </summary>
    private const int IdChunk = 400;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPhoneNormalizer _phone;

    public DuplicateScanHandler(IApplicationDbContext db, ICurrentUser user, IPhoneNormalizer phone)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _phone = Guard.AgainstNull(phone);
    }

    public async Task<IReadOnlyList<DuplicateGroup>> Handle(DuplicateScanQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        // null == spans agencies (an unscoped SuperAdmin); otherwise confined to one. See TenantScope.
        var scope2 = TenantScope.ConfinedTo(_user);

        // Grouped on the DIGITS, not on the text as typed.
        //
        // This used to GROUP BY PhoneNumber in SQL, which only ever found duplicates that happened
        // to have been punctuated identically. Nobody types a number the same way twice — one agent
        // writes 555-123-4567, the next (555) 123 4567, a web form sends +1 555 123 4567, an import
        // stores 5551234567 — and all four are one telephone. The screen reported a clean database
        // to people looking straight at two copies of the same customer.
        //
        // SQLite cannot strip punctuation in a query, so the grouping happens here. Only the id and
        // the number are pulled, which is a fraction of the row: it was materialising the full DTO
        // for every lead that exhausted memory on large tenants, and that part stays fixed.
        var rows = await _db.Leads
            .Where(l => (scope2 == null || l.AgencyId == scope2) && l.PhoneNumber != null && l.PhoneNumber != "")
            .Select(l => new { l.Id, l.PhoneNumber })
            .ToListAsync(ct);

        var duplicateGroups = rows
            .Select(r => new { r.Id, Digits = _phone.Normalize(r.PhoneNumber) })
            // A number with no digits in it at all is a typo, not a duplicate of every other typo.
            .Where(r => r.Digits.Length > 0)
            .GroupBy(r => r.Digits)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicateGroups.Count == 0)
            return Array.Empty<DuplicateGroup>();

        // Load the full rows for just those leads, in chunks.
        var ids = duplicateGroups.SelectMany(g => g.Select(r => r.Id)).ToList();
        var byId = new Dictionary<Guid, LeadDto>();
        for (var i = 0; i < ids.Count; i += IdChunk)
        {
            var chunk = ids.Skip(i).Take(IdChunk).ToList();
            var page = await _db.Leads
                .Where(l => chunk.Contains(l.Id))
                .Select(l => new LeadDto(l.Id, l.FirstName, l.LastName, l.PhoneNumber, l.Email, l.State,
                    l.Stage, l.Disposition, l.AssignedUserId, l.TeamId, l.JornayaVerified, l.CreatedAt))
                .ToListAsync(ct);
            foreach (var lead in page) byId[lead.Id] = lead;
        }

        return duplicateGroups
            .Select(g => new DuplicateGroup(
                $"phone:{g.Key}",
                g.Select(r => byId.TryGetValue(r.Id, out var lead) ? lead : null)
                 .Where(l => l is not null).Select(l => l!).ToList()))
            // A group can fall below two if a lead was deleted between the two reads; it is not a
            // duplicate any more and showing it would send someone to clean up nothing.
            .Where(g => g.Leads.Count > 1)
            .ToList();
    }
}
