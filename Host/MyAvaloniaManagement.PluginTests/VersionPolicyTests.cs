using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using BiliDownloader.Plugin;
using DaTangAccountingHelpPlug.Plugin;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.ViewModels.Hello;
using MyAvaloniaManagement.PluginSdk;
using MyPlugTest.Plugin;
using MySmallTools.Plugin;
using V2PluginModule = MyAvaloniaManagement.PluginSdk.UI.IPluginModule;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 验证集中版本政策、实际程序集元数据和构建生成的插件清单没有发生漂移。
/// </summary>
/// <remarks>
/// 设计意图：这是仓库版本政策测试，不是运行时插件发现测试。它读取插件的声明式
/// MSBuild 属性、公共版本映射与本次构建生成的清单，再与实际程序集交叉验证，使版本
/// 复制错误在普通非发布构建中给出具体字段，而不是等到用户启动宿主后才得到笼统诊断。
/// </remarks>
public sealed class VersionPolicyTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void VersionPolicy_产品与Sdk程序集元数据来自集中属性()
    {
        var properties = ReadVersionProperties();
        var hostAssembly = typeof(WelcomeViewModel).Assembly;
        // manifest v2 的单一 SDK 区间直接来自最终 Core 契约程序集；UI 程序集同版本事实
        // 由 PluginSdkCompatibilityProfile 和 SDK 专项门禁交叉验证。
        var sdkContractAssembly = typeof(PluginId).Assembly;

        Assert.Equal("2.0.0", properties["MyAvaloniaProductVersion"]);
        Assert.Equal("2.0.0", properties["MyAvaloniaPluginSdkVersion"]);
        Assert.False(
            properties.ContainsKey("MyAvaloniaHostApiAssemblyVersion"),
            "V2 不得继续维护独立 Host API 版本事实。");

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
            properties["MyAvaloniaProductAssemblyVersion"],
            hostAssembly.GetName().Version?.ToString());

        AssertVersionFact(
            "Plugin SDK Version/InformationalVersion",
            properties["MyAvaloniaPluginSdkVersion"],
            ReadInformationalVersionCore(sdkContractAssembly));
        AssertVersionFact(
            "Plugin SDK FileVersion",
            properties["MyAvaloniaPluginSdkFileVersion"],
            ReadFileVersion(sdkContractAssembly));
        AssertVersionFact(
            "Plugin SDK AssemblyVersion",
            properties["MyAvaloniaPluginSdkAssemblyVersion"],
            sdkContractAssembly.GetName().Version?.ToString());

        AssertProjectMapping(
            Path.Combine("Host", "MyAvaloniaManagement", "MyAvaloniaManagement.csproj"),
            "Version",
            "$(MyAvaloniaProductVersion)");
        AssertProjectMapping(
            Path.Combine("Host", "MyAvaloniaManagement", "MyAvaloniaManagement.csproj"),
            "AssemblyVersion",
            "$(MyAvaloniaProductAssemblyVersion)");
        AssertProjectMapping(
            Path.Combine("Host", "MyAvaloniaManagement.PluginSdk", "MyAvaloniaManagement.PluginSdk.csproj"),
            "PackageVersion",
            "$(MyAvaloniaPluginSdkVersion)");
        AssertProjectMapping(
            Path.Combine("Host", "MyAvaloniaManagement.PluginSdk.UI", "MyAvaloniaManagement.PluginSdk.UI.csproj"),
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
    public void VersionPolicy_manifestDocument与布局均接入V2()
    {
        var properties = ReadVersionProperties();

        Assert.Equal("2", properties["MyAvaloniaV2ManifestSchemaVersion"]);
        Assert.Equal("2", properties["MyAvaloniaV2DocumentEnvelopeSchemaVersion"]);
        Assert.Equal("2", properties["MyAvaloniaV2LayoutSchemaVersion"]);
        Assert.Equal("layout-v2.json", properties["MyAvaloniaV2LayoutFileName"]);

        Assert.Equal(2, PluginManifestReader.CurrentSchemaVersion);
        Assert.Equal(2, DocumentEnvelopeSerializer.CurrentSchemaVersion);
        Assert.Equal(2, DockLayoutSnapshotV2.CurrentSchemaVersion);
        Assert.Equal("layout-v2.json", DockLayoutStore.LayoutFileName);
        AssertVersionFact(
            "Host data root generation",
            properties["MyAvaloniaHostDataRootGeneration"],
            HostDataRootPolicy.CurrentGeneration);
    }

    [Fact]
    public void VersionPolicy_四个插件的项目程序集清单与兼容区间一致()
    {
        var sharedProps = Path.Combine(
            RepositoryRoot,
            "build",
            "MyAvaloniaManagement.ManagedPlugin.props");
        AssertVersionFact(
            "Managed Plugin Version mapping",
            "$(PluginVersion)",
            ReadProjectProperty(sharedProps, "Version"));
        AssertVersionFact(
            "Managed Plugin FileVersion mapping",
            "$(PluginVersion).0",
            ReadProjectProperty(sharedProps, "FileVersion"));
        AssertVersionFact(
            "Managed Plugin InformationalVersion mapping",
            "$(PluginVersion)",
            ReadProjectProperty(sharedProps, "InformationalVersion"));
        AssertVersionFact(
            "Managed Plugin AssemblyVersion mapping",
            "$(PluginVersion).0",
            ReadProjectProperty(sharedProps, "AssemblyVersion"));

        var hostProfile = PluginSdkCompatibilityProfile.Current;
        var versionProperties = ReadVersionProperties();
        var sdkVersion = Version.Parse(versionProperties["MyAvaloniaPluginSdkVersion"]);
        var sdkNextMajor = Version.Parse(versionProperties["MyAvaloniaPluginSdkNextMajorVersion"]);
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
                $"{plugin.Name} PluginVersion/SDK V2",
                sdkVersion.ToString(3),
                projectVersion);
            AssertVersionFact(
                $"{plugin.Name} ManagedPlugin",
                "true",
                ReadProjectProperty(projectPath, "ManagedPlugin"));
            AssertVersionFact(
                $"{plugin.Name} stable plugin id",
                plugin.PluginId,
                ReadProjectProperty(projectPath, "ManagedPluginId"));
            AssertVersionFact(
                $"{plugin.Name} directory name",
                plugin.DirectoryName,
                ReadProjectProperty(projectPath, "ManagedPluginDirectoryName"));
            AssertVersionFact(
                $"{plugin.Name} runtime identifier",
                "win-x64",
                ReadProjectProperty(projectPath, "ManagedPluginRuntimeIdentifier"));
            AssertVersionFact(
                $"{plugin.Name} actual FileVersion",
                projectVersion + ".0",
                ReadFileVersion(plugin.Assembly));
            AssertVersionFact(
                $"{plugin.Name} actual InformationalVersion",
                projectVersion,
                ReadInformationalVersionCore(plugin.Assembly));

            // 测试输出会把四个 ProjectReference 的同名 plugin.manifest.json 覆盖到同一目录，
            // 因此必须回到各插件自己的 bin/<Configuration>/net10.0 读取生成清单。
            // 这里仍然读取构建产物，不允许源码树保留第二份手写事实。
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
                ?? throw new DirectoryNotFoundException("无法从测试输出确定构建配置。");
            var manifestDirectory = Path.Combine(
                Path.GetDirectoryName(projectPath)!,
                "bin",
                configuration,
                "net10.0");
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
                $"{plugin.Name} project/manifest id",
                plugin.PluginId,
                manifest.PluginId.Value);
            AssertVersionFact(
                $"{plugin.Name} entryPoint.assembly",
                plugin.Assembly.GetName().Name + ".dll",
                manifest.EntryPoint.Assembly);
            var expectedEntryType = Assert.Single(plugin.Assembly.ExportedTypes, type =>
                typeof(V2PluginModule).IsAssignableFrom(type) &&
                !type.IsAbstract).FullName!;
            AssertVersionFact(
                $"{plugin.Name} entry type expression",
                expectedEntryType,
                ReadProjectProperty(projectPath, "ManagedPluginEntryType"));
            AssertVersionFact(
                $"{plugin.Name} manifest entry type",
                expectedEntryType,
                manifest.EntryPoint.Type);
            Assert.Equal(
                PluginManifestReader.CurrentSchemaVersion,
                manifest.SchemaVersion);
            AssertVersionFact(
                $"{plugin.Name} SDK min expression",
                "$(MyAvaloniaPluginSdkVersion)",
                ReadProjectProperty(projectPath, "ManagedPluginSdkMinInclusive"));
            AssertVersionFact(
                $"{plugin.Name} SDK max expression",
                "$(MyAvaloniaPluginSdkNextMajorVersion)",
                ReadProjectProperty(projectPath, "ManagedPluginSdkMaxExclusive"));
            AssertVersionFact(
                $"{plugin.Name} SDK minInclusive",
                sdkVersion.ToString(3),
                manifest.Sdk.MinInclusive.ToString(3));
            AssertVersionFact(
                $"{plugin.Name} SDK maxExclusive",
                sdkNextMajor.ToString(3),
                manifest.Sdk.MaxExclusive.ToString(3));

            var compatible = PluginCompatibilityEvaluator.TryEvaluate(
                manifest,
                hostProfile,
                out var compatibilityCode,
                out var compatibilityDetail);
            Assert.True(
                compatible,
                $"{plugin.Name} 不包含当前 Plugin SDK 版本：" +
                $"{compatibilityCode}: {compatibilityDetail}");
        }
    }

    private static IReadOnlyList<PluginRelease> GetPluginReleases() =>
    [
        new(
            "BiliDownloader",
            typeof(BiliDownloaderPluginModule).Assembly,
            Path.Combine("Plugins", "BiliDownloader", "BiliDownloader", "BiliDownloader.csproj"),
            "myavalonia.plugin.bili-downloader",
            "BiliDownloader"),
        new(
            "DaTangAccountingHelpPlug",
            typeof(DaTangAccountingHelpPluginModule).Assembly,
            Path.Combine("Plugins", "DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug.csproj"),
            "myavalonia.plugin.datang-accounting-help",
            "DaTang"),
        new(
            "MyPlugTest",
            typeof(MyPlugTestPluginModule).Assembly,
            Path.Combine("Plugins", "MyPlugTest", "MyPlugTest", "MyPlugTest.csproj"),
            "myavalonia.plugin.my-plug-test",
            "MyPlugTest"),
        new(
            "MySmallTools",
            typeof(MySmallToolsPluginModule).Assembly,
            Path.Combine("Plugins", "MySmallTools", "MySmallTools", "MySmallTools.csproj"),
            "myavalonia.plugin.my-small-tools",
            "SmallTools"),
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
        string PluginId,
        string DirectoryName);
}
