using System.Xml.Linq;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 验证 Plugin SDK API 基线的版本归属和可审阅文本规则。
/// </summary>
/// <remarks>
/// Roslyn Analyzer 负责比较符号；本测试只保护仓库政策，避免有人把活动目录指向错误主版本、
/// 用删除标记绕过破坏性检查，或通过无序与重复条目降低代码评审的可读性。
/// </remarks>
public sealed class PluginSdkApiBaselinePolicyTests
{
    [Fact]
    public void 活动Api基线与Sdk包和程序集主版本一致()
    {
        var repositoryRoot = FindRepositoryRoot();
        var properties = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Version.props"));
        var baseline = Property(properties, "MyAvaloniaPluginSdkApiBaseline");
        var packageVersion = Version.Parse(Property(properties, "MyAvaloniaPluginSdkVersion"));
        var assemblyVersion = Version.Parse(Property(properties, "MyAvaloniaPluginSdkAssemblyVersion"));

        Assert.StartsWith("v", baseline, StringComparison.Ordinal);
        Assert.True(int.TryParse(baseline[1..], out var baselineMajor));
        Assert.Equal(packageVersion.Major, baselineMajor);
        Assert.Equal(assemblyVersion.Major, baselineMajor);

        foreach (var projectDirectory in new[]
                 {
                     "MyAvaloniaManagement.PluginSdk",
                     "MyAvaloniaManagement.PluginSdk.UI",
                 })
        {
            var baselineDirectory = Path.Combine(
                repositoryRoot, "Host", projectDirectory, "ApiCompatibility", baseline);
            Assert.True(File.Exists(Path.Combine(baselineDirectory, "PublicAPI.Shipped.txt")));
            Assert.True(File.Exists(Path.Combine(baselineDirectory, "PublicAPI.Unshipped.txt")));
        }
    }

