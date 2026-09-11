using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.ToolCenter;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Models.Tools;

namespace MyAvaloniaManagement.Tests;

/// <summary>通过生产目录、用例、原子存储验证工具中心，所有持久化均使用独立临时目录。</summary>
public sealed class ToolCenterTests
{
    private static readonly string First = HostExtensionIds.FileSystemTree.Value;
    private static readonly string Second = HostExtensionIds.PluginMenu.Value;

    [Fact]
    public void 收藏排序分类和最近记录跨存储实例恢复且不改变布局()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();
        var preferences = context.Provider.GetRequiredService<ToolCenterPreferences>();
        var layoutChanges = 0;
        context.Workspace.LayoutChanged += (_, _) => layoutChanges++;
        preferences.ToggleFavorite(First, "文件");
        preferences.ToggleFavorite(Second, "诊断");
        preferences.MoveFavorite(Second, -1);
        Assert.Equal([Second, First], preferences.Current.FavoriteToolIds);
        preferences.MoveFavorite(Second, -20);
        preferences.MoveFavorite("bad/id", 1);
        preferences.MoveFavorite(Second, 20);
        Assert.Equal([First, Second], preferences.Current.FavoriteToolIds);
        var category = preferences.AddCategory("  我的效率工具  ");
        Assert.True(category.Succeeded);
        preferences.AssignCategory(First, category.CategoryId!);
        Assert.True(preferences.RenameCategory(category.CategoryId!, "常用资源").Succeeded);
        Assert.Equal(category.CategoryId, preferences.CategoryFor(First));
        preferences.RememberAccess(First, "新名称");
        preferences.RememberAccess(Second, "诊断");
        preferences.RememberAccess(First, "新名称");
        Assert.Equal([First, Second], preferences.Current.RecentToolIds);
        Assert.Equal(0, layoutChanges);
        var loaded = new ToolCenterPreferences(new ToolCenterPreferencesStore(Path.Combine(context.TempDirectory, ToolCenterPreferencesStore.FileName)));
        Assert.Equal(preferences.Current.FavoriteToolIds, loaded.Current.FavoriteToolIds);
        Assert.Equal(preferences.Current.RecentToolIds, loaded.Current.RecentToolIds);
        Assert.Equal("常用资源", Assert.Single(loaded.Current.Categories).DisplayName);
        Assert.Equal("新名称", loaded.Current.LastKnownToolNames[First]);
        Assert.True(loaded.DeleteCategory(category.CategoryId!).Succeeded);
        Assert.Equal(ToolCenterPreferences.OtherCategoryId, loaded.CategoryFor(First));
        loaded.ToggleFavorite(First, "文件");
        Assert.Equal([Second], loaded.Current.FavoriteToolIds);
    }

    [Fact]
    public void 分类保留名称及非法输入受保护而最近列表有界去重()
    {
        using var context = new TestHostContext();
        var preferences = context.Provider.GetRequiredService<ToolCenterPreferences>();
        Assert.False(preferences.AddCategory(" ").Succeeded);
        Assert.False(preferences.AddCategory(new string('a', 61)).Succeeded);
        Assert.False(preferences.AddCategory("资源导航").Succeeded);
        var category = preferences.AddCategory("WORK");
        Assert.False(preferences.AddCategory("work").Succeeded);
        Assert.False(preferences.RenameCategory(category.CategoryId!, "其他工具").Succeeded);
        Assert.False(preferences.RenameCategory("builtin:other", "改变").Succeeded);
        Assert.False(preferences.DeleteCategory("builtin:other").Succeeded);
        preferences.AssignCategory(First, "unknown");
        preferences.AssignCategory("bad/id", category.CategoryId!);
        preferences.ToggleFavorite("bad/id", "坏标识");
        preferences.RememberAccess("bad/id", "坏标识");
        Assert.Empty(preferences.Current.ToolCategoryAssignments);
        Assert.Empty(preferences.Current.FavoriteToolIds);
        for (var index = 0; index < 30; index++) preferences.RememberAccess($"myavalonia.host.tool.test-{index}", "测试");
        Assert.Equal(20, preferences.Current.RecentToolIds.Length);
        Assert.EndsWith("test-29", preferences.Current.RecentToolIds[0]);
        Assert.EndsWith("test-10", preferences.Current.RecentToolIds[^1]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{broken")]
    public void 损坏偏好保留原文件且后续可以重新保存(string content)
    {
        using var context = new TestHostContext();
        var path = Path.Combine(context.TempDirectory, "preferences.json");
        File.WriteAllText(path, content);
        var store = new ToolCenterPreferencesStore(path);
        Assert.Empty(store.Load().FavoriteToolIds);
        Assert.NotEmpty(store.Error);
        Assert.Equal(content, File.ReadAllText(Assert.Single(Directory.GetFiles(context.TempDirectory, "*.invalid.bak"))));
        Assert.True(store.Save(new() { FavoriteToolIds = [First] }));
        Assert.Equal(string.Empty, store.Error);
        Assert.Equal([First], new ToolCenterPreferencesStore(path).Load().FavoriteToolIds);
    }

    [Fact]
    public void 未知版本只读且写入失败不撤销内存或阻断观察者()
    {
        using var context = new TestHostContext();
        var path = Path.Combine(context.TempDirectory, "future.json");
        const string future = "{\"schemaVersion\":99,\"futureField\":true}";
        File.WriteAllText(path, future);
        var preferences = new ToolCenterPreferences(new ToolCenterPreferencesStore(path));
        var notifications = 0;
        preferences.Changed += (_, _) => throw new InvalidOperationException("observer");
        preferences.Changed += (_, _) => notifications++;
        preferences.ToggleFavorite(First, "文件");
        Assert.Equal([First], preferences.Current.FavoriteToolIds);
        Assert.Equal(1, notifications);
        Assert.NotEmpty(preferences.SaveWarning);
        Assert.Equal(future, File.ReadAllText(path));
        var blockedPath = Path.Combine(path, "cannot-write.json");
        var blocked = new ToolCenterPreferences(new ToolCenterPreferencesStore(blockedPath));
        blocked.ToggleFavorite(Second, "诊断");
        Assert.Equal([Second], blocked.Current.FavoriteToolIds);
        Assert.NotEmpty(blocked.SaveWarning);
        Assert.False(File.Exists(blockedPath));
    }

    [Fact]
    public void 规范化坏字段不引入重复身份或悬空分类()
    {
        var settings = ToolCenterPreferencesStore.Normalize(new()
        {
            FavoriteToolIds = [First, First, "bad/id", null!],
            RecentToolIds = null!,
            Categories = [new("user:a", "A"), new("user:b", "A"), new("user:a", "B"),
                new("bad/id", "C"), new("user:c", "资源导航"), null!],
            ToolCategoryAssignments = new() { [First] = "user:missing", [Second] = "user:a", ["bad/id"] = "user:a" },
            LastKnownToolNames = new() { [First] = "文件", [Second] = " " }
        });
        Assert.Equal([First], settings.FavoriteToolIds);
        Assert.Empty(settings.RecentToolIds);
        Assert.Single(settings.Categories);
        Assert.Equal(ToolCenterPreferences.OtherCategoryId, settings.ToolCategoryAssignments[First]);
        Assert.Equal("user:a", settings.ToolCategoryAssignments[Second]);
        Assert.Single(settings.LastKnownToolNames);
    }

    [Fact]
    public void 查询全局搜索保留来源约束且历史缺失项只进入常用和最近()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();
        var preferences = context.Provider.GetRequiredService<ToolCenterPreferences>();
        var query = context.Provider.GetRequiredService<ToolCenterQuery>();
        const string missing = "myavalonia.plugin.missing.tool.main";
        preferences.ToggleFavorite(missing, "历史工具");
        preferences.RememberAccess(missing, "历史工具");
        preferences.ToggleFavorite(First, "文件");
        var category = preferences.AddCategory("效率");
        preferences.AssignCategory(First, category.CategoryId!);
        var items = query.Capture();
        Assert.Equal(3, items.Count);
        Assert.Equal(2, query.Filter(items, "all", "", "all").Count);
        Assert.Equal(2, query.Filter(items, "favorites", "", "all").Count);
        var placeholder = Assert.Single(query.Filter(items, "recent", "", "all"));
        Assert.True(placeholder.IsMissing);
        Assert.False(placeholder.CanOpen);
        Assert.False(placeholder.CanHide);
        Assert.Equal("插件缺失", placeholder.StatusText);
        Assert.Empty(query.Filter(items, "all", "历史工具", "all"));
        Assert.Equal(First, Assert.Single(query.Filter(items, "favorites", "效率", "host")).ToolId);
        Assert.Equal(First, Assert.Single(query.Filter(items, "visible", "FILE-SYSTEM", "all")).ToolId);
        Assert.Empty(query.Filter(items, "all", "效率", "missing-owner"));
        Assert.Single(query.Filter(items, category.CategoryId!, "", "all"));
        Assert.Empty(query.Filter(items, "visible", "", "all"));
        Assert.Equal(2, query.Filter(items, "hidden", "", "all").Count);
    }

    [Fact]
    public void 显隐结果可重复退出受控批量通知合并且失败访问不写最近()
    {
        using var context = new TestHostContext();
        var actions = context.Provider.GetRequiredService<ToolCenterActions>();
        var preferences = context.Provider.GetRequiredService<ToolCenterPreferences>();
        Assert.Equal(ToolOperationStatus.NotReady, actions.Open(First).Status);
        _ = context.CreateMainWindowViewModel();
        Assert.Equal(ToolOperationStatus.NotFound, actions.Open("myavalonia.host.tool.missing").Status);
        Assert.Empty(preferences.Current.RecentToolIds);
        var focused = 0;
        actions.FocusRequested += (_, _) => focused++;
        Assert.True(actions.Open(First, true).Succeeded);
        var instance = context.Workspace.CreatedTools[First];
        Assert.True(actions.Open(First).Succeeded);
        Assert.Same(instance, context.Workspace.CreatedTools[First]);
        Assert.Equal(1, focused);
        Assert.Equal([First], preferences.Current.RecentToolIds);
        Assert.True(actions.Open(Second).Succeeded);
        var notifications = 0;
        context.Workspace.LayoutChanged += (_, _) => throw new InvalidOperationException("observer");
        context.Workspace.LayoutChanged += (_, _) => notifications++;
        Assert.Equal(0, actions.HideAll().FailureCount);
        Assert.Equal(1, notifications);
        Assert.Equal(ToolOperationStatus.AlreadySatisfied, actions.Hide(First).Status);
        Assert.Equal(ToolOperationStatus.NotFound, actions.Hide("missing").Status);
        Assert.Single(context.Workspace.GetDocuments());
        context.Workspace.BeginShutdown();
        Assert.Equal(ToolOperationStatus.NotReady, actions.Open(First).Status);
        Assert.Equal(ToolOperationStatus.NotReady, actions.Hide(First).Status);
        Assert.All(context.Provider.GetRequiredService<ToolWorkspaceReadModel>().Capture(), item => Assert.False(item.CanOpen));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void 旧管理项迁移幂等并在覆盖前保留原始字节(bool visible, bool pinned)
    {
        using var context = new TestHostContext();
        var path = Path.Combine(context.TempDirectory, "legacy.json");
        var snapshot = new DockLayoutSnapshotV2
        {
            ActiveToolId = RetiredHostToolIds.ToolManagement,
            Panes = [new() { Id = "right", Proportion = 0.31 }],
            Tools = [new() { Id = First, DockId = "tools", Order = 0, IsVisible = true },
                new() { Id = RetiredHostToolIds.ToolManagement, DockId = "tools", Order = 1, IsVisible = visible, IsPinned = pinned },
                new() { Id = Second, DockId = "tools", Order = 2, IsVisible = true, IsPinned = true }]
        };
        var store = new DockLayoutStore(path);
        store.Save(snapshot);
        var original = File.ReadAllBytes(path);
        var migrated = Assert.IsType<DockLayoutSnapshotV2>(store.Load());
        Assert.Null(migrated.ActiveToolId);
        Assert.Equal([First, Second], migrated.Tools.Select(item => item.Id));
        Assert.Equal([0, 1], migrated.Tools.Select(item => item.Order));
        Assert.True(migrated.Tools[1].IsPinned);
        Assert.Equal(0.31, Assert.Single(migrated.Panes).Proportion);
        Assert.Same(migrated, RetiredToolLayoutMigration.Apply(migrated));
        Assert.Equal(original, File.ReadAllBytes(path));
        store.Save(migrated);
        Assert.Equal(original, File.ReadAllBytes(Assert.Single(Directory.GetFiles(context.TempDirectory, "*.pre-tool-retirement.bak"))));
        store.Save(store.Load()!);
        Assert.Single(Directory.GetFiles(context.TempDirectory, "*.pre-tool-retirement.bak"));
    }

    [Fact]
    public void 仅管理项可迁移为空且其他未知工具不会被吞掉()
    {
        var snapshot = new DockLayoutSnapshotV2 { Tools = [new() { Id = RetiredHostToolIds.ToolManagement, DockId = "tools", Order = 0 }] };
        Assert.Empty(RetiredToolLayoutMigration.Apply(snapshot).Tools);
        var unknown = new DockToolSnapshotV2 { Id = "myavalonia.plugin.unknown.tool.main", DockId = "tools", Order = 1 };
        snapshot.Tools.Add(unknown);
        Assert.Equal(unknown.Id, Assert.Single(RetiredToolLayoutMigration.Apply(snapshot).Tools).Id);
    }
}
