using System.Net.NetworkInformation;

namespace LinkRoom.Core;

/// <summary>
/// Detects commonly used game ports on the local machine and LAN.
/// Supports manual port addition and UPnP-style port opening.
/// </summary>
public static class GamePortScanner
{
    /// <summary>Common game ports to scan automatically.</summary>
    public static readonly (string Name, int Port)[] KnownGamePorts =
    [
        ("Minecraft Java", 25565),
        ("Minecraft Bedrock", 19132),
        ("Counter-Strike", 27015),
        ("Terraria", 7777),
        ("Factorio", 34197),
        ("Valheim", 2456),
        ("Source Engine", 27005),
        ("Garry's Mod", 27015),
        ("Starbound", 21025),
        ("Rust", 28015),
        ("ARK", 7777),
        ("Palworld", 8211),
    ];

    /// <summary>Scans known game ports to see which are locally open (listening).</summary>
    public static List<(string Name, int Port)> ScanListeningGamePorts()
    {
        var open = new List<(string, int)>();
        var endpoints = IPGlobalProperties.GetIPGlobalProperties();

        var tcpListeners = endpoints.GetActiveTcpListeners();
        var udpListeners = endpoints.GetActiveUdpListeners();

        foreach (var (name, port) in KnownGamePorts)
        {
            if (tcpListeners.Any(e => e.Port == port) || udpListeners.Any(e => e.Port == port))
                open.Add((name, port));
        }
        return open;
    }
}