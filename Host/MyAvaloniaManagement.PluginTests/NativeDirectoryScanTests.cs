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
