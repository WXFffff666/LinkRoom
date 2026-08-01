namespace LinkRoom.Core.Tests;

public class PluginRegistryTests
{
    static string NewPluginDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"linkroom-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void LoadFromDirectory_ValidJson_RegistersPlugin()
    {
        var dir = NewPluginDir();
        try
        {
            var id = $"test-plugin-{Guid.NewGuid():N}";
            File.WriteAllText(Path.Combine(dir, "plugin.json"),
                $$"""{"id":"{{id}}","displayName":"Test Plugin","defaultPort":25565,"protocol":"udp"}""");

            PluginRegistry.LoadFromDirectory(dir);

            var plugin = Assert.Single(PluginRegistry.All, p => p.Id == id);
            Assert.Equal("Test Plugin", plugin.DisplayName);
            Assert.Equal(25565, plugin.DefaultPort);
            Assert.Equal("udp", plugin.Protocol);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void LoadFromDirectory_SkipsInvalidJson()
    {
        var dir = NewPluginDir();
        try
        {
            var validId = $"valid-plugin-{Guid.NewGuid():N}";
            var invalidId = $"invalid-plugin-{Guid.NewGuid():N}";
            File.WriteAllText(Path.Combine(dir, "valid.json"),
                $$"""{"id":"{{validId}}","displayName":"Valid","defaultPort":7777}""");
            File.WriteAllText(Path.Combine(dir, "invalid.json"), "{ not valid json !!!");
            File.WriteAllText(Path.Combine(dir, "missing-id.json"),
                """{"displayName":"No Id","defaultPort":1234}""");

            PluginRegistry.LoadFromDirectory(dir);

            Assert.Contains(PluginRegistry.All, p => p.Id == validId);
            Assert.DoesNotContain(PluginRegistry.All, p => p.Id == invalidId);
            Assert.DoesNotContain(PluginRegistry.All, p => p.DisplayName == "No Id");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void LoadFromDirectory_EmptyOrMissingDir_NoOp()
    {
        var dir = NewPluginDir();
        try
        {
            var before = PluginRegistry.All.Count;
            PluginRegistry.LoadFromDirectory(dir);           // empty dir
            PluginRegistry.LoadFromDirectory(Path.Combine(dir, "missing")); // missing dir
            Assert.Equal(before, PluginRegistry.All.Count);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Register_AddsPlugin()
    {
        var id = $"register-{Guid.NewGuid():N}";
        PluginRegistry.Register(new GamePlugin { Id = id, DisplayName = "Registered", DefaultPort = 8211 });

        var plugin = Assert.Single(PluginRegistry.All, p => p.Id == id);
        Assert.Equal("Registered", plugin.DisplayName);
        Assert.Equal(8211, plugin.DefaultPort);
    }

    [Fact]
    public void Register_SameId_ReplacesExisting()
    {
        var id = $"dedupe-{Guid.NewGuid():N}";
        PluginRegistry.Register(new GamePlugin { Id = id, DisplayName = "Old", DefaultPort = 1000 });
        PluginRegistry.Register(new GamePlugin { Id = id, DisplayName = "New", DefaultPort = 2000 });

        Assert.Single(PluginRegistry.All, p => p.Id == id);
        Assert.Equal("New", PluginRegistry.All.Single(p => p.Id == id).DisplayName);
    }
}
