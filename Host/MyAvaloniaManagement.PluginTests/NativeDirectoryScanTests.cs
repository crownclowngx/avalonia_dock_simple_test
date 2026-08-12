using MyAvaloniaManagement.Business.Helpers;
using Xunit;

namespace MyAvaloniaManagement.PluginTests;

public sealed class NativeDirectoryScanTests
{
    [Theory]
    [InlineData("native")]
    [InlineData("NATIVE")]
    [InlineData("runtimes")]
    [InlineData("RunTimes")]
    [InlineData("libvlc")]
    [InlineData("LIBVLC")]
    public void PluginScannerAndResolver_DoNotEnterNativeDirectories(string excludedName)
    {
        var rootName = "NativeScanTest-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(AppContext.BaseDirectory, rootName);
        var plugin = Path.Combine(root, "PluginUnderTest");
        var native = Path.Combine(plugin, "managed", excludedName, "deep");
        Directory.CreateDirectory(native);

        try
        {
            var sourceAssembly = typeof(NativeDirectoryScanTests).Assembly.Location;
            File.Copy(sourceAssembly, Path.Combine(native, Path.GetFileName(sourceAssembly)));

            var scanned = AssemblyLoaderHelper.LoadPluginsFromDirectories(rootName);
            Assert.Empty(scanned);

            // PluginLoadContext 现在同样强制清单。复制一个不会被主动加载的有效根入口，
            // 只用于建立目录索引，从而继续验证 native/runtimes/libvlc 子树不会参与托管解析。
            var hostAssembly = typeof(AssemblyLoaderHelper).Assembly.Location;
            File.Copy(hostAssembly, Path.Combine(plugin, Path.GetFileName(hostAssembly)));
            File.WriteAllText(
                Path.Combine(plugin, "plugin.manifest.json"),
                $$"""
                {
                  "schemaVersion": 1,
                  "pluginId": "myavalonia.plugin.native-scan-test",
                  "pluginVersion": "1.0.0",
                  "entryAssembly": "{{Path.GetFileName(hostAssembly)}}",
                  "compatibility": {
                    "hostApi": { "minInclusive": "1.0.0", "maxExclusive": "2.0.0" },
                    "commonContract": { "minInclusive": "1.0.0", "maxExclusive": "2.0.0" }
                  }
                }
                """);

            var context = new PluginLoadContext(plugin);
            var resolved = context.ResolveAssembly(typeof(NativeDirectoryScanTests).Assembly.FullName!);
            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
