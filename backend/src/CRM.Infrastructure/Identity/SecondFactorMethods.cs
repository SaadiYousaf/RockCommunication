using CRM.Application.Common.Integrations;
using CRM.Application.Common.Interfaces;
using CRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace CRM.Infrastructure.Identity;

public class TotpSecondFactor : ISecondFactorMethod
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ITwoFactorService _totp;

    public SecondFactorKind Kind => SecondFactorKind.Totp;

    public TotpSecondFactor(UserManager<ApplicationUser> users, ITwoFactorService totp)
    {
        _users = users;
        _totp = totp;
    }

    public async Task<SecondFactorEnrollment> BeginEnrollmentAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");
        var secret = await _users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(secret))
        {
            await _users.ResetAuthenticatorKeyAsync(user);
            secret = await _users.GetAuthenticatorKeyAsync(user);
        }
        var qr = _totp.BuildQrUri(user.Email!, secret!);
        return new SecondFactorEnrollment(Kind, secret, qr);
    }

    public async Task<bool> VerifyAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return false;
        var secret = await _users.GetAuthenticatorKeyAsync(user);
        return !string.IsNullOrEmpty(secret) && _totp.Verify(secret, code);
    }

    public Task ChallengeAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
}

public class EmailOtpSecondFactor : ISecondFactorMethod
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IEmailProvider _email;
    private readonly AppDbContext _db;

    public SecondFactorKind Kind => SecondFactorKind.EmailOtp;

    public EmailOtpSecondFactor(UserManager<ApplicationUser> users, IEmailProvider email, AppDbContext db)
    {
        _users = users;
        _email = email;
        _db = db;
    }

    public Task<SecondFactorEnrollment> BeginEnrollmentAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(new SecondFactorEnrollment(Kind, null, null));

    public async Task ChallengeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");
        if (string.IsNullOrEmpty(user.Email)) return;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var existing = await _db.EmailOtpCodes
            .Where(o => o.UserId == userId)
            .ToListAsync(ct);
        _db.EmailOtpCodes.RemoveRange(existing);

        _db.EmailOtpCodes.Add(new EmailOtpCode
        {
            UserId = userId,
            Code = code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        });
        await _db.SaveChangesAsync(ct);

        await _email.SendAsync(new EmailMessage(
            user.Email,
            "Your CRM verification code",
            $"Your verification code is: {code}\n\nThis code expires in 5 minutes."), ct);
    }

    public async Task<bool> VerifyAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var entry = await _db.EmailOtpCodes
            .FirstOrDefaultAsync(o => o.UserId == userId && o.Code == code, ct);
        if (entry is null || entry.ExpiresAt < DateTime.UtcNow) return false;
        _db.EmailOtpCodes.Remove(entry);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public class EmailOtpCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SecondFactorRegistry
{
    private readonly Dictionary<SecondFactorKind, ISecondFactorMethod> _methods;
    public SecondFactorRegistry(IEnumerable<ISecondFactorMethod> methods)
    {
        _methods = methods.ToDictionary(m => m.Kind);
    }
    public ISecondFactorMethod Get(SecondFactorKind kind) =>
        _methods.TryGetValue(kind, out var m) ? m
            : throw new InvalidOperationException($"No second-factor method registered for {kind}.");

    public IReadOnlyList<SecondFactorKind> Available => _methods.Keys.ToList();
}
