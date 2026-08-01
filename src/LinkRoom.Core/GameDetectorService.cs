using System.Diagnostics;

namespace LinkRoom.Core;

/// <summary>
/// Detects running game processes and maps them to known games/ports.
/// Read-only: never kills processes, never opens ports. The process-name →
/// port mapping is a static constant so it can be unit-tested without live
/// processes. Coordinates with GamePortScanner.KnownGamePorts: a process hit
/// is reported even when the game's port is not currently listening.
/// </summary>
public static class GameDetectorService
{
    /// <summary>
    /// Known game processes → (process name without .exe, game name, default
    /// host port). Order is the report order for simultaneous hits.
    /// </summary>
    public static readonly GameProcessInfo[] KnownGames =
    [
        new("javaw", "Minecraft Java", 25565),
        new("java", "Minecraft Java", 25565),
        new("Minecraft.Windows", "Minecraft（Windows 版）", 19132),
        new("cs2", "Counter-Strike 2", 27015),
        new("Terraria", "Terraria", 7777),
        new("Factorio", "Factorio", 34197),
        new("valheim_server", "Valheim", 2456),
        new("valheim", "Valheim", 2456),
        new("RustClient", "Rust", 28015),
        new("Palworld", "Palworld", 8211),
        new("ShooterGame", "ARK: Survival Evolved", 7777),
        new("srcds", "Source Engine", 27015),
        new("Starbound", "Starbound", 21025),
    ];

    /// <summary>
    /// Pure mapping: given running process names (with or without .exe),
    /// returns the known games they map to, in KnownGames order. Case-insensitive;
    /// empty/whitespace entries are ignored. Unit-testable without real processes.
    /// </summary>
    public static List<GameProcessInfo> MatchByProcessNames(IEnumerable<string> running)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in running)
        {
            var name = raw?.Trim() ?? "";
            if (name.Length == 0) continue;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            set.Add(name);
        }

        var result = new List<GameProcessInfo>();
        foreach (var game in KnownGames)
            if (set.Contains(game.ProcessName))
                result.Add(game);
        return result;
    }

    /// <summary>
    /// Enumerates live processes and reports which known games are running.
    /// A process hit is reported even if the game's port is not listening —
    /// the port is the default the game uses when hosting.
    /// </summary>
    public static List<GameProcessInfo> DetectRunningGames()
    {
        var running = KnownGames
            .Select(g => g.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => Process.GetProcessesByName(name).Length > 0);
        return MatchByProcessNames(running);
    }
}

/// <summary>A detected game: running process name, display name and default host port.</summary>
public sealed record GameProcessInfo(string ProcessName, string GameName, int Port)
{
    /// <summary>e.g. "检测到：Minecraft Java（javaw.exe）端口 25565".</summary>
    public string DisplayText => $"检测到：{GameName}（{ProcessName}.exe）端口 {Port}";
}
