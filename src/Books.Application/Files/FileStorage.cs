namespace Books.Application.Files;

/// <summary>A stored file's identity: its key, byte size and SHA-256 hash.</summary>
public sealed record StoredFile(string Key, long Size, string Hash);

/// <summary>
/// Local file storage for report packs and attachments. Binary data never goes in the database: a row keeps only the
/// key, so storage can move without breaking links (rule 45).
/// </summary>
public interface IFileStorage
{
    /// <summary>Saves a stream under a generated key in a known folder (for example reports or attachments).</summary>
    Task<StoredFile> SaveAsync(string folder, Stream content, string extension, CancellationToken ct);

    /// <summary>Opens a stored file, or throws not found when the key is unknown or points outside the root.</summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);

    Task<bool> DeleteAsync(string key, CancellationToken ct);

    bool Exists(string key);
}
