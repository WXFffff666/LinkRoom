namespace LinkRoom.Core.Tests;

public class GameDetectorServiceTests
{
    public static TheoryData<string, string, int> ExpectedMapping => new()
    {
        { "javaw", "Minecraft Java", 25565 },
        { "java", "Minecraft Java", 25565 },
        { "Minecraft.Windows", "Minecraft（Windows 版）", 19132 },
        { "cs2", "Counter-Strike 2", 27015 },
        { "Terraria", "Terraria", 7777 },
        { "Factorio", "Factorio", 34197 },
        { "valheim_server", "Valheim", 2456 },
        { "valheim", "Valheim", 2456 },
        { "RustClient", "Rust", 28015 },
        { "Palworld", "Palworld", 8211 },
        { "ShooterGame", "ARK: Survival Evolved", 7777 },
    };

    [Fact]
    public void KnownGames_MappingTable_IsNonEmpty_AndNoDuplicateProcessName()
    {
        Assert.NotEmpty(GameDetectorService.KnownGames);
        Assert.Equal(
            GameDetectorService.KnownGames.Length,
            GameDetectorService.KnownGames.Select(g => g.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [MemberData(nameof(ExpectedMapping))]
    public void KnownGames_ContainsExpectedMapping(string process, string game, int port)
    {
        Assert.Contains(
            GameDetectorService.KnownGames,
            g => g.ProcessName == process && g.GameName == game && g.Port == port);
    }

    [Fact]
    public void KnownGames_EveryGamePort_IsCoveredByGamePortScanner()
    {
        var scannedPorts = GamePortScanner.KnownGamePorts.Select(k => k.Port).ToHashSet();
        foreach (var g in GameDetectorService.KnownGames)
            Assert.True(scannedPorts.Contains(g.Port), $"{g.GameName} 端口 {g.Port} 不在 GamePortScanner.KnownGamePorts 中");
    }

    [Theory]
    [MemberData(nameof(ExpectedMapping))]
    public void MatchByProcessNames_SingleHit_ReturnsExpectedGame(string process, string game, int port)
    {
        var r = GameDetectorService.MatchByProcessNames([process]);

        Assert.Single(r);
        Assert.Equal(process, r[0].ProcessName);
        Assert.Equal(game, r[0].GameName);
        Assert.Equal(port, r[0].Port);
    }

    [Fact]
    public void MatchByProcessNames_ExeSuffix_IsTolerated()
    {
        var r = GameDetectorService.MatchByProcessNames(["cs2.exe", "Terraria.EXE"]);

        Assert.Collection(r,
            g => Assert.Equal("cs2", g.ProcessName),
            g => Assert.Equal("Terraria", g.ProcessName));
    }

    [Fact]
    public void MatchByProcessNames_IsCaseInsensitive()
    {
        var r = GameDetectorService.MatchByProcessNames(["JAVAW"]);

        Assert.Single(r);
        Assert.Equal("Minecraft Java", r[0].GameName);
        Assert.Equal(25565, r[0].Port);
    }

    [Fact]
    public void MatchByProcessNames_MultipleGames_AllReportedInTableOrder()
    {
        var r = GameDetectorService.MatchByProcessNames(["Palworld", "ShooterGame", "cs2"]);

        Assert.Collection(r,
            g => Assert.Equal("cs2", g.ProcessName),
            g => Assert.Equal("Palworld", g.ProcessName),
            g => Assert.Equal("ShooterGame", g.ProcessName));
    }

    [Fact]
    public void MatchByProcessNames_DuplicateRunningProcess_ReportedOncePerTableEntry()
    {
        // Both javaw and java map to Minecraft Java; both are legitimate hits.
        var r = GameDetectorService.MatchByProcessNames(["javaw", "javaw", "java"]);

        Assert.Collection(r,
            g => Assert.Equal("javaw", g.ProcessName),
            g => Assert.Equal("java", g.ProcessName));
    }

    [Fact]
    public void MatchByProcessNames_UnknownAndEmptyNames_Ignored()
    {
        var r = GameDetectorService.MatchByProcessNames(["", "   ", "not-a-game", null!, "explorer"]);

        Assert.Empty(r);
    }

    [Fact]
    public void MatchByProcessNames_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(GameDetectorService.MatchByProcessNames([]));
        Assert.Empty(GameDetectorService.MatchByProcessNames(Array.Empty<string>()));
    }

    [Fact]
    public void DisplayText_FormatsGameNameProcessAndPort()
    {
        var g = new GameProcessInfo("javaw", "Minecraft Java", 25565);

        Assert.Equal("检测到：Minecraft Java（javaw.exe）端口 25565", g.DisplayText);
    }
}
