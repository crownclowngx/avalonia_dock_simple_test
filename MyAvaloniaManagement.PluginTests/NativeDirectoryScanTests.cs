using MyAvaloniaManagement.Business.Helpers;
using Xunit;

namespace MyAvaloniaManagement.PluginTests;

public sealed class NativeDirectoryScanTests
{
    [Fact]
    public void PluginScannerAndResolver_DoNotEnterNativeDirectory()
    {
        var rootName = "NativeScanTest-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(AppContext.BaseDirectory, rootName);
        var plugin = Path.Combine(root, "PluginUnderTest");
        var native = Path.Combine(plugin, "native", "win-x64", "libvlc");
        Directory.CreateDirectory(native);

        try
        {
            var sourceAssembly = typeof(NativeDirectoryScanTests).Assembly.Location;
            File.Copy(sourceAssembly, Path.Combine(native, Path.GetFileName(sourceAssembly)));

            var scanned = AssemblyLoaderHelper.LoadPluginsFromDirectories(rootName);
            Assert.Empty(scanned);

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
