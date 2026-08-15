using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using BiliDownloader.Plugin;
using DaTangAccountingHelpPlug.Plugin;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.ViewModels.Hello;
using MyAvaloniaManagementCommon.Plugin;
using MyPlugTest.Plugin;
using MySmallTools.Plugin;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 验证集中版本政策、实际程序集元数据和仓库内插件清单没有发生漂移。
/// </summary>
/// <remarks>
/// 设计意图：这是仓库发布政策测试，不是运行时插件发现测试。它有意读取源码树中的
/// MSBuild 属性与清单，再与本次构建的程序集交叉验证，使版本复制错误在发布门禁中给出
/// 具体字段，而不是等到用户启动宿主后才得到笼统的不兼容诊断。
/// </remarks>
public sealed class VersionPolicyTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void VersionPolicy_产品与Sdk程序集元数据来自集中属性()
    {
        var properties = ReadVersionProperties();
        var hostAssembly = typeof(WelcomeViewModel).Assembly;
        var sdkAssembly = typeof(IPluginModule).Assembly;

        AssertVersionFact(
            "Host Version/InformationalVersion",
            properties["MyAvaloniaProductVersion"],
            ReadInformationalVersionCore(hostAssembly));
        AssertVersionFact(
            "Host FileVersion",
            properties["MyAvaloniaProductFileVersion"],
            ReadFileVersion(hostAssembly));
        AssertVersionFact(
            "Host AssemblyVersion",
            properties["MyAvaloniaHostApiAssemblyVersion"],
            hostAssembly.GetName().Version?.ToString());

        AssertVersionFact(
            "Plugin SDK Version/InformationalVersion",
            properties["MyAvaloniaPluginSdkVersion"],
            ReadInformationalVersionCore(sdkAssembly));
        AssertVersionFact(
            "Plugin SDK FileVersion",
            properties["MyAvaloniaPluginSdkFileVersion"],
            ReadFileVersion(sdkAssembly));
        AssertVersionFact(
            "Plugin SDK AssemblyVersion",
            properties["MyAvaloniaPluginSdkAssemblyVersion"],
            sdkAssembly.GetName().Version?.ToString());

        AssertProjectMapping(
            Path.Combine("Host", "MyAvaloniaManagement", "MyAvaloniaManagement.csproj"),
            "Version",
            "$(MyAvaloniaProductVersion)");
        AssertProjectMapping(
            Path.Combine("Host", "MyAvaloniaManagementCommon", "MyAvaloniaManagementCommon.csproj"),
            "PackageVersion",
            "$(MyAvaloniaPluginSdkVersion)");
    }

    [Fact]
    public void VersionPolicy_欢迎页显示宿主产品版本而不是测试入口版本()
    {
        var expected = ReadVersionProperties()["MyAvaloniaProductVersion"];

        Assert.Equal($"版本 {expected}", new WelcomeViewModel().VersionText);
    }

    [Fact]
    public void VersionPolicy_清单Schema与数据根代际匹配集中政策()
    {
        var properties = ReadVersionProperties();

        AssertVersionFact(
            "manifest schema",
            properties["MyAvaloniaManifestSchemaVersion"],
            PluginManifestReader.CurrentSchemaVersion.ToString());
        AssertVersionFact(
            "Host data root generation",
            properties["MyAvaloniaHostDataRootGeneration"],
            HostDataRootPolicy.CurrentGeneration);
    }

    [Fact]
    public void VersionPolicy_四个插件的项目程序集清单与兼容区间一致()
    {
        var hostProfile = HostCompatibilityProfile.Current;
        foreach (var plugin in GetPluginReleases())
        {
            var projectPath = Path.Combine(RepositoryRoot, plugin.ProjectPath);
            var projectVersion = ReadProjectProperty(projectPath, "PluginVersion");
            Assert.True(
                projectVersion.Split('.').Length == 3 &&
                Version.TryParse(projectVersion, out _),
                $"{plugin.Name} PluginVersion 必须是 major.minor.patch 数字版本，" +
                $"实际为 '{projectVersion}'。");
            AssertVersionFact(
                $"{plugin.Name} Version mapping",
                "$(PluginVersion)",
                ReadProjectProperty(projectPath, "Version"));
            AssertVersionFact(
                $"{plugin.Name} FileVersion mapping",
                "$(PluginVersion).0",
                ReadProjectProperty(projectPath, "FileVersion"));
            AssertVersionFact(
                $"{plugin.Name} InformationalVersion mapping",
                "$(PluginVersion)",
                ReadProjectProperty(projectPath, "InformationalVersion"));
            AssertVersionFact(
                $"{plugin.Name} AssemblyVersion mapping",
                "$(PluginVersion).0",
                ReadProjectProperty(projectPath, "AssemblyVersion"));
            AssertVersionFact(
                $"{plugin.Name} actual FileVersion",
                projectVersion + ".0",
                ReadFileVersion(plugin.Assembly));
            AssertVersionFact(
                $"{plugin.Name} actual InformationalVersion",
                projectVersion,
                ReadInformationalVersionCore(plugin.Assembly));

            var manifestDirectory = Path.GetDirectoryName(Path.Combine(
                RepositoryRoot,
                plugin.ManifestPath))!;
            var success = PluginManifestReader.TryRead(
                manifestDirectory,
                out var manifest,
                out var errorCode,
                out var errorDetail);
            Assert.True(
                success,
                $"{plugin.Name} 清单解析失败：{errorCode}: {errorDetail}");

            var assemblyVersion = plugin.Assembly.GetName().Version;
            Assert.True(
                PluginCompatibilityEvaluator.HasMatchingPluginVersion(
                    manifest!,
                    assemblyVersion),
                $"{plugin.Name} pluginVersion={manifest!.PluginVersion} 与入口程序集 " +
                $"AssemblyVersion={assemblyVersion} 不一致。");
            AssertVersionFact(
                $"{plugin.Name} project/manifest version",
                projectVersion,
                manifest.PluginVersion.ToString(3));
            AssertVersionFact(
                $"{plugin.Name} entryAssembly",
                plugin.Assembly.GetName().Name + ".dll",
                manifest.EntryAssembly);
            Assert.Equal(
                PluginManifestReader.CurrentSchemaVersion,
                manifest.SchemaVersion);

            var compatible = PluginCompatibilityEvaluator.TryEvaluate(
                manifest,
                hostProfile,
                out var compatibilityCode,
                out var compatibilityDetail);
            Assert.True(
                compatible,
                $"{plugin.Name} 不包含当前 Host/SDK 版本：" +
                $"{compatibilityCode}: {compatibilityDetail}");
        }
    }

    private static IReadOnlyList<PluginRelease> GetPluginReleases() =>
    [
        new(
            "BiliDownloader",
            typeof(BiliDownloaderPluginModule).Assembly,
            Path.Combine("Plugins", "BiliDownloader", "BiliDownloader", "BiliDownloader.csproj"),
            Path.Combine("Plugins", "BiliDownloader", "BiliDownloader", "plugin.manifest.json")),
        new(
            "DaTangAccountingHelpPlug",
            typeof(DaTangAccountingHelpPluginModule).Assembly,
            Path.Combine("Plugins", "DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug.csproj"),
            Path.Combine("Plugins", "DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug", "plugin.manifest.json")),
        new(
            "MyPlugTest",
            typeof(MyPlugTestPluginModule).Assembly,
            Path.Combine("Plugins", "MyPlugTest", "MyPlugTest", "MyPlugTest.csproj"),
            Path.Combine("Plugins", "MyPlugTest", "MyPlugTest", "plugin.manifest.json")),
        new(
            "MySmallTools",
            typeof(MySmallToolsPluginModule).Assembly,
            Path.Combine("Plugins", "MySmallTools", "MySmallTools", "MySmallTools.csproj"),
            Path.Combine("Plugins", "MySmallTools", "MySmallTools", "plugin.manifest.json")),
    ];

    private static IReadOnlyDictionary<string, string> ReadVersionProperties()
    {
        var document = XDocument.Load(Path.Combine(
            RepositoryRoot,
            "Directory.Version.props"));
        return document
            .Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
            .ToDictionary(
                element => element.Name.LocalName,
                element => element.Value.Trim(),
                StringComparer.Ordinal);
    }

    private static void AssertProjectMapping(
        string projectRelativePath,
        string propertyName,
        string expectedExpression) =>
        AssertVersionFact(
            $"{projectRelativePath}::{propertyName}",
            expectedExpression,
            ReadProjectProperty(
                Path.Combine(RepositoryRoot, projectRelativePath),
                propertyName));

    private static string ReadProjectProperty(string projectPath, string propertyName)
    {
        var document = XDocument.Load(projectPath);
        var values = document
            .Descendants()
            .Where(element => element.Name.LocalName == propertyName)
            .Select(element => element.Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            values.Length == 1,
            $"{projectPath} 应且只应声明一个 {propertyName}，实际为 {values.Length} 个。");
        return values[0];
    }

    private static string ReadInformationalVersionCore(Assembly assembly)
    {
        var value = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        Assert.False(
            string.IsNullOrWhiteSpace(value),
            $"{assembly.GetName().Name} 缺少 InformationalVersion。");
        var separator = value.IndexOf('+');
        return separator < 0 ? value : value[..separator];
    }

    private static string? ReadFileVersion(Assembly assembly) =>
        FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion;

    private static void AssertVersionFact(
        string factName,
        string expected,
        string? actual) =>
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"{factName} 漂移：期望 '{expected}'，实际 '{actual ?? "<null>"}'。");

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "MyAvaloniaManagement.sln")) &&
                File.Exists(Path.Combine(current.FullName, "Directory.Version.props")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"无法从测试输出目录 {AppContext.BaseDirectory} 定位仓库根和 Directory.Version.props。");
    }

    private sealed record PluginRelease(
        string Name,
        Assembly Assembly,
        string ProjectPath,
        string ManifestPath);
}
