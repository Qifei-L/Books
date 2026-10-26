using System.Security.Cryptography;
using Books.Application.Common;
using Books.Application.Files;

namespace Books.Infrastructure.Files;

/// <summary>
/// Files under one root directory. Keys are generated here and checked again on read, so a stored key can never
/// escape the root (path traversal) even if a database row were tampered with.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly string _prefix;

    public LocalFileStorage(string root)
    {
        _root = Path.GetFullPath(root);
        _prefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public async Task<StoredFile> SaveAsync(string folder, Stream content, string extension, CancellationToken ct)
    {
        if (!Folder(folder) || !Extension(extension))
        {
            throw BooksProblem.Invalid("A storage folder or extension is invalid.");
        }
        var now = DateTimeOffset.UtcNow;
        var key = $"{folder}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await content.CopyToAsync(file, ct);
        }
        var size = new FileInfo(path).Length;
        string hash;
        await using (var read = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        {
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(read, ct));
        }
        return new StoredFile(key, size, hash);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
        Task.FromResult<Stream>(Exists(key)
            ? new FileStream(PathOf(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : throw BooksProblem.NotFound());

    public Task<bool> DeleteAsync(string key, CancellationToken ct)
    {
        if (!Safe(key))
        {
            return Task.FromResult(false);
        }
        var path = PathOf(key);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }
        File.Delete(path);
        return Task.FromResult(true);
    }

    public bool Exists(string key) => Safe(key) && File.Exists(PathOf(key));

    private string PathOf(string key) => Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A key is relative, uses forward slashes, has no dot segments and cannot name a drive or stream.</summary>
    private bool Safe(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.StartsWith('\\') || key.Contains('\\')
            || key.Contains(':') || key.Contains('\0') || key.Split('/').Any(part => part is "" or "." or ".."))
        {
            return false;
        }
        var full = Path.GetFullPath(PathOf(key));
        return full.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Folder(string folder) =>
        folder.Length is > 0 and <= 40 && folder.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_');

    private static bool Extension(string extension) =>
        extension.Length is > 1 and <= 9 && extension[0] == '.'
        && extension[1..].All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c));
}
