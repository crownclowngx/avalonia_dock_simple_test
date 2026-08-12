using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 使用四个当前 Managed Plugin 的真实构建输出验证统一入口、deps 解析和公共契约共享。
/// </summary>
public sealed class CurrentManagedPluginLoadingTests
{
    [Theory]
    [InlineData("BiliDownloader/BiliDownloader", "BiliDownloader", "myavalonia.plugin.bili-downloader")]
    [InlineData("MyPlugTest/MyPlugTest", "MyPlugTest", "myavalonia.plugin.my-plug-test")]
    [InlineData("DaTangAccountingHelpPlug/DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug", "myavalonia.plugin.datang-accounting-help")]
    [InlineData("MySmallTools/MySmallTools", "MySmallTools", "myavalonia.plugin.my-small-tools")]
    public void 当前Managed插件可从真实构建目录发现唯一模块(
        string projectPath,
        string assemblyName,
        string pluginId)
    {
        var pluginDirectory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "Plugins",
            projectPath.Replace('/', Path.DirectorySeparatorChar),
            "bin",
            "Debug",
            "net10.0"));
        var pluginAssemblyPath = Path.Combine(
            pluginDirectory,
            assemblyName + ".dll");

        Assert.True(
            File.Exists(pluginAssemblyPath),
            $"插件构建输出不存在：{pluginAssemblyPath}");
        Assert.True(
            File.Exists(Path.Combine(pluginDirectory, assemblyName + ".deps.json")),
            $"插件缺少标准 deps 入口：{assemblyName}");

        var context = new PluginLoadContext(pluginDirectory);
        var pluginAssembly = context.LoadFromAssemblyPath(pluginAssemblyPath);
        var catalog = PluginModuleCatalog.Discover([pluginAssembly]);

        var module = Assert.Single(catalog.Modules);
        Assert.Equal(pluginId, module.PluginId.Value);
        Assert.True(typeof(IPluginModule).IsAssignableFrom(module.GetType()));
        Assert.Same(
            typeof(IPluginModule).Assembly,
            context.ResolveAssembly(typeof(IPluginModule).Assembly.FullName!));
    }
}
