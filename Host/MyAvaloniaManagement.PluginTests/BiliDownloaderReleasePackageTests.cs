using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 使用宿主真实 PluginLoadContext 探测 G8 候选目录。环境变量由发布脚本指向 staging；
/// 普通测试运行则使用当前插件构建目录，从而保持零跳过且持续验证同一加载规则。
/// </summary>
public sealed class BiliDownloaderReleasePackageTests
{
    [Fact]
    public void Win_x64候选可由宿主上下文加载并发现插件模块()
    {
        var configured = Environment.GetEnvironmentVariable("BILIDOWNLOADER_G8_PLUGIN_ROOT");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent?.Name
            ?? throw new InvalidOperationException("无法确定测试构建配置。");
        var pluginRoot = string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "Plugins", "BiliDownloader", "BiliDownloader",
                "bin", configuration, "net10.0"))
            : Path.GetFullPath(configured);
        var pluginPath = Path.Combine(pluginRoot, "BiliDownloader.dll");
        Assert.True(File.Exists(pluginPath), $"候选目录缺少插件程序集：{pluginPath}");

        var context = new PluginLoadContext(pluginRoot);
        var assembly = context.LoadFromAssemblyPath(pluginPath);
        Assert.True(PluginManifestReader.TryRead(
            pluginRoot, out var manifest, out var errorCode, out var errorDetail),
            $"插件清单无效：{errorCode}: {errorDetail}");
        Assert.Equal("myavalonia.plugin.bili-downloader", manifest!.PluginId.Value);
        var entryType = assembly.GetType(
            manifest.EntryPoint.Type, throwOnError: false, ignoreCase: false);
        Assert.True(PluginModulePreflight.TryValidate(
            entryType, out var validatedType, out var entryCode, out var entryDetail),
            $"入口类型无效：{entryCode}: {entryDetail}");
        var module = Assert.IsAssignableFrom<IPluginModule>(
            Activator.CreateInstance(validatedType!));
        Assert.True(typeof(IPluginModule).IsAssignableFrom(module.GetType()));
        // 发布脚本的 staging 只允许 win-x64；普通构建目录仍保留跨平台资产，
        // 且独立项目输出不复制发布目标筛选出的全部私有依赖。因此仅在显式候选模式下
        // 验证私有依赖解析和 RID 封闭性，普通运行仍验证清单、入口和模块身份。
        if (!string.IsNullOrWhiteSpace(configured))
        {
            Assert.NotNull(context.ResolveAssembly("Microsoft.Data.Sqlite"));
            Assert.True(File.Exists(Path.Combine(
                pluginRoot, "runtimes", "win-x64", "native", "e_sqlite3.dll")));
            var runtimeRoots = Directory.EnumerateDirectories(Path.Combine(pluginRoot, "runtimes"))
                .Select(path => Path.GetFileName(path)!)
                .ToArray();
            Assert.Equal(new[] { "win-x64" }, runtimeRoots);
        }
    }
}
