using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using MyAvaloniaManagement.Business.Helpers;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 以可读白名单保护基础 SDK、宿主主题和可选 UI Profile 的依赖方向。
/// </summary>
public sealed class PluginSdkDependencyBoundaryTests
{
    private static readonly string[] BaseSdkPackages = [];

    private static readonly string[] BaseSdkBuildOnlyPackages =
    [
        "Microsoft.CodeAnalysis.PublicApiAnalyzers",
    ];

    private static readonly string[] UiProfilePackages =
    [
        "Avalonia",
        "Avalonia.Themes.Fluent",
        "Irihi.Ursa",
        "Irihi.Ursa.Themes.Semi",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Semi.Avalonia",
    ];

    [Fact]
    public void 基础Sdk只引用公共契约编译所需包()
    {
        var project = LoadProject(
            "Host", "MyAvaloniaManagement.PluginSdk", "MyAvaloniaManagement.PluginSdk.csproj");

        Assert.Equal(BaseSdkPackages, RuntimePackageReferences(project));
        Assert.Equal(BaseSdkBuildOnlyPackages, BuildOnlyPackageReferences(project));
        Assert.Equal("MyAvaloniaManagement.PluginSdk", Property(project, "PackageId"));
        Assert.Equal("true", Property(project, "GenerateDocumentationFile"));
        Assert.Contains("CS1591", Property(project, "WarningsAsErrors"), StringComparison.Ordinal);
        Assert.All(
            ["RS0016", "RS0017", "RS0024", "RS0025", "RS0036", "RS0037", "RS0041", "RS0048"],
            diagnostic => Assert.Contains(
                diagnostic,
                Property(project, "WarningsAsErrors"),
                StringComparison.Ordinal));

        var analyzer = Assert.Single(
            project.Descendants("PackageReference"),
            item => item.Attribute("Include")?.Value == "Microsoft.CodeAnalysis.PublicApiAnalyzers");
        Assert.Equal("all", analyzer.Attribute("PrivateAssets")?.Value);
        Assert.Equal(
            "runtime; build; native; contentfiles; analyzers",
            analyzer.Attribute("IncludeAssets")?.Value);
        Assert.Equal(
            [
                @"ApiCompatibility\$(MyAvaloniaPluginSdkApiBaseline)\PublicAPI.Shipped.txt",
                @"ApiCompatibility\$(MyAvaloniaPluginSdkApiBaseline)\PublicAPI.Unshipped.txt",
            ],
            project.Descendants("AdditionalFiles")
                .Select(item => item.Attribute("Include")?.Value)
                .Where(item => item is not null)
                .Cast<string>()
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray());
        Assert.Equal("MyAvaloniaManagement.PluginSdk", Property(project, "AssemblyName"));
    }

    [Fact]
    public void Host直接拥有全部UiProfile包而不是依赖Common传递()
    {
        var host = LoadProject("Host", "MyAvaloniaManagement", "MyAvaloniaManagement.csproj");
        var hostPackages = RuntimePackageReferences(host).ToHashSet(StringComparer.Ordinal);

        Assert.All(
            UiProfilePackages.Where(package =>
                package != "Microsoft.Extensions.DependencyInjection.Abstractions"),
            package => Assert.Contains(package, hostPackages));
        Assert.Contains("Microsoft.Extensions.DependencyInjection", hostPackages);
        Assert.Contains("Avalonia.Fonts.Inter", hostPackages);
        Assert.Contains("Avalonia.Desktop", hostPackages);
        Assert.DoesNotContain("Microsoft.CodeAnalysis.PublicApiAnalyzers", PackageReferences(host));
        Assert.Empty(host.Descendants("AdditionalFiles"));
    }

