using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using LinkRoom.Core;

namespace LinkRoom;

public static class RuntimeAssetExtractor
{
    static readonly string[] Assets =
    [
        "easytier-core.exe", "easytier-cli.exe", "easytier-web.exe", "easytier-web-embed.exe",
        "wintun.dll", "Packet.dll", "WinDivert64.sys"
    ];

    const string EtagFile = ".etag";
    const string EtagHeader = "LinkRoom.Runtime.Etag v1";

    static string? _runtimeDir;

    public static string RuntimeDir => _runtimeDir ?? throw new InvalidOperationException("Runtime not extracted.");

    public static string EnsureExtracted(string version = AppPaths.EasyTierVersion)
    {
        _runtimeDir = AppPaths.RuntimeDir;

        var asm = Assembly.GetExecutingAssembly();
        var expected = ReadExpectedSizes(asm);

        // Cache hit only when every asset exists, is non-empty AND the on-disk .etag
        // matches the embedded manifest. .etag is written last (after all files), so a
        // crash during extraction leaves no .etag → next start re-extracts everything.
        if (Directory.Exists(_runtimeDir) && AllPresent() && EtagMatches(expected))
            return _runtimeDir;

        Directory.CreateDirectory(_runtimeDir);
        RestrictAcl(_runtimeDir);

        var written = new List<(string Name, long Size)>();
        foreach (var (name, _) in expected)
        {
            using var stream = asm.GetManifestResourceStream($"easytier.{name}");
            if (stream == null) continue;
            written.Add((name, ExtractAtomically(stream, Path.Combine(_runtimeDir, name))));
        }
        WriteEtag(written);

        return _runtimeDir;
    }

    /// <summary>Embedded manifest: asset name → expected byte count (resource stream Length, -1 when unavailable).</summary>
    static List<(string Name, long Size)> ReadExpectedSizes(Assembly asm)
    {
        var list = new List<(string, long)>();
        foreach (var name in Assets)
        {
            using var stream = asm.GetManifestResourceStream($"easytier.{name}");
            if (stream == null) continue;
            list.Add((name, stream.CanSeek ? stream.Length : -1));
        }
        return list;
    }

    /// <summary>
    /// Atomic write: copy to dest.tmp, verify it is non-empty, then move over dest.
    /// A crash never leaves a half-written dest — only a .tmp that the next run overwrites.
    /// </summary>
    static long ExtractAtomically(Stream src, string dest)
    {
        var tmp = dest + ".tmp";
        try
        {
            long size;
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                src.CopyTo(fs);
                size = fs.Length;
            }
            if (size == 0)
            {
                File.Delete(tmp);
                return 0;
            }
            File.Move(tmp, dest, overwrite: true);
            return size;
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
    }

    static bool EtagMatches(List<(string Name, long Size)> expected)
    {
        var path = Path.Combine(_runtimeDir!, EtagFile);
        if (!File.Exists(path)) return false;
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { return false; }
        if (lines.Length != expected.Count + 1 || !string.Equals(lines[0], EtagHeader, StringComparison.Ordinal))
            return false;
        for (var i = 0; i < expected.Count; i++)
        {
            var parts = lines[i + 1].Split('\t');
            if (parts.Length != 2) return false;
            if (!string.Equals(parts[0], expected[i].Name, StringComparison.Ordinal)) return false;
            if (!long.TryParse(parts[1], out var size)) return false;
            // Expected size -1 (Length unavailable) → accept any non-negative stored size.
            if (expected[i].Size >= 0 && size != expected[i].Size) return false;
        }
        return true;
    }

    static void WriteEtag(List<(string Name, long Size)> written)
    {
        var sb = new StringBuilder();
        sb.AppendLine(EtagHeader);
        foreach (var (name, size) in written)
            sb.Append(name).Append('\t').Append(size).AppendLine();

        var path = Path.Combine(_runtimeDir!, EtagFile);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    static bool AllPresent()
    {
        foreach (var name in Assets)
        {
            var path = Path.Combine(_runtimeDir!, name);
            if (!File.Exists(path) || new FileInfo(path).Length == 0) return false;
        }
        return true;
    }

    static void RestrictAcl(string dir)
    {
        try
        {
            var di = new DirectoryInfo(dir);
            var sec = di.GetAccessControl();
            var user = WindowsIdentity.GetCurrent().Name;
            sec.SetOwner(WindowsIdentity.GetCurrent().User!);
            sec.SetAccessRule(new FileSystemAccessRule(user,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            di.SetAccessControl(sec);
        }
        catch { }
    }
}
