using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CRM.Infrastructure.Security;

/// <summary>
/// Application-level encryption for the most sensitive PII columns (SSN, driver's licence)
/// so that a stolen database file / backup does not expose them in cleartext. AES-256-GCM
/// (authenticated) with a random 96-bit nonce per value. The key is derived once at startup
/// from configuration (<c>Encryption:Key</c>, else the JWT secret) — see AddInfrastructure.
///
/// Values are stored as <c>enc:v1:base64(nonce|tag|ciphertext)</c>. Anything without that
/// prefix is treated as legacy plaintext and returned as-is on read (re-encrypted on next
/// write), so pre-existing rows keep working.
/// </summary>
public static class PiiProtector
{
    private const string Prefix = "enc:v1:";
    private static byte[]? _key;

    public static void Configure(byte[] key) => _key = key;

    public static string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext) || _key is null) return plaintext;
        var nonce = RandomNumberGenerator.GetBytes(12);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[pt.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(_key, 16))
            aes.Encrypt(nonce, pt, ct, tag);
        var blob = new byte[nonce.Length + tag.Length + ct.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, blob, nonce.Length, tag.Length);
        Buffer.BlockCopy(ct, 0, blob, nonce.Length + tag.Length, ct.Length);
        return Prefix + Convert.ToBase64String(blob);
    }

    public static string? Decrypt(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || _key is null) return stored;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored; // legacy plaintext
        try
        {
            var blob = Convert.FromBase64String(stored[Prefix.Length..]);
            var nonce = blob.AsSpan(0, 12);
            var tag = blob.AsSpan(12, 16);
            var ct = blob.AsSpan(28);
            var pt = new byte[ct.Length];
            using (var aes = new AesGcm(_key, 16))
                aes.Decrypt(nonce, ct, tag, pt);
            return Encoding.UTF8.GetString(pt);
        }
        catch
        {
            return stored; // tamper / wrong key — fail readable rather than crash the query
        }
    }
}

/// <summary>EF value converter that transparently encrypts a string column at rest.</summary>
public sealed class EncryptedStringConverter : ValueConverter<string?, string?>
{
    public EncryptedStringConverter() : base(v => PiiProtector.Encrypt(v), v => PiiProtector.Decrypt(v)) { }
}
