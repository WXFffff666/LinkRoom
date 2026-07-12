using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using LinkRoom.Core;

namespace LinkRoom;

public static class RuntimeAssetExtractor
{
    static readonly string[] Assets =
    [
        "easytier-core.exe", "easytier-cli.exe", "easytier-web.exe", "easytier-web-embed.exe",
        "wintun.dll", "Packet.dll", "WinDivert64.sys"
    ];

    static string? _runtimeDir;

    public static string RuntimeDir => _runtimeDir ?? throw new InvalidOperationException("Runtime not extracted.");

    public static string EnsureExtracted(string version = AppPaths.EasyTierVersion)
    {
        _runtimeDir = AppPaths.RuntimeDir;

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
            if (File.Exists(dest) && new FileInfo(dest).Length > 0) continue;
            using var fs = File.Create(dest);
            stream.CopyTo(fs);
        }

        return _runtimeDir;
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
