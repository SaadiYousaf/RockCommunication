namespace CRM.Application.Common.Interfaces;

/// <summary>
/// Persists chat-message file attachments. The returned <c>StorageKey</c> is an
/// opaque path stored on the message; clients fetch bytes via the authorized
/// download endpoint, so the underlying directory layout / cloud bucket can
/// change without breaking the API surface.
/// </summary>
public interface IChatAttachmentStorage
{
    /// <summary>Saves the supplied stream and returns the opaque storage key.</summary>
    Task<string> SaveAsync(Guid roomId, string originalFileName, Stream content, CancellationToken ct = default);

    /// <summary>Opens a previously-saved attachment for reading. Throws <see cref="FileNotFoundException"/> if missing.</summary>
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default);
}