    [Fact]
    public void Api文本可稳定审阅且不能用删除标记绕过主版本政策()
    {
        var repositoryRoot = FindRepositoryRoot();
        var properties = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Version.props"));
        var baseline = Property(properties, "MyAvaloniaPluginSdkApiBaseline");
        foreach (var projectDirectory in new[]
                 {
                     "MyAvaloniaManagement.PluginSdk",
                     "MyAvaloniaManagement.PluginSdk.UI",
                 })
        {
            var baselineDirectory = Path.Combine(
                repositoryRoot, "Host", projectDirectory, "ApiCompatibility", baseline);
            var shipped = ReadAndAssertApiFile(
                Path.Combine(baselineDirectory, "PublicAPI.Shipped.txt"));
            var unshipped = ReadAndAssertApiFile(
                Path.Combine(baselineDirectory, "PublicAPI.Unshipped.txt"));

            Assert.NotEmpty(shipped.Concat(unshipped));
            Assert.Empty(shipped.Intersect(unshipped, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void G1_V1V2历史基线和V3Shipped未改写且新增表面进入Unshipped()
    {
        var repositoryRoot = FindRepositoryRoot();
        var apiRoot = Path.Combine(
            repositoryRoot,
            "Host",
            "MyAvaloniaManagement.PluginSdk",
            "ApiCompatibility");

        var v1Shipped = ReadAndAssertApiFile(Path.Combine(
            apiRoot, "v1", "PublicAPI.Shipped.txt"));
        var v1Unshipped = ReadAndAssertApiFile(Path.Combine(
            apiRoot, "v1", "PublicAPI.Unshipped.txt"));
        var v2Shipped = ReadAndAssertApiFile(Path.Combine(
            apiRoot, "v2", "PublicAPI.Shipped.txt"));
        var coreV2Unshipped = ReadAndAssertApiFile(Path.Combine(
            apiRoot, "v2", "PublicAPI.Unshipped.txt"));
        var uiApiRoot = Path.Combine(
            repositoryRoot, "Host", "MyAvaloniaManagement.PluginSdk.UI", "ApiCompatibility");
        var uiV2Shipped = ReadAndAssertApiFile(Path.Combine(
            uiApiRoot, "v2", "PublicAPI.Shipped.txt"));
        var uiV2Unshipped = ReadAndAssertApiFile(Path.Combine(
            uiApiRoot, "v2", "PublicAPI.Unshipped.txt"));
        var v3Shipped = ReadAndAssertApiFile(Path.Combine(
            apiRoot, "v3", "PublicAPI.Shipped.txt"));
        var coreV3Unshipped = ReadAndAssertApiFile(Path.Combine(
            apiRoot, "v3", "PublicAPI.Unshipped.txt"));
        var uiV3Shipped = ReadAndAssertApiFile(Path.Combine(
            uiApiRoot, "v3", "PublicAPI.Shipped.txt"));
        var uiV3Unshipped = ReadAndAssertApiFile(Path.Combine(
            uiApiRoot, "v3", "PublicAPI.Unshipped.txt"));

        Assert.NotEmpty(v1Shipped);
        Assert.Empty(v1Unshipped);
        // G14 只把已经审核的 V2 符号从 Unshipped 原样转入 Shipped，
        // 不修改 V1 历史证据，也不借封板增删任何 public 符号。因此此处
        // 同时固定条目数和归属文件，避免仅判断“非空”而放过意外漂移。
        Assert.Equal(85, v2Shipped.Length);
        Assert.Equal(46, uiV2Shipped.Length);
        Assert.Empty(coreV2Unshipped);
        Assert.Empty(uiV2Unshipped);
        // G1 保持既有 V3 Shipped 正式承诺不变；Workflow Action 3.1 兼容新增只进入
        // Unshipped。固定数量和代表性签名可防止误写 Shipped 或遗漏本次重新签署的 Run 边界。
        Assert.Equal(127, v3Shipped.Length);
        Assert.Equal(45, uiV3Shipped.Length);
        Assert.Equal(91, coreV3Unshipped.Length);
        Assert.Equal(66, uiV3Unshipped.Length);
        Assert.Contains(coreV3Unshipped, entry => entry.Contains(
            "IWorkflowActionGateway.CreateRun()", StringComparison.Ordinal));
        Assert.Contains(coreV3Unshipped, entry => entry.Contains(
            "IWorkflowActionRun.InvokeAsync", StringComparison.Ordinal));
        Assert.Contains(uiV3Unshipped, entry => entry.Contains(
            "UseWorkflowActionGateway", StringComparison.Ordinal));
        Assert.Contains(coreV3Unshipped, entry => entry.Contains(
            "IWorkbenchDocumentCommandTarget.ExecuteAsync", StringComparison.Ordinal));
        Assert.Contains(coreV3Unshipped, entry => entry.Contains(
            "WorkbenchCommandStateChangedEventArgs.CommandId.get", StringComparison.Ordinal));
        Assert.Contains(uiV3Unshipped, entry => entry.Contains(
            "IWorkbenchCommandRegistration.AddDocumentCommand", StringComparison.Ordinal));
        Assert.Contains(uiV3Unshipped, entry => entry.Contains(
            "KeyBindingContributionDescriptor", StringComparison.Ordinal));
        Assert.Contains(uiV3Shipped, entry => entry.Contains(
            "IWindowContentFullscreenHost.TryPresent(Avalonia.Controls.Control! content) -> System.IDisposable?",
            StringComparison.Ordinal));
        Assert.DoesNotContain(uiV3Shipped, entry =>
            entry.Contains("TryRestore", StringComparison.Ordinal) ||
            entry.Contains("object! owner", StringComparison.Ordinal));
        Assert.Contains(v3Shipped, entry =>
            entry.Contains("DocumentSaveSnapshot", StringComparison.Ordinal));
        Assert.Contains(v3Shipped, entry =>
            entry.Contains("AcceptChanges(MyAvaloniaManagement.PluginSdk.DocumentRevision", StringComparison.Ordinal));
        Assert.DoesNotContain(v3Shipped, entry =>
            entry.Contains("CaptureContentAsync", StringComparison.Ordinal));
        Assert.Contains(v3Shipped, entry =>
            entry.Contains("NewDocumentActivation", StringComparison.Ordinal));
        Assert.Contains(v3Shipped, entry =>
            entry.Contains("RestoreDocumentActivation", StringComparison.Ordinal));
        Assert.DoesNotContain(v3Shipped, entry =>
            entry.Contains("DocumentActivationContext", StringComparison.Ordinal));
        Assert.DoesNotContain(v3Shipped, entry =>
            entry.Contains("IHostEventBus", StringComparison.Ordinal));
        Assert.Contains(v2Shipped, entry =>
            entry.Contains("CaptureContentAsync", StringComparison.Ordinal));
        Assert.DoesNotContain(v2Shipped, entry =>
            entry.Contains("MyAvaloniaManagementCommon", StringComparison.Ordinal));
    }

    [Fact]
    public void Api分析器固定为私有构建依赖且缺少基线时不能静默通过()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageVersions = XDocument.Load(
            Path.Combine(repositoryRoot, "Directory.Packages.props"));
        var analyzerVersion = packageVersions.Descendants("PackageVersion")
            .Single(item => item.Attribute("Include")?.Value ==
                            "Microsoft.CodeAnalysis.PublicApiAnalyzers")
            .Attribute("Version")?.Value;
        Assert.Equal("5.6.0", analyzerVersion);

        foreach (var projectDirectory in new[]
                 {
                     "MyAvaloniaManagement.PluginSdk",
                     "MyAvaloniaManagement.PluginSdk.UI",
                 })
        {
            var editorConfig = File.ReadAllText(Path.Combine(
                repositoryRoot, "Host", projectDirectory, ".editorconfig"));
            Assert.Contains(
                "dotnet_public_api_analyzer.require_api_files = true",
                editorConfig,
                StringComparison.Ordinal);
        }
    }

    private static string[] ReadAndAssertApiFile(string path)
    {
        var lines = File.ReadAllLines(path);
        Assert.NotEmpty(lines);
        Assert.Equal("#nullable enable", lines[0]);

        var entries = lines[1..];
        Assert.DoesNotContain(entries, string.IsNullOrWhiteSpace);
        Assert.All(entries, entry =>
        {
            Assert.Equal(entry.Trim(), entry);
            Assert.False(
                entry.StartsWith("*REMOVED*", StringComparison.Ordinal),
                $"同一主版本不得登记删除：{entry}");
        });
        Assert.Equal(entries.Length, entries.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            entries.OrderBy(entry => entry, StringComparer.Ordinal),
            entries);
        return entries;
    }

    private static string Property(XDocument document, string name) =>
        document.Descendants(name).Single().Value;

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
}
