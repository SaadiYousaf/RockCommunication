using CRM.Application.Sales.Commands;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Sales;

public class InternalSaleChecker : IInternalSaleChecker
{
    private readonly AppDbContext _db;
    public InternalSaleChecker(AppDbContext db) => _db = Guard.AgainstNull(db);

    public async Task<(bool IsInternal, string? Reason)> CheckAsync(Lead lead, Guid closerUserId, CancellationToken ct = default)
    {
        Guard.AgainstNull(lead);
        var phoneMatch = await _db.Users
            .Where(u => u.AgencyId == lead.AgencyId && u.PhoneNumber == lead.PhoneNumber)
            .AnyAsync(ct);
        if (phoneMatch) return (true, "Phone number matches an internal employee.");

        if (!string.IsNullOrWhiteSpace(lead.Email))
        {
            var emailMatch = await _db.Users
                .Where(u => u.AgencyId == lead.AgencyId && u.Email == lead.Email)
                .AnyAsync(ct);
            if (emailMatch) return (true, "Email matches an internal employee.");
        }

        var dupSales = await _db.Sales
            .Where(s => s.AgencyId == lead.AgencyId && s.CloserUserId == closerUserId
                        && s.SoldAt > DateTime.UtcNow.AddHours(-1))
            .CountAsync(ct);
        if (dupSales >= 5) return (true, "Closer recorded unusually many sales in the last hour.");

        return (false, null);
    }
}
