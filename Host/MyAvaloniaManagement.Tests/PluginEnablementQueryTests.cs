using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Enablement;
using MyAvaloniaManagement.Business.PluginStatus;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.ViewModels.PluginStatus;

namespace MyAvaloniaManagement.Tests;

/// <summary>区分用户意图、发现事实和运行失败，避免把 IsAvailable=false 当作禁用。</summary>
public sealed class PluginEnablementQueryTests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.enablement-test");

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task 启动与下次四种组合正确显示且改回取消待重启(bool startupEnabled, bool nextEnabled)
    {
        using var files = new EnablementTestFiles();
        var initial = files.Store.TrySave(files.Store.Load(), new(startupEnabled ? [] : [Owner])).Snapshot;
        var service = new PluginEnablementService(files.Store, initial, [Owner]);
        var query = CreateQuery(files.Directory, initial, service);
        Assert.True((await service.SetEnabledAsync(Owner, nextEnabled)).Success);
        var item = Assert.Single(query.Capture());
        Assert.Equal(!startupEnabled, item.IsDisabled);
        Assert.Equal(startupEnabled != nextEnabled, item.RequiresRestart);
        Assert.Equal(nextEnabled, item.NextStartupEnabled);
        Assert.False(item.IsAvailable);
        Assert.Equal(startupEnabled, item.HasProblem); // 启用却未提交 Registry 是失败，正常禁用不是异常。
        using var model = new PluginStatusWindowViewModel(query, TimeProvider.System, enablement: service);
        model.Refresh();
        Assert.Contains("下次启动", model.CreateDiagnosticText());
        model.SelectedFilter = "待重启";
        Assert.Equal(startupEnabled != nextEnabled ? 1 : 0, model.VisibleItems.Count);
        Assert.True((await service.SetEnabledAsync(Owner, startupEnabled)).Success);
        model.Refresh();
        Assert.Empty(model.VisibleItems);
        model.SelectedFilter = "已禁用";
        Assert.Equal(startupEnabled ? 0 : 1, model.VisibleItems.Count);
        Assert.Equal(startupEnabled, initial.Settings!.IsEnabled(Owner));
    }

    [Fact]
    public void 目录诊断与候选合并并保留失败插件的操作身份()
    {
        using var files = new EnablementTestFiles();
        using var diagnostics = HostDiagnosticSession.Start(files.Directory);
        diagnostics.Report(new(HostDiagnosticCodes.PluginEntryInvalid, HostDiagnosticPhase.PluginRootDiscovery)
            { PluginDirectory = "Plugin", Exception = new Exception("secret-canary") });
        var startup = files.Store.Load();
        var service = new PluginEnablementService(files.Store, startup, [Owner]);
        var item = Assert.Single(CreateQuery(files.Directory, startup, service, diagnostics).Capture());
        Assert.Equal(Owner.Value, item.PluginId);
        Assert.True(item.CanSetEnablement);
        Assert.True(item.HasProblem);
        Assert.Single(item.Diagnostics);
        Assert.DoesNotContain("canary", item.Detail);
    }

    [Fact]
    public void 配置未知不冒充禁用且查询不创建文件()
    {
        using var files = new EnablementTestFiles();
        var invalid = new PluginEnablementReadResult(null, "invalid", "PLUGIN_ENABLEMENT_INVALID");
        var service = new PluginEnablementService(files.Store, invalid, [Owner]);
        var query = CreateQuery(files.Directory, invalid, service);
        var item = Assert.Single(query.Capture());
        Assert.False(item.IsDisabled);
        Assert.False(item.RequiresRestart);
        Assert.False(item.CanSetEnablement);
        Assert.Null(item.NextStartupEnabled);
        Assert.True(item.HasProblem);
        Assert.False(File.Exists(files.Path));
    }

    private static PluginStatusQuery CreateQuery(string root, PluginEnablementReadResult startup,
        IPluginEnablementState state, HostDiagnosticSession? diagnostics = null)
    {
        var manifest = new PluginManifest(2, Owner, new(1, 0, 0, 0), new("Plugin.dll", "Plugin.Module"), new(new(3, 0, 0, 0), new(4, 0, 0, 0)));
        var snapshot = new PluginDiscoverySnapshot([], new Dictionary<System.Reflection.Assembly, PluginManifest>(),
            new Dictionary<System.Reflection.Assembly, Type>(), [], [new(Path.Combine(root, "Plugin"), manifest)], startup);
        var registry = new PluginRegistry([], []);
        return new(registry, new PluginAvailabilityReadModel(new PluginLifecycleStateStore(registry)), diagnostics, snapshot, state);
    }
}
