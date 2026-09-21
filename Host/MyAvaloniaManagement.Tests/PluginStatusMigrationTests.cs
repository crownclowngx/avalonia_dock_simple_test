using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.ToolCenter;

namespace MyAvaloniaManagement.Tests;

/// <summary>覆盖独立工具偏好的定向迁移；测试只写自己的临时目录。</summary>
public sealed class PluginStatusMigrationTests
{
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
