using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 验证插件可以只依赖宿主语义资源适配主题，而不需要知道 Semi、Ursa 或 Dock 的内部资源名。
/// </summary>
public sealed partial class PluginStyleContractTests
{
    private static readonly string[] SemanticBrushKeys =
    [
        "AppPanelBrush",
        "AppSubtlePanelBrush",
        "AppToolSelectedBrush",
        "AppDividerBrush",
        "AppBorderBrush",
        "AppSecondaryTextBrush",
        "AppInfoBrush",
        "AppWarningBrush",
        "AppWarningPanelBrush",
        "AppWarningBorderBrush",
        "AppErrorBrush",
        "AppDangerBrush",
        "AppReadMessageBackgroundBrush",
        "AppUnreadMessageBackgroundBrush",
    ];

    [AvaloniaFact]
    public void 正式语义画刷在浅色和深色主题中均存在且类型稳定()
    {
        var application = Assert.IsType<App>(Application.Current);

        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            foreach (var key in SemanticBrushKeys)
            {
                Assert.True(
                    application.TryGetResource(key, theme, out var value),
                    $"主题 {theme} 缺少插件语义资源 {key}。" );
                Assert.IsType<SolidColorBrush>(value);
            }
        }
    }

    [AvaloniaFact]
    public void 插件DynamicResource随宿主主题切换而更新()
    {
        var application = Assert.IsType<App>(Application.Current);
        var originalTheme = application.RequestedThemeVariant;
        var probe = new PluginStyleProbe();
        var window = new Window { Content = probe };

        try
        {
            window.Show();
            application.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            var light = Assert.IsType<SolidColorBrush>(probe.Surface.Background).Color;

            application.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            var dark = Assert.IsType<SolidColorBrush>(probe.Surface.Background).Color;

            Assert.NotEqual(light, dark);
        }
        finally
        {
            window.Close();
            application.RequestedThemeVariant = originalTheme;
        }
    }

    [Fact]
    public void 仓库插件只能消费已登记的App语义资源且不依赖Dock内部画刷()
    {
        var repositoryRoot = FindRepositoryRoot();
        var allowed = SemanticBrushKeys.ToHashSet(StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var filePath in Directory.EnumerateFiles(
                     Path.Combine(repositoryRoot, "Plugins"),
                     "*.axaml",
                     SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, filePath);
            var text = File.ReadAllText(filePath);
            foreach (Match match in AppResourceRegex().Matches(text))
            {
                if (!allowed.Contains(match.Groups[1].Value))
                {
                    violations.Add($"{relativePath}: {match.Groups[1].Value}");
                }
            }

            if (text.Contains("DynamicResource DockTheme", StringComparison.Ordinal) ||
                text.Contains("StaticResource DockTheme", StringComparison.Ordinal))
            {
                violations.Add($"{relativePath}: 使用了不属于基础 SDK 的 DockTheme 资源");
            }
        }

        Assert.Empty(violations);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MyAvaloniaManagement.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("无法从测试输出目录定位仓库根目录。");
    }

    [GeneratedRegex(@"\{(?:DynamicResource|StaticResource)\s+(App[A-Za-z0-9]+)")]
    private static partial Regex AppResourceRegex();
}
