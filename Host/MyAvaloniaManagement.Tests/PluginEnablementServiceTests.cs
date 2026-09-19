using MyAvaloniaManagement.Business.Plugins.Enablement;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Tests;

/// <summary>服务是已保存意图的唯一所有者；并发、失败和重读均不能反向更改启动快照。</summary>
public sealed class PluginEnablementServiceTests
{
    [Fact]
    public async Task 重启冻结期间拒绝写入和重读_解除后仍能提交原设置()
    {
        using var files = new EnablementTestFiles();
        var id = new PluginId("myavalonia.plugin.first");
        var service = new PluginEnablementService(files.Store, files.Store.Load(), [id]);
        using (await service.PauseForRestartAsync(CancellationToken.None))
        {
            var rejected = await service.SetEnabledAsync(id, false);
            Assert.False(rejected.Success);
            Assert.Equal("PLUGIN_ENABLEMENT_RESTART_PENDING", rejected.ErrorCode);
            await Assert.ThrowsAsync<InvalidOperationException>(service.ReloadAsync);
            Assert.True(files.Store.Load().Settings!.IsEnabled(id));
        }
        Assert.True((await service.SetEnabledAsync(id, false)).Success);
        Assert.False(files.Store.Load().Settings!.IsEnabled(id));
    }
    [Fact]
    public async Task 连续保存与改回只更新已保存意图并拒绝未知身份()
    {
        using var files = new EnablementTestFiles();
        var first = new PluginId("myavalonia.plugin.first");
        var second = new PluginId("myavalonia.plugin.second");
        var startup = files.Store.Load();
        var service = new PluginEnablementService(files.Store, startup, [first, second]);
        var results = await Task.WhenAll(service.SetEnabledAsync(first, false), service.SetEnabledAsync(second, false));
        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(2, service.Current.Settings!.DisabledPluginIds.Count);
        Assert.Empty(startup.Settings!.DisabledPluginIds);
        Assert.True((await service.SetEnabledAsync(first, true)).Success);
        Assert.Equal([second], service.Current.Settings.DisabledPluginIds);
        Assert.False((await service.SetEnabledAsync(new("myavalonia.plugin.unknown"), false)).Success);
    }

    [Fact]
    public async Task 保存失败不提交且显式重读解决外部冲突()
    {
        using var files = new EnablementTestFiles();
        var id = new PluginId("myavalonia.plugin.first");
        var startup = files.Store.Load();
        var service = new PluginEnablementService(files.Store, startup, [id]);
        Assert.True(files.Store.TrySave(startup, new([id])).Success);
        Assert.False((await service.SetEnabledAsync(id, false)).Success);
        Assert.Same(startup, service.Current);
        await service.ReloadAsync();
        Assert.False(service.Current.Settings!.IsEnabled(id));
        Assert.True((await service.SetEnabledAsync(id, true)).Success);
        Assert.True(files.Store.Load().Settings!.IsEnabled(id));
    }
}
