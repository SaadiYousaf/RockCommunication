using CRM.Application.Common.Interfaces;
using OtpNet;

namespace CRM.Infrastructure.Identity;

public class TwoFactorService : ITwoFactorService
{
    public string GenerateSecret()
    {
        var bytes = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(bytes);
    }

    public string BuildQrUri(string userEmail, string secret, string issuer = "CRM")
    {
        var label = Uri.EscapeDataString($"{issuer}:{userEmail}");
        var iss = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={secret}&issuer={iss}&algorithm=SHA1&digits=6&period=30";
    }

    public bool Verify(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;
        try
        {
            var bytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(bytes);
            return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch
        {
            return false;
        }
    }
}
