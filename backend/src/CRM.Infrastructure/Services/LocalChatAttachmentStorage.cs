using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CRM.Infrastructure.Services;

/// <summary>
/// Filesystem-backed attachment store. Files land under
/// <c>{ContentRoot}/App_Data/chat-attachments/{roomId}/{guid}{ext}</c> by default,
/// overridable via <c>Storage:ChatAttachmentsRoot</c> when you want a mounted
/// volume in production. The roomId folder isolates files by conversation so
/// directory listings on disk stay manageable.
/// </summary>
public class LocalChatAttachmentStorage : IChatAttachmentStorage
{
    private readonly string _root;

    public LocalChatAttachmentStorage(IHostEnvironment env, IConfiguration config)
    {
        Guard.AgainstNull(env);
        Guard.AgainstNull(config);
        var configured = config["Storage:ChatAttachmentsRoot"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "chat-attachments")
            : configured;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Guid roomId, string originalFileName, Stream content, CancellationToken ct = default)
    {
        Guard.AgainstNull(originalFileName);
        Guard.AgainstNull(content);
        var ext = Path.GetExtension(originalFileName);
        // Sanity-strip any path components from the original name. We only keep
        // the extension; the canonical name lives in the message metadata.
        if (ext.Length > 16) ext = ""; // refuse silly extensions
        var dir = Path.Combine(_root, roomId.ToString("N"));
        Directory.CreateDirectory(dir);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, fileName);
        await using (var fs = File.Create(fullPath))
        {
            await content.CopyToAsync(fs, ct);
        }
        // The key the message stores is relative — DB never holds an absolute path.
        return $"{roomId:N}/{fileName}";
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        Guard.AgainstNull(storageKey);
        // Defence-in-depth: refuse traversal even though keys are server-generated.
        if (storageKey.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(storageKey))
            throw new FileNotFoundException(storageKey);
        var fullPath = Path.Combine(_root, storageKey);
        if (!File.Exists(fullPath)) throw new FileNotFoundException(storageKey);
        Stream s = File.OpenRead(fullPath);
        return Task.FromResult(s);
    }
}
