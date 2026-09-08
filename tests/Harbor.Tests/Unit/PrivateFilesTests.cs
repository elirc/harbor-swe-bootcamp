using Harbor.Infrastructure;

namespace Harbor.Tests.Unit;

public class PrivateFilesTests
{
    [Fact]
    public async Task BoundedUploadUsesServerIdentityAndCleansFailedTemporaryFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "harbor-files-" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = new PrivateFiles(root); var ws = Guid.NewGuid();
            var stored = await files.StoreAsync(ws, ArtifactKind.Attachments, new MemoryStream([1, 2, 3]), 3, default);
            Assert.Equal(3, stored.Length); Assert.Equal(64, stored.Sha256.Length);
            Assert.True(files.Exists(ws, ArtifactKind.Attachments, stored.StorageId));
            await Assert.ThrowsAsync<ArtifactLimitException>(() => files.StoreAsync(ws, ArtifactKind.Attachments, new MemoryStream([1, 2, 3, 4]), 3, default));
            Assert.Single(files.Artifacts(ws, ArtifactKind.Attachments));
            Assert.Empty(Directory.EnumerateFiles(files.DirectoryFor(ws, ArtifactKind.Attachments), "*.tmp"));
            var name = PrivateFiles.SafeDisplayName("../\\malicious\r\n\"name.txt");
            Assert.DoesNotContain('/', name); Assert.DoesNotContain('\\', name); Assert.DoesNotContain('\n', name);
            Assert.DoesNotContain("malicious", files.PathFor(ws, ArtifactKind.Attachments, stored.StorageId));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
