using MyAvaloniaManagement.Business.Plugins.Discovery;

namespace MyAvaloniaManagement.PluginTests;

public sealed class PluginRootDirectoryPolicyTests
{
    [Theory]
    [InlineData(true, "My Host.app/Contents/MacOS", "Controls")]
    [InlineData(false, "My Host.app/Contents/MacOS", "My Host.app/Contents/MacOS/Controls")]
    [InlineData(true, "publish", "publish/Controls")]
    [InlineData(true, "Other/Contents/MacOS", "Other/Contents/MacOS/Controls")]
    [InlineData(true, "My Host.app/Other/MacOS", "My Host.app/Other/MacOS/Controls")]
    public void 插件目录只在Mac标准应用包内重定向(bool isMacOS, string executable, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "macos-plugin-path-test");
        var actual = PluginRootDirectoryPolicy.Resolve(Path.Combine(root, executable) + Path.DirectorySeparatorChar, "Controls", isMacOS);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, expected)), actual);
    }

    [Fact]
    public void Mac应用包保留显式插件根目录()
    {
        var root = Path.Combine(Path.GetTempPath(), "macos-plugin-path-test");
        var explicitRoot = Path.Combine(root, "external", "Controls");
        Assert.Equal(explicitRoot, PluginRootDirectoryPolicy.Resolve(
            Path.Combine(root, "Host.app", "Contents", "MacOS"), explicitRoot, true));
    }
}
