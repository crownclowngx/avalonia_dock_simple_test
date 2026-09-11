using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.ToolCenter;

namespace MyAvaloniaManagement.Tests;

/// <summary>覆盖历史布局和独立偏好的定向迁移；测试只写自己的临时目录。</summary>
public sealed class PluginStatusMigrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void 插件状态在隐藏停靠和自动收起状态下均可退役并保留其他布局(bool visible, bool pinned)
    {
        using var context = new TestHostContext();
        var store = new DockLayoutStore(Path.Combine(context.TempDirectory, "legacy.json"));
        var first = HostExtensionIds.FileSystemTree.Value;
        var second = HostExtensionIds.PluginMenu.Value;
        var old = new DockLayoutSnapshotV2
        {
            ActiveToolId = RetiredHostToolIds.PluginStatus,
            Panes = [new() { Id = "right", Proportion = 0.36 }],
            Tools =
            [
                new() { Id = first, DockId = "tools", Order = 0, IsVisible = false },
                new() { Id = RetiredHostToolIds.PluginStatus, DockId = "tools", Order = 1, IsVisible = visible, IsPinned = pinned },
                new() { Id = RetiredHostToolIds.ToolManagement, DockId = "tools", Order = 2, IsVisible = true },
                new() { Id = second, DockId = "tools", Order = 3, IsVisible = true, IsPinned = true }
            ]
        };
        store.Save(old);
        var originalBytes = File.ReadAllBytes(store.LayoutPath);
        var migrated = Assert.IsType<DockLayoutSnapshotV2>(store.Load());
        Assert.Null(migrated.ActiveToolId);
        Assert.Equal([first, second], migrated.Tools.Select(item => item.Id));
        Assert.Equal([0, 1], migrated.Tools.Select(item => item.Order));
        Assert.False(migrated.Tools[0].IsVisible);
        Assert.True(migrated.Tools[1].IsVisible);
        Assert.True(migrated.Tools[1].IsPinned);
        Assert.Equal(0.36, Assert.Single(migrated.Panes).Proportion);
        Assert.Same(migrated, RetiredToolLayoutMigration.Apply(migrated));
        Assert.Equal(originalBytes, File.ReadAllBytes(store.LayoutPath));
        store.Save(migrated);
        Assert.Equal(originalBytes, File.ReadAllBytes(Assert.Single(Directory.GetFiles(context.TempDirectory, "*.pre-tool-retirement.bak"))));
        store.Save(store.Load()!);
        Assert.Single(Directory.GetFiles(context.TempDirectory, "*.pre-tool-retirement.bak"));
    }

    [Fact]
    public void 仅退役项可以迁移为空而其他未知项和活动工具引用仍保留()
    {
        var unknown = new DockToolSnapshotV2 { Id = "myavalonia.plugin.missing.tool.main", DockId = "tools", Order = 1 };
        var old = new DockLayoutSnapshotV2
        {
            ActiveToolId = unknown.Id,
            Tools = [new() { Id = RetiredHostToolIds.PluginStatus, DockId = "tools", Order = 0 }, unknown]
        };
        var migrated = RetiredToolLayoutMigration.Apply(old);
        Assert.Equal(unknown.Id, Assert.Single(migrated.Tools).Id);
        Assert.Equal(unknown.Id, migrated.ActiveToolId);
        old.Tools.Remove(unknown);
        old = old with { ActiveToolId = RetiredHostToolIds.PluginStatus };
        Assert.Empty(RetiredToolLayoutMigration.Apply(old).Tools);
        Assert.Null(RetiredToolLayoutMigration.Apply(old).ActiveToolId);
    }

    [Fact]
    public void 历史收藏最近分类与名称一起迁移且缺失插件偏好不会丢失()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();
        var path = Path.Combine(context.TempDirectory, "old-preferences.json");
        const string missing = "myavalonia.plugin.missing.tool.main";
        var ids = new[] { RetiredHostToolIds.PluginStatus, missing, RetiredHostToolIds.ToolManagement, HostExtensionIds.PluginMenu.Value };
        var original = JsonSerializer.Serialize(new ToolCenterSettings
        {
            FavoriteToolIds = ids,
            RecentToolIds = ids,
            Categories = [new("user:custom", "我的分类")],
            ToolCategoryAssignments = ids.ToDictionary(id => id, _ => "user:custom"),
            LastKnownToolNames = ids.ToDictionary(id => id, _ => "历史名称")
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(path, original);
        var preferences = new ToolCenterPreferences(new ToolCenterPreferencesStore(path));
        var expected = new[] { missing, HostExtensionIds.PluginMenu.Value };
        Assert.Equal(expected, preferences.Current.FavoriteToolIds);
        Assert.Equal(expected, preferences.Current.RecentToolIds);
        Assert.Equal(expected.Order(), preferences.Current.ToolCategoryAssignments.Keys.Order());
        Assert.Equal(expected.Order(), preferences.Current.LastKnownToolNames.Keys.Order());
        Assert.Single(preferences.Current.Categories);
        Assert.Equal(original, File.ReadAllText(path));
        var query = new ToolCenterQuery(context.Provider.GetRequiredService<ToolWorkspaceReadModel>(), preferences);
        Assert.DoesNotContain(query.Capture(), item => RetiredHostToolIds.Contains(item.ToolId));
        Assert.Equal(missing, Assert.Single(query.Capture(), item => item.IsMissing).ToolId);
        preferences.RememberAccess(HostExtensionIds.PluginMenu.Value, "插件分组菜单");
        var saved = new ToolCenterPreferencesStore(path).Load();
        Assert.DoesNotContain(saved.FavoriteToolIds, RetiredHostToolIds.Contains);
        Assert.DoesNotContain(saved.RecentToolIds, RetiredHostToolIds.Contains);
        Assert.Equal(expected, saved.FavoriteToolIds);
    }
}