    [Fact]
    public void UiSdk是真实契约程序集且第三方版本来自集中属性()
    {
        var profile = LoadProject(
            "Host",
            "MyAvaloniaManagement.PluginSdk.UI",
            "MyAvaloniaManagement.PluginSdk.UI.csproj");

        Assert.Equal(UiProfilePackages, RuntimePackageReferences(profile));
        Assert.Equal(BaseSdkBuildOnlyPackages, BuildOnlyPackageReferences(profile));
        Assert.Equal("$(MyAvaloniaPluginSdkVersion)", Property(profile, "PackageVersion"));
        Assert.All(
            profile.Descendants("PackageReference")
                .Where(item => item.Attribute("Include")?.Value is not
                    ("Microsoft.Extensions.DependencyInjection.Abstractions" or
                     "Microsoft.CodeAnalysis.PublicApiAnalyzers")),
            item => Assert.Matches(@"^\[\$\(MyAvalonia.+UiVersion\)\]$", item.Attribute("VersionOverride")?.Value));
        Assert.Contains("Microsoft.CodeAnalysis.PublicApiAnalyzers", PackageReferences(profile));
        Assert.Equal(2, profile.Descendants("AdditionalFiles").Count());
        Assert.Contains(
            profile.Descendants("ProjectReference"),
            item => item.Attribute("Include")?.Value ==
                @"..\MyAvaloniaManagement.PluginSdk\MyAvaloniaManagement.PluginSdk.csproj");
        Assert.DoesNotContain(
            profile.Descendants("PackageReference"),
            item => item.Attribute("Include")?.Value?.StartsWith("Dock.", StringComparison.Ordinal) == true ||
                    item.Attribute("Include")?.Value == "Newtonsoft.Json");
    }

    [Fact]
    public void Legacy桥不可打包且不再拥有Sdk基线()
    {
        var legacy = LoadProject(
            "Host", "MyAvaloniaManagement.LegacyPluginContracts",
            "MyAvaloniaManagement.LegacyPluginContracts.csproj");

        Assert.Equal("false", Property(legacy, "IsPackable"));
        Assert.Empty(legacy.Descendants("PackageId"));
        Assert.Empty(legacy.Descendants("AdditionalFiles"));
        Assert.DoesNotContain("Microsoft.CodeAnalysis.PublicApiAnalyzers", PackageReferences(legacy));
    }

    [Theory]
    [InlineData("CommunityToolkit.Mvvm")]
    [InlineData("Semi.Avalonia")]
    [InlineData("Ursa")]
    [InlineData("Dock.Avalonia")]
    [InlineData("Dock.Controls.Recycling.Model")]
    public void 宿主支持的共享程序集由默认上下文提供(string assemblyName)
    {
        var policy = new HostContractAssemblyPolicy();
        var requested = AssemblyLoadContext.Default
            .LoadFromAssemblyName(new AssemblyName(assemblyName))
            .GetName();

        Assert.True(policy.IsShared(requested));
        var resolved = policy.ResolveSharedAssembly(requested);

        Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(resolved));
    }

    [Theory]
    [InlineData("Plugins", "BiliDownloader", "BiliDownloader", "BiliDownloader.csproj")]
    [InlineData("Plugins", "MyPlugTest", "MyPlugTest", "MyPlugTest.csproj")]
    [InlineData("Plugins", "MySmallTools", "MySmallTools", "MySmallTools.csproj")]
    [InlineData("Plugins", "DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug.csproj")]
    public void 使用Toolkit的仓库插件显式拥有编译依赖(params string[] projectPath)
    {
        var project = LoadProject(projectPath);

        Assert.Contains("CommunityToolkit.Mvvm", PackageReferences(project));
    }

    private static XDocument LoadProject(params string[] segments) =>
        XDocument.Load(Path.Combine([FindRepositoryRoot(), .. segments]));

    private static string[] PackageReferences(XDocument project) =>
        project.Descendants("PackageReference")
            .Select(item => item.Attribute("Include")?.Value)
            .Where(item => item is not null)
            .Cast<string>()
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

    private static string[] RuntimePackageReferences(XDocument project) =>
        project.Descendants("PackageReference")
            .Where(item => item.Attribute("PrivateAssets")?.Value != "all")
            .Select(item => item.Attribute("Include")?.Value)
            .Where(item => item is not null)
            .Cast<string>()
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

    private static string[] BuildOnlyPackageReferences(XDocument project) =>
        project.Descendants("PackageReference")
            .Where(item => item.Attribute("PrivateAssets")?.Value == "all")
            .Select(item => item.Attribute("Include")?.Value)
            .Where(item => item is not null)
            .Cast<string>()
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

    private static string Property(XDocument project, string name) =>
        project.Descendants(name).Single().Value;

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
