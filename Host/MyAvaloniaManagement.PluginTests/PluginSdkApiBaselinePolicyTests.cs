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
    public void G1_V1V2历史基线未改写且V3表面全部处于Unshipped()
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
        // G1 只建立未发布的 V3 版本线，不改变 public C# 形状。把 V2 Shipped 原样投影到
        // V3 Unshipped，既让 Analyzer 继续保护全部签名，也避免提前制造发布承诺。
        Assert.Empty(v3Shipped);
        Assert.Empty(uiV3Shipped);
        Assert.Equal(v2Shipped, coreV3Unshipped);
        Assert.Equal(uiV2Shipped, uiV3Unshipped);
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
