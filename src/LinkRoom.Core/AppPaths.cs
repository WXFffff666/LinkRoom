namespace LinkRoom.Core;

/// <summary>
/// Unified portable data directory layout under exe/LinkRoomData/.
/// </summary>
public static class AppPaths
{
    public const string DataFolderName = "LinkRoomData";
    public const string EasyTierVersion = "2.6.4";
    public const string DefaultSharedNode = "tcp://public.easytier.top:11010";

    static string? _exeDir;
    static bool? _portable;

    public static string ExeDirectory
    {
        get
        {
            if (_exeDir != null) return _exeDir;
            var path = Environment.ProcessPath;
            _exeDir = string.IsNullOrEmpty(path)
                ? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/')
                : Path.GetDirectoryName(path)!;
            return _exeDir;
        }
    }

    public static void Configure(bool portableMode)
    {
        _portable = portableMode;
    }

    public static bool IsPortable =>
        _portable ?? Directory.Exists(Path.Combine(ExeDirectory, DataFolderName))
                   || IsDirectoryWritable(Path.Combine(ExeDirectory, DataFolderName));

    public static string DataRoot
    {
        get
        {
            // Portable mode still requires a writable exe dir — otherwise fall
            // back to %LocalAppData%\LinkRoom (BUG-7).
            var portableDir = Path.Combine(ExeDirectory, DataFolderName);
            if (_portable != false && IsDirectoryWritable(portableDir))
                return portableDir;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LinkRoom");
        }
    }

    public static string RuntimeDir => Path.Combine(DataRoot, "runtime", EasyTierVersion);
    public static string ConfigDir => Path.Combine(DataRoot, "config");
    public static string SettingsPath => Path.Combine(ConfigDir, "settings.json");
    public static string LogDir => Path.Combine(DataRoot, "logs");
    public static string TempDir => Path.Combine(DataRoot, "temp");
    public static string DiagnosticsDir => Path.Combine(DataRoot, "diagnostics");
    public static string PluginsDir => Path.Combine(DataRoot, "plugins");
    public static string StunCachePath => Path.Combine(ConfigDir, "stun-servers.json");

    public static void EnsureDataDirectories()
    {
        try
        {
            Directory.CreateDirectory(RuntimeDir);
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(LogDir);
            Directory.CreateDirectory(TempDir);
            Directory.CreateDirectory(DiagnosticsDir);
            Directory.CreateDirectory(PluginsDir);
        }
        catch (Exception ex)
        {
            throw new IOException($"无法创建数据目录 {DataRoot}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Crash cleanup (BUG-12): temp EasyTier configs may contain the room
    /// secret — remove any residue left by a crashed previous run. Called once
    /// at startup; kept out of EnsureDataDirectories so parallel writers
    /// (e.g. tests) do not delete each other's in-flight configs.
    /// </summary>
    public static void CleanupTempConfigs()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(TempDir, "*.toml"))
                try { File.Delete(f); } catch { }
        }
        catch { }
    }

    static bool IsDirectoryWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var test = Path.Combine(dir, ".write-test");
            File.WriteAllText(test, "");
            File.Delete(test);
            return true;
        }
        catch { return false; }
    }
}
