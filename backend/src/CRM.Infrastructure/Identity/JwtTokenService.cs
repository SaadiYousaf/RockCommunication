using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CRM.Infrastructure.Identity;

public class JwtOptions
{
    public string Issuer { get; set; } = "CRM";
    public string Audience { get; set; } = "CRM";
    public string Secret { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _opts;
    private readonly AppDbContext _db;

    public JwtTokenService(IOptions<JwtOptions> opts, AppDbContext db)
    {
        _opts = Guard.AgainstNull(opts).Value;
        _db = Guard.AgainstNull(db);
    }

    public async Task<TokenResult> IssueAsync(
        Guid userId, string userName, Guid agencyId, IEnumerable<string> roles,
        Guid? callCenterId = null,
        IReadOnlyDictionary<string, string>? extraClaims = null,
        CancellationToken ct = default)
    {
        Guard.AgainstNull(roles);

        var expires = DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, userName),
            new("agency", agencyId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        // Only agents are pinned to a call center; agency-level users omit the claim and
        // therefore see every call center in their agency (handled by the query filter).
        if (callCenterId is { } cc && cc != Guid.Empty)
            claims.Add(new Claim("callcenter", cc.ToString()));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        if (extraClaims is not null)
            foreach (var kv in extraClaims)
                claims.Add(new Claim(kv.Key, kv.Value));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_opts.Issuer, _opts.Audience, claims, expires: expires, signingCredentials: creds);
        var access = new JwtSecurityTokenHandler().WriteToken(token);

        var refresh = GenerateRefreshToken();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(refresh),
            ExpiresAt = DateTime.UtcNow.AddDays(_opts.RefreshTokenDays)
        });
        await _db.SaveChangesAsync(ct);

        return new TokenResult(access, refresh, expires);
    }

    public async Task<TokenResult?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null) return null;

        // Reuse detection: a refresh token that's already been rotated or revoked is
        // either an attacker replaying a stolen token or a confused client. Either way,
        // assume compromise and burn every active refresh token in the chain so the
        // legitimate user is forced to re-authenticate.
        if (!existing.IsActive)
        {
            await RevokeAllForUserAsync(existing.UserId, ct);
            return null;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == existing.UserId, ct);
        if (user is null || !user.IsActive) return null;

        var roles = await (from ur in _db.UserRoles
                           join r in _db.Roles on ur.RoleId equals r.Id
                           where ur.UserId == user.Id
                           select r.Name!).ToListAsync(ct);

        existing.RevokedAt = DateTime.UtcNow;
        var newToken = await IssueAsync(user.Id, user.UserName!, user.AgencyId, roles, user.CallCenterId, null, ct);
        existing.ReplacedByHash = Hash(newToken.RefreshToken);
        await _db.SaveChangesAsync(ct);
        return newToken;
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is { RevokedAt: null })
        {
            existing.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in active) t.RevokedAt = now;
        if (active.Count > 0) await _db.SaveChangesAsync(ct);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
