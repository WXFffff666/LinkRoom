using System.Globalization;
using LinkRoom.Core;
using LinkRoom.Core.Resources;

namespace LinkRoom.Tests;

/// <summary>
/// i18n coverage: zh/en resources differ, resx satellite fallback
/// (untranslated culture → neutral default, unknown key → null, no throw),
/// and AppSettings.Language persistence round-trip.
/// </summary>
public class LocalizationTests
{
    static readonly CultureInfo Zh = new("zh-CN");
    static readonly CultureInfo En = new("en-US");

    /// <summary>
    /// zh-CN (neutral resource) and en-US (en satellite) must return
    /// different values for the key UI strings.
    /// </summary>
    [Theory]
    [InlineData("MainCreateButton")]
    [InlineData("MainConnectButton")]
    [InlineData("MainTagline")]
    [InlineData("SettingsLanMode")]
    [InlineData("WizardStep1Title")]
    public void Key_ResolvesDifferently_InZhAndEn(string key)
    {
        var zh = Strings.ResourceManager.GetString(key, Zh);
        var en = Strings.ResourceManager.GetString(key, En);

        Assert.NotNull(zh);
        Assert.NotNull(en);
        Assert.NotEqual(zh, en);
    }

    /// <summary>
    /// resx fallback: a culture with no satellite (fr-FR) falls back to the
    /// neutral default resource (Chinese), and a truly unknown key returns
    /// null — neither path throws.
    /// </summary>
    [Fact]
    public void MissingSatellite_FallsBackToNeutralWithoutThrowing()
    {
        var fr = new CultureInfo("fr-FR");

        var frValue = Strings.ResourceManager.GetString("MainCreateButton", fr);
        var neutralValue = Strings.ResourceManager.GetString("MainCreateButton", Zh);

        Assert.NotNull(frValue);
        Assert.Equal(neutralValue, frValue); // neutral = default (zh) resource

        Assert.Null(Strings.ResourceManager.GetString("KeyDoesNotExist", En));
    }

    /// <summary>AppSettings.Language round-trips through SettingsService.</summary>
    [Fact]
    public void AppSettings_Language_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "linkroom-i18n-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ss = new SettingsService(Path.Combine(dir, "settings.json"));

            Assert.Null(new AppSettings().Language); // default = follow system

            ss.Save(new AppSettings { Language = "en" });
            Assert.Equal("en", ss.Load().Language);

            ss.Save(new AppSettings { Language = "zh" });
            Assert.Equal("zh", ss.Load().Language);

            ss.Save(new AppSettings { Language = null });
            Assert.Null(ss.Load().Language);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
