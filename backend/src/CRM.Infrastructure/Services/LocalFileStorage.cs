using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CRM.Infrastructure.Services;

/// <summary>
/// Filesystem-backed implementation of <see cref="IFileStorage"/>. Files land under
/// <c>{ContentRoot}/App_Data/files/{container}/{guid}{ext}</c> by default, overridable
/// via <c>Storage:FilesRoot</c> for a mounted volume in production.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IHostEnvironment env, IConfiguration config)
    {
        Guard.AgainstNull(env);
        Guard.AgainstNull(config);
        var configured = config["Storage:FilesRoot"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "files")
            : configured;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(string container, string originalFileName, Stream content, CancellationToken ct = default)
    {
        Guard.AgainstNull(container);
        Guard.AgainstNull(originalFileName);
        Guard.AgainstNull(content);
        // Keep only a sane extension; the canonical name lives in entity metadata.
        var ext = Path.GetExtension(originalFileName);
        if (ext.Length > 16) ext = "";

        var safeContainer = SanitizeSegment(container);
        var dir = Path.Combine(_root, safeContainer);
        Directory.CreateDirectory(dir);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, fileName);
        await using (var fs = File.Create(fullPath))
        {
            await content.CopyToAsync(fs, ct);
        }
        return $"{safeContainer}/{fileName}"; // relative key — DB never holds an absolute path
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        Guard.AgainstNull(storageKey);
        var fullPath = Resolve(storageKey);
        if (!File.Exists(fullPath)) throw new FileNotFoundException(storageKey);
        Stream s = File.OpenRead(fullPath);
        return Task.FromResult(s);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        try
        {
            var fullPath = Resolve(storageKey);
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
        catch { /* best-effort */ }
        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        // Defence-in-depth: refuse traversal even though keys are server-generated.
        if (storageKey.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(storageKey))
            throw new FileNotFoundException(storageKey);
        return Path.Combine(_root, storageKey);
    }

    private static string SanitizeSegment(string s)
    {
        var cleaned = new string(s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return string.IsNullOrEmpty(cleaned) ? "misc" : cleaned.ToLowerInvariant();
    }
}
