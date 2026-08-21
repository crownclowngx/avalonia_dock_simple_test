using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 使用四个当前 Managed Plugin 的真实构建输出验证统一入口、deps 解析和公共契约共享。
/// </summary>
public sealed class CurrentManagedPluginLoadingTests
{
    [Theory]
    [InlineData("BiliDownloader/BiliDownloader", "BiliDownloader", "BiliDownloader", "myavalonia.plugin.bili-downloader")]
    [InlineData("MyPlugTest/MyPlugTest", "MyPlugTest", "MyPlugTest", "myavalonia.plugin.my-plug-test")]
    [InlineData("DaTangAccountingHelpPlug/DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug", "DaTang", "myavalonia.plugin.datang-accounting-help")]
    [InlineData("MySmallTools/MySmallTools", "MySmallTools", "SmallTools", "myavalonia.plugin.my-small-tools")]
    public void 尚未迁移的业务插件从真实构建目录读取后被V2预检隔离(
        string projectPath,
        string assemblyName,
        string directoryName,
        string pluginId)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent?.Name
            ?? throw new InvalidOperationException("无法确定测试构建配置。");
        var packageRoot = Environment.GetEnvironmentVariable("MYAVALONIA_G3_PACKAGE_ROOT");
        var pluginDirectory = string.IsNullOrWhiteSpace(packageRoot)
            ? Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "Plugins",
                projectPath.Replace('/', Path.DirectorySeparatorChar),
                "bin",
                configuration,
                "net10.0"))
            : Path.GetFullPath(Path.Combine(packageRoot, "Controls", directoryName));
        var pluginAssemblyPath = Path.Combine(
            pluginDirectory,
            assemblyName + ".dll");

        Assert.True(
            File.Exists(pluginAssemblyPath),
            $"插件构建输出不存在：{pluginAssemblyPath}");
        Assert.True(
            File.Exists(Path.Combine(pluginDirectory, assemblyName + ".deps.json")),
            $"插件缺少标准 deps 入口：{assemblyName}");
        Assert.True(
            PluginManifestReader.TryRead(
                pluginDirectory,
                out var manifest,
                out var manifestErrorCode,
                out var manifestErrorDetail),
            $"插件清单无效：{manifestErrorCode}: {manifestErrorDetail}");
        Assert.Equal(pluginId, manifest!.PluginId.Value);
        Assert.Equal(assemblyName + ".dll", manifest.EntryPoint.Assembly);

        var context = new PluginLoadContext(pluginDirectory);
        var pluginAssembly = context.LoadFromAssemblyPath(pluginAssemblyPath);
        Assert.True(PluginCompatibilityEvaluator.HasMatchingPluginVersion(
            manifest,
            pluginAssembly.GetName().Version));
        var entryType = pluginAssembly.GetType(
            manifest.EntryPoint.Type, throwOnError: false, ignoreCase: false);
        Assert.False(PluginModulePreflight.TryValidate(
            entryType, out var validatedType, out var entryCode, out _));
        Assert.Null(validatedType);
        Assert.Equal(HostDiagnosticCodes.PluginEntryInvalid, entryCode);
        Assert.Equal(pluginId, manifest.PluginId.Value);
    }
}
