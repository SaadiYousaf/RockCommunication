namespace CRM.Application.Common.Interfaces;

/// <summary>
/// Generic binary store for first-class file features (documents, sale verification
/// recordings, …). Mirrors the chat-attachment storage contract: the returned key is
/// opaque and stored on an entity; bytes are only ever served through an authorized
/// endpoint, so the on-disk layout / cloud bucket can change without breaking the API.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Saves the supplied stream under <paramref name="container"/> (a logical bucket
    /// such as "documents" or "sale-recordings") and returns the opaque storage key.
    /// </summary>
    Task<string> SaveAsync(string container, string originalFileName, Stream content, CancellationToken ct = default);

    /// <summary>Opens a previously-saved file. Throws <see cref="FileNotFoundException"/> if missing.</summary>
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default);

    /// <summary>Best-effort delete. Never throws if the file is already gone.</summary>
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
