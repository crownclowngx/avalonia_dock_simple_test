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
    private static readonly string[] BaseSdkPackages =
    [
        "Avalonia",
        "Dock.Model.Mvvm",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Newtonsoft.Json",
    ];

    private static readonly string[] UiProfilePackages =
    [
        "Avalonia.Themes.Fluent",
        "Dock.Avalonia",
        "Dock.Avalonia.Themes.Fluent",
        "Dock.Controls.ProportionalStackPanel",
        "Dock.Controls.Recycling",
        "Dock.Controls.Recycling.Model",
        "Irihi.Ursa",
        "Irihi.Ursa.Themes.Semi",
        "Semi.Avalonia",
    ];

    [Fact]
    public void 基础Sdk只引用公共契约编译所需包()
    {
        var project = LoadProject("Host", "MyAvaloniaManagementCommon", "MyAvaloniaManagementCommon.csproj");

        Assert.Equal(BaseSdkPackages, PackageReferences(project));
        Assert.Equal("MyAvaloniaManagement.PluginSdk", Property(project, "PackageId"));
        Assert.Equal("true", Property(project, "GenerateDocumentationFile"));
        Assert.Contains("CS1591", Property(project, "WarningsAsErrors"), StringComparison.Ordinal);
        Assert.DoesNotContain(
            project.Descendants("Folder"),
            item => string.Equals(
                item.Attribute("Include")?.Value.TrimEnd('\\', '/'),
                "Chain",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Host直接拥有全部UiProfile包而不是依赖Common传递()
    {
        var host = LoadProject("Host", "MyAvaloniaManagement", "MyAvaloniaManagement.csproj");
        var hostPackages = PackageReferences(host).ToHashSet(StringComparer.Ordinal);

        Assert.All(UiProfilePackages, package => Assert.Contains(package, hostPackages));
        Assert.Contains("Avalonia.Fonts.Inter", hostPackages);
        Assert.Contains("Avalonia.Desktop", hostPackages);
    }

    [Fact]
    public void UiProfile是同版本纯依赖包且第三方版本来自集中属性()
    {
        var profile = LoadProject(
            "Host",
            "MyAvaloniaManagement.PluginSdk.UI",
            "MyAvaloniaManagement.PluginSdk.UI.csproj");

        Assert.Equal(UiProfilePackages, PackageReferences(profile));
        Assert.Equal("false", Property(profile, "IncludeBuildOutput"));
        Assert.Equal("$(MyAvaloniaPluginSdkVersion)", Property(profile, "PackageVersion"));
        Assert.All(
            profile.Descendants("PackageReference"),
            item => Assert.Matches(@"^\[\$\(MyAvalonia.+UiVersion\)\]$", item.Attribute("VersionOverride")?.Value));
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
