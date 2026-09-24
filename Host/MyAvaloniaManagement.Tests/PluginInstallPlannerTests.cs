using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Installation;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Tests;

public sealed class PluginInstallPlannerTests
{
    private static PluginManifest Manifest(string version, string id = "myavalonia.plugin.test") =>
        new(2, new PluginId(id), Version.Parse(version), new("Probe.dll", "Probe.Module"), new(new(3, 0, 0), new(4, 0, 0)));

    [Theory]
    [InlineData("1.10.0", "1.9.0", false, (int)PluginInstallAction.Upgrade, false)]
    [InlineData("1.0.0", "2.0.0", false, (int)PluginInstallAction.Downgrade, true)]
    [InlineData("1.0.0", "1.0.0", false, (int)PluginInstallAction.Reinstall, true)]
    [InlineData("1.0.0", "1.0.0", true, (int)PluginInstallAction.Unchanged, false)]
    public void 版本数字比较及同版本内容策略(string candidate, string current, bool sameHash, int expected, bool explicitChoice)
    {
        var package = new CheckedPluginPackage("test", "renamed", "unused", Manifest(candidate), "zip", "new", false);
        var result = PluginInstallPlanner.Plan(package, [new("existing", Manifest(current), sameHash ? "new" : "old")]);
        Assert.Equal((PluginInstallAction)expected, result.Action); Assert.Equal(explicitChoice, result.RequiresExplicitChoice);
        Assert.Equal("existing", result.TargetDirectory);
    }

    [Fact]
    public void 新装目录空闲而相同目录不同身份拒绝()
    {
        var package = new CheckedPluginPackage("test", "Probe", "unused", Manifest("1.0.0"), "zip", "new", false);
        Assert.Equal(PluginInstallAction.Install, PluginInstallPlanner.Plan(package, []).Action);
        Assert.Throws<PluginInstallException>(() => PluginInstallPlanner.Plan(package, [new("probe", Manifest("1.0.0", "myavalonia.plugin.other"), "old")]));
        Assert.Throws<PluginInstallException>(() => PluginInstallPlanner.Plan(package, [new("one", Manifest("1.0.0"), "old"), new("two", Manifest("2.0.0"), "new")]));
    }
}
