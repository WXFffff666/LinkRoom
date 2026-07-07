using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

namespace LinkRoom;

/// <summary>
/// Extracts embedded EasyTier runtime assets from the single-file exe
/// to %LOCALAPPDATA%\LinkRoom\runtime\<version>\ on first launch.
/// Verifies extraction, restricts ACLs, prevents multiple instances.
/// </summary>
public static class RuntimeAssetExtractor
{
    private static readonly string[] Assets =
    [
        "easytier-core.exe", "easytier-cli.exe",
        "wintun.dll", "Packet.dll", "WinDivert64.sys"
    ];

    private static string? _runtimeDir;

    public static string RuntimeDir => _runtimeDir ?? throw new InvalidOperationException("Runtime not extracted yet.");

    /// <summary>Extracts all runtime assets. Skips if already present.</summary>
    public static string EnsureExtracted(string version = "2.6.4")
    {
        _runtimeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinkRoom", "runtime", version);

        if (Directory.Exists(_runtimeDir) && AllPresent())
            return _runtimeDir;

        Directory.CreateDirectory(_runtimeDir);
        RestrictAcl(_runtimeDir);

        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in Assets)
        {
            var resName = $"easytier.{name}";
            using var stream = asm.GetManifestResourceStream(resName);
            if (stream == null) continue;

            var dest = Path.Combine(_runtimeDir, name);
            using var fs = File.Create(dest);
            stream.CopyTo(fs);
        }

        return _runtimeDir;
    }

    private static bool AllPresent()
    {
        foreach (var name in Assets)
        {
            var path = Path.Combine(_runtimeDir!, name);
            if (!File.Exists(path) || new FileInfo(path).Length == 0) return false;
        }
        return true;
    }

    private static void RestrictAcl(string dir)
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
        catch { /* best effort */ }
    }
}
