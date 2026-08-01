using System.Text;
using LinkRoom;
using LinkRoom.Core;

namespace LinkRoom.Tests;

/// <summary>
/// End-to-end tests for RuntimeAssetExtractor against the real embedded easytier
/// resources (this project references src/LinkRoom directly). State is isolated
/// under the test host's LinkRoomData/ and cleaned up after each test.
/// </summary>
public class RuntimeAssetExtractorTests : IDisposable
{
    public RuntimeAssetExtractorTests()
    {
        // Always start from a clean slate so the first EnsureExtracted takes the
        // extraction path deterministically.
        DeleteDataRoot();
    }

    public void Dispose() => DeleteDataRoot();

    static void DeleteDataRoot()
    {
        try
        {
            if (Directory.Exists(AppPaths.DataRoot))
                Directory.Delete(AppPaths.DataRoot, recursive: true);
        }
        catch { }
    }

    static long EmbeddedLength(string assetName)
    {
        using var s = typeof(RuntimeAssetExtractor).Assembly
            .GetManifestResourceStream($"easytier.{assetName}");
        Assert.NotNull(s);
        return s!.Length;
    }

    [Fact]
    public void TruncatedFile_IsRepairedByFullReExtraction()
    {
        // Simulate a crash mid-extraction: the runtime dir exists, one asset is a
        // 10-byte stub, and no .etag was ever written (it is written last, only
        // after every file has been atomically moved into place).
        var runtimeDir = AppPaths.RuntimeDir;
        Directory.CreateDirectory(runtimeDir);
        var truncated = Path.Combine(runtimeDir, "easytier-cli.exe");
        File.WriteAllBytes(truncated, new byte[10]);

        RuntimeAssetExtractor.EnsureExtracted();

        Assert.Equal(EmbeddedLength("easytier-cli.exe"), new FileInfo(truncated).Length);
        Assert.True(File.Exists(Path.Combine(runtimeDir, ".etag")));
        Assert.Empty(Directory.GetFiles(runtimeDir, "*.tmp"));
    }

    [Fact]
    public void StaleEtag_TriggersFullReExtraction()
    {
        var runtimeDir = RuntimeAssetExtractor.EnsureExtracted();

        // Corrupt an asset (non-zero, so AllPresent would pass) and rewrite the .etag
        // with a byte count that no longer matches the embedded manifest — as if the
        // embedded resources changed between builds. The stale binary must be refreshed.
        var file = Path.Combine(runtimeDir, "easytier-cli.exe");
        File.WriteAllBytes(file, new byte[10]);
        var etagPath = Path.Combine(runtimeDir, ".etag");
        var lines = File.ReadAllLines(etagPath);
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].StartsWith("easytier-cli.exe\t", StringComparison.Ordinal))
                lines[i] = "easytier-cli.exe\t123";
        File.WriteAllLines(etagPath, lines);

        RuntimeAssetExtractor.EnsureExtracted();

        Assert.Equal(EmbeddedLength("easytier-cli.exe"), new FileInfo(file).Length);
        var storedSize = File.ReadLines(etagPath)
            .Single(l => l.StartsWith("easytier-cli.exe\t", StringComparison.Ordinal))
            .Split('\t')[1];
        Assert.Equal(EmbeddedLength("easytier-cli.exe").ToString(), storedSize);
        Assert.Empty(Directory.GetFiles(runtimeDir, "*.tmp"));
    }

    [Fact]
    public void MatchingEtag_UsesCache_NoReExtraction()
    {
        var runtimeDir = RuntimeAssetExtractor.EnsureExtracted();
        var file = Path.Combine(runtimeDir, "easytier-cli.exe");
        var fileBefore = File.GetLastWriteTimeUtc(file);
        var etagBefore = File.GetLastWriteTimeUtc(Path.Combine(runtimeDir, ".etag"));

        // Second call with everything intact must take the cache path and rewrite nothing.
        RuntimeAssetExtractor.EnsureExtracted();

        Assert.Equal(fileBefore, File.GetLastWriteTimeUtc(file));
        Assert.Equal(etagBefore, File.GetLastWriteTimeUtc(Path.Combine(runtimeDir, ".etag")));
        Assert.Empty(Directory.GetFiles(runtimeDir, "*.tmp"));
    }

    [Fact]
    public void EtagFile_RecordsEveryAssetWithExpectedByteCount()
    {
        var runtimeDir = RuntimeAssetExtractor.EnsureExtracted();

        var etag = File.ReadAllText(Path.Combine(runtimeDir, ".etag"), Encoding.UTF8);
        var lines = etag.TrimEnd().Split('\n');
        Assert.StartsWith("LinkRoom.Runtime.Etag v1", lines[0]);

        var assets = lines.Skip(1).Select(l => l.TrimEnd('\r').Split('\t')).ToList();
        Assert.Equal(7, assets.Count);
        foreach (var parts in assets)
        {
            Assert.Equal(EmbeddedLength(parts[0]), long.Parse(parts[1]));
        }
    }
}
