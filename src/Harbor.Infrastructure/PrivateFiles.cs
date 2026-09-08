using System.Security.Cryptography;

namespace Harbor.Infrastructure;

public enum ArtifactKind { Attachments, Exports }
public sealed record StoredArtifact(Guid StorageId, long Length, string Sha256);
public sealed class ArtifactLimitException : Exception;

/// <summary>Private immutable blobs. User-supplied display names never participate in paths.</summary>
public sealed class PrivateFiles
{
    public string Root { get; }
    public PrivateFiles(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        RejectLink(Root);
    }

    public string DirectoryFor(Guid workspace, ArtifactKind kind)
    {
        var workspacePath = Contained(Path.Combine(Root, workspace.ToString("N")));
        Directory.CreateDirectory(workspacePath); RejectLink(workspacePath);
        var directory = Contained(Path.Combine(workspacePath, kind.ToString().ToLowerInvariant()));
        Directory.CreateDirectory(directory); RejectLink(directory);
        return directory;
    }

    public string PathFor(Guid workspace, ArtifactKind kind, Guid storageId) =>
        Contained(Path.Combine(DirectoryFor(workspace, kind), storageId.ToString("N") + ".blob"));

    public bool Exists(Guid workspace, ArtifactKind kind, Guid id)
    {
        var path = PathFor(workspace, kind, id);
        if (!File.Exists(path)) return false;
        RejectLink(path); return true;
    }

    public FileStream Open(Guid workspace, ArtifactKind kind, Guid id)
    {
        var path = PathFor(workspace, kind, id); RejectLink(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public void Delete(Guid workspace, ArtifactKind kind, Guid id)
    {
        var path = PathFor(workspace, kind, id);
        if (File.Exists(path)) { RejectLink(path); File.Delete(path); }
    }

    public IEnumerable<(Guid Id, string Path, DateTimeOffset ModifiedAt)> Artifacts(Guid workspace, ArtifactKind kind)
    {
        foreach (var path in Directory.EnumerateFiles(DirectoryFor(workspace, kind), "*.blob"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id)) continue;
            RejectLink(path);
            yield return (id, Contained(path), File.GetLastWriteTimeUtc(path));
        }
    }

    public async Task<StoredArtifact> StoreAsync(Guid workspace, ArtifactKind kind, Stream source, long maxBytes, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var destination = PathFor(workspace, kind, id);
        var temporary = Contained(Path.Combine(DirectoryFor(workspace, kind), id.ToString("N") + ".tmp"));
        long length = 0;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) != 0)
                {
                    length += read;
                    if (length > maxBytes) throw new ArtifactLimitException();
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                await output.FlushAsync(ct);
                output.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
            return new(id, length, Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<StoredArtifact> CreateAsync(Guid workspace, ArtifactKind kind, long maxBytes, Func<Stream, Task> produce, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var destination = PathFor(workspace, kind, id);
        var temporary = Contained(Path.Combine(DirectoryFor(workspace, kind), id.ToString("N") + ".tmp"));
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long length;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                using var bounded = new BoundedArtifactStream(output, maxBytes, hash);
                await produce(bounded);
                await output.FlushAsync(ct); output.Flush(flushToDisk: true);
                length = bounded.BytesWritten;
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
            return new(id, length, Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed class BoundedArtifactStream(Stream target, long limit, IncrementalHash hash) : Stream
    {
        public long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
        private void Count(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > limit - BytesWritten) throw new ArtifactLimitException();
            BytesWritten += bytes.Length; hash.AppendData(bytes);
        }
        public override void Write(byte[] buffer, int offset, int count) { Count(buffer.AsSpan(offset, count)); target.Write(buffer, offset, count); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        { Count(buffer.Span); await target.WriteAsync(buffer, ct); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override void Flush() => target.Flush();
        public override Task FlushAsync(CancellationToken ct) => target.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    public int DeleteOldTemporaryFiles(Guid workspace, ArtifactKind kind, DateTimeOffset before, int limit)
    {
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(DirectoryFor(workspace, kind), "*.tmp"))
        {
            if (removed >= limit) break;
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _) || File.GetLastWriteTimeUtc(path) >= before.UtcDateTime) continue;
            RejectLink(path); File.Delete(Contained(path)); removed++;
        }
        return removed;
    }

    public static string SafeDisplayName(string? input)
    {
        var name = new string((input ?? "file").Where(c => !char.IsControl(c) && c != '/' && c != '\\' && c != ':' && c != '"').Take(160).ToArray()).Trim(' ', '.');
        return name.Length == 0 ? "file" : name;
    }

    private string Contained(string path)
    {
        var absolute = Path.GetFullPath(path);
        if (!absolute.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Artifact path is outside private storage.");
        return absolute;
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Private artifact storage does not follow filesystem links.");
    }
}
