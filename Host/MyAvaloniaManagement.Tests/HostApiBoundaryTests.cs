using System.Reflection;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.ViewModels.Tools;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证 Host 可执行程序集不会重新变成插件编译 API。</summary>
/// <remarks>
/// 这些断言使用可读事实替代 Host SHA256：自有命名空间不得导出、生产 ViewModel 不得拥有
/// 无参构造、历史静态定位器不得复活。框架生成的 CompiledAvaloniaXaml 类型不属于 Host
/// 自有命名空间，因此不会被误认为插件契约。
/// </remarks>
public sealed class HostApiBoundaryTests
{
    private static readonly Assembly HostAssembly = typeof(App).Assembly;

    [Fact]
    public void HostApiBoundary_宿主自有命名空间没有导出类型()
    {
        var exported = HostAssembly.ExportedTypes
            .Where(type =>
                type.Namespace?.StartsWith(
                    "MyAvaloniaManagement",
                    StringComparison.Ordinal) == true)
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(exported);
    }

    [Fact]
    public void HostApiBoundary_生产ViewModel没有无参构造()
    {
        var productionTypes = new[]
        {
            typeof(MainWindowViewModel),
            typeof(FileSystemTreeViewModel),
            typeof(PlugGroupMenuViewModel),
            typeof(PluginStatusViewModel),
            typeof(ToolManagementViewModel),
        };

        Assert.All(productionTypes, type => Assert.Null(type.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            Type.EmptyTypes,
            modifiers: null)));
    }

    [Fact]
    public void HostApiBoundary_静态服务定位器类型不存在()
    {
        Assert.Null(HostAssembly.GetType(
            "MyAvaloniaManagement.Business.Helpers.ServiceProvider",
            throwOnError: false));
    }

    [Fact]
    public void MainWindow不再暴露仅供测试调用的Document转发入口()
    {
        Assert.Null(typeof(MainWindowViewModel).GetMethod("CreateDocument"));
        Assert.Null(typeof(MainWindowViewModel).GetMethod("OpenDocumentByPath"));
        Assert.Null(typeof(PlugGroupMenuViewModel).GetMethod("CreateDocumentAsync"));
    }
}
