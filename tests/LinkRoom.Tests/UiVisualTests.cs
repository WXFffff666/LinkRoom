using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using iNKORE.UI.WPF.Modern;

namespace LinkRoom.Tests;

/// <summary>
/// UI visual-consistency guard rails for the Fluent restyle:
///  - no hardcoded color literals may exist outside AppColors.xaml,
///  - every {DynamicResource}/{StaticResource} brush key referenced by a
///    window must resolve to AppColors.xaml, App.xaml or the window's own
///    resources,
///  - the Light and Dark theme dictionaries in App.xaml must define the
///    same set of semantic brush keys (so dark mode cannot silently drop a
///    color).  Validity of the x:Key references themselves is proven by the
///    build (XAML compiles to BAML).
/// </summary>
public class UiVisualTests
{
    static readonly Regex HexColorRegex = new(@"#[0-9A-Fa-f]{3,8}\b", RegexOptions.Compiled);
    static readonly Regex RefKeyRegex = new(@"\{(?:Dynamic|Static)Resource\s+([\w\.]+)\}", RegexOptions.Compiled);
    static readonly Regex KeyRegex = new(@"x:Key=""([^""]+)""", RegexOptions.Compiled);

    static string SrcDir => FindSrcDir();

    static string FindSrcDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LinkRoom.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = Path.Combine(dir.FullName, "src", "LinkRoom");
        Assert.True(Directory.Exists(src), $"src/LinkRoom not found under {dir.FullName}");
        return src;
    }

    static string[] XamlFiles() => Directory.EnumerateFiles(SrcDir, "*.xaml", SearchOption.TopDirectoryOnly).ToArray();

    /// <summary>
    /// Hex color literals are only allowed inside AppColors.xaml — the single
    /// source of truth for every color in the app.
    /// </summary>
    [Fact]
    public void NoHardcodedColors_OutsideAppColors()
    {
        var offenders = new StringBuilder();
        foreach (var file in XamlFiles())
        {
            if (Path.GetFileName(file) == "AppColors.xaml") continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (HexColorRegex.IsMatch(lines[i]))
                    offenders.AppendLine($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
        }
        Assert.True(offenders.Length == 0,
            "Hardcoded color literals leaked outside AppColors.xaml — move them into AppColors.xaml:\n" + offenders);
    }

    /// <summary>
    /// Every brush key referenced by a window via {DynamicResource}/{StaticResource}
    /// must be defined in AppColors.xaml, in the App.xaml theme dictionaries, or in
    /// the referencing window's own resources.
    /// </summary>
    [Fact]
    public void AllReferencedPaletteKeys_AreDefined()
    {
        var defined = new HashSet<string>();
        foreach (var file in XamlFiles())
        {
            if (Path.GetFileName(file) is "AppColors.xaml" or "App.xaml") continue;
            foreach (Match m in KeyRegex.Matches(File.ReadAllText(file)))
                defined.Add(m.Groups[1].Value);
        }
        // AppColors.xaml + App.xaml define the app-wide palette/theme resources.
        defined.UnionWith(CollectKeys(Path.Combine(SrcDir, "AppColors.xaml")));
        defined.UnionWith(CollectKeys(Path.Combine(SrcDir, "App.xaml")));

        var missing = new List<string>();
        foreach (var file in XamlFiles())
        {
            if (Path.GetFileName(file) is "AppColors.xaml" or "App.xaml") continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in RefKeyRegex.Matches(lines[i]))
                {
                    if (!defined.Contains(m.Groups[1].Value))
                        missing.Add($"{Path.GetFileName(file)}:{i + 1} -> {m.Groups[1].Value}");
                }
            }
        }
        Assert.True(missing.Count == 0, "Unresolvable resource keys:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// Dark mode completeness: the "Dark" theme dictionary in App.xaml must forward
    /// exactly the same semantic brush keys as the "Light" dictionary, so no color
    /// is left un-themable.
    /// </summary>
    [Fact]
    public void DarkThemeDictionary_DefinesSameKeys_AsLight()
    {
        var appXaml = File.ReadAllText(Path.Combine(SrcDir, "App.xaml"));
        var lightBlock = Slice(appXaml, "x:Key=\"Light\"", "x:Key=\"Dark\"");
        var darkBlock = Slice(appXaml, "x:Key=\"Dark\"", "x:Key=\"HighContrast\"");

        Assert.False(string.IsNullOrEmpty(lightBlock), "Light theme dictionary not found in App.xaml");
        Assert.False(string.IsNullOrEmpty(darkBlock), "Dark theme dictionary not found in App.xaml");

        var lightKeys = CollectKeysInBlock(lightBlock);
        var darkKeys = CollectKeysInBlock(darkBlock);

        var missingInDark = lightKeys.Except(darkKeys).ToList();
        Assert.True(missingInDark.Count == 0,
            "Keys defined in Light but missing from Dark theme dictionary:\n" + string.Join("\n", missingInDark));
    }

    static string Slice(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        start = text.IndexOf('>', start) + 1;
        var end = text.IndexOf(endMarker, StringComparison.Ordinal);
        if (end < 0) return text[start..];
        return text[start..end];
    }

    /// <summary>
    /// End-to-end dark-mode proof: load the real App.xaml resource graph
    /// (ThemeResources + Light/Dark theme dictionaries + AppColors.xaml),
    /// flip ThemeManager.ApplicationTheme and assert the semantic brush key
    /// actually resolves to a different color. This is the runtime mechanism
    /// MainWindow.xaml.cs triggers on the DarkMode toggle.
    /// </summary>
    [Fact]
    public void ThemeSwitch_FlipsSemanticBrushColors()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent(); // loads App.xaml BAML incl. ThemeResources + theme dictionaries

                ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                var lightBrush = ResolveBrush(app.Resources, "TextPrimary");
                Assert.NotNull(lightBrush);
                Assert.Equal(Color.FromRgb(0x1D, 0x1D, 0x1F), lightBrush.Color);

                ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                var darkBrush = ResolveBrush(app.Resources, "TextPrimary");
                Assert.NotNull(darkBrush);
                Assert.Equal(Color.FromRgb(0xF2, 0xF2, 0xF7), darkBrush.Color);
                Assert.NotEqual(lightBrush.Color, darkBrush.Color);

                // A NeutralCard key must exist in dark mode too (dark-mode completeness at runtime)
                var darkNeutral = ResolveBrush(app.Resources, "NeutralCard");
                Assert.NotNull(darkNeutral);
                Assert.Equal(Color.FromRgb(0x3A, 0x3A, 0x3C), darkNeutral.Color);

                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException("Theme switch runtime check failed:\n" + failure);
    }

    static SolidColorBrush? ResolveBrush(ResourceDictionary root, string key)
    {
        if (root.Contains(key) && root[key] is SolidColorBrush brush) return brush;
        foreach (var merged in root.MergedDictionaries)
        {
            var found = ResolveBrush(merged, key);
            if (found != null) return found;
        }
        return null;
    }

    static HashSet<string> CollectKeys(string file) => CollectKeysInBlock(File.ReadAllText(file));

    static HashSet<string> CollectKeysInBlock(string block)
    {
        var keys = new HashSet<string>();
        foreach (Match m in KeyRegex.Matches(block))
            keys.Add(m.Groups[1].Value);
        return keys;
    }
}
