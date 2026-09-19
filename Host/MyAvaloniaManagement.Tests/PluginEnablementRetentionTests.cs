using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Plugins.Enablement;
using MyAvaloniaManagement.Business.ToolCenter;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Tests;

/// <summary>以同一数据根模拟启用、缺失贡献、重新启用；验证开关与原有布局、收藏存储互不侵入。</summary>
public sealed class PluginEnablementRetentionTests
{
    [Fact]
    public async Task 禁用期间保存布局仍保留位置显示意图和收藏且重新启用后可投影()
    {
        using var files = new EnablementTestFiles();
        var owner = new PluginId("myavalonia.plugin.retention");
        var tool = owner.Value + ".tool.main";
        var previous = new DockLayoutSnapshotV3(3,
            new("main", DockWindowBounds.Default, DockLayoutNode.Split("split", "horizontal",
                [DockLayoutNode.Group("group", [tool], 0.3), DockLayoutNode.Documents() with { Proportion = 0.7 }])),
            [], [new(tool, "visible", DockLayoutIds.LeftTools, 0)]);
        var preferencesPath = Path.Combine(files.Directory, ToolCenterPreferencesStore.FileName);
        var preferences = new ToolCenterPreferences(new ToolCenterPreferencesStore(preferencesPath));
        preferences.ToggleFavorite(tool, "保留工具");
        preferences.RememberAccess(tool, "保留工具");
        var preferencesBytes = File.ReadAllBytes(preferencesPath);
        var service = new PluginEnablementService(files.Store, files.Store.Load(), [owner]);
        using (var layout = new DockLayoutV3Store(files.Directory))
        {
            Assert.True(layout.Save(previous));
            Assert.True((await service.SetEnabledAsync(owner, false)).Success);
            var liveWithoutPlugin = new DockLayoutSnapshotV3(3,
                new("main", DockWindowBounds.Default, DockLayoutNode.Documents()), [], []);
            Assert.True(layout.Save(DockLayoutTree.Merge(liveWithoutPlugin, layout.Load()!)));
        }
        Assert.True((await service.SetEnabledAsync(owner, true)).Success);
        using var reopened = new DockLayoutV3Store(files.Directory);
        var saved = reopened.Load()!;
        Assert.Equal(DockLayoutV3Tests.Write(previous), DockLayoutV3Tests.Write(saved));
        var projected = DockLayoutTree.Filter(saved.MainWindow.Root, new HashSet<string> { tool }, keepDocuments: true)!;
        Assert.Equal(0.3, projected.Children[0].Proportion);
        Assert.Equal(tool, Assert.Single(projected.Children[0].ToolIds));
        Assert.Equal("visible", Assert.Single(saved.Tools).State);
        Assert.Equal(preferencesBytes, File.ReadAllBytes(preferencesPath));
        var reloaded = new ToolCenterPreferences(new ToolCenterPreferencesStore(preferencesPath));
        Assert.Equal([tool], reloaded.Current.FavoriteToolIds);
        Assert.Equal([tool], reloaded.Current.RecentToolIds);
    }
}
