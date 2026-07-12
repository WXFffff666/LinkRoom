namespace LinkRoom.Core;

/// <summary>
/// Simple plugin API for third-party game port/protocol registration.
/// </summary>
public interface IGamePlugin
{
    string Id { get; }
    string DisplayName { get; }
    int DefaultPort { get; }
    string Protocol { get; }
}

public sealed class GamePlugin : IGamePlugin
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required int DefaultPort { get; init; }
    public string Protocol { get; init; } = "tcp";
}

public static class PluginRegistry
{
    static readonly List<IGamePlugin> Plugins = [];

    public static IReadOnlyList<IGamePlugin> All => Plugins;

    public static void Register(IGamePlugin plugin)
    {
        Plugins.RemoveAll(p => p.Id == plugin.Id);
        Plugins.Add(plugin);
    }

    public static void LoadFromDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var json in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var p = System.Text.Json.JsonSerializer.Deserialize<GamePlugin>(
                    File.ReadAllText(json),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (p?.Id != null) Register(p);
            }
            catch { /* skip invalid plugin files */ }
        }
    }
}
