using Avalonia.Controls;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.PluginStatus;
using MyAvaloniaManagement.Models.Plugins;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels.PluginStatus;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证生产查询的事实合并，以及窗口模型的筛选、刷新和复制边界。</summary>
/// <remarks>使用真实 Registry/StateStore/诊断会话，工厂故意抛错，确保查看状态不会执行插件。</remarks>
public sealed class PluginStatusTests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.status-test");

    [Fact]
    public void 查询展示声明而不创建插件并读取真实可用性门控()
    {
        var registry = CreateRegistry();
        var states = new PluginLifecycleStateStore(registry);
        var query = new PluginStatusQuery(registry, new PluginAvailabilityReadModel(states));
        var available = Assert.Single(query.Capture());
        Assert.True(available.IsAvailable);
        Assert.False(available.HasProblem);
        Assert.Equal(3, available.Contributions.Count);
        Assert.Equal(["命令", "工具", "文档"], available.Contributions.Select(item => item.Kind).Order(StringComparer.Ordinal));
        Assert.All(available.Contributions, item => Assert.Contains("已声明", item.AvailabilityText));
        states.BeginShutdown();
        var withdrawn = Assert.Single(query.Capture());
        Assert.False(withdrawn.IsAvailable);
        Assert.Equal("当前不可用", withdrawn.AvailabilityText);
        Assert.All(withdrawn.Contributions, item => Assert.Contains("当前不可用", item.AvailabilityText));
        Assert.True(available.IsAvailable); // 历史快照不会被后续刷新反向改写。
    }

    [Theory]
    [InlineData(nameof(PluginLifecycleStatus.NotStarted), false, false)]
    [InlineData(nameof(PluginLifecycleStatus.Initializing), false, false)]
    [InlineData(nameof(PluginLifecycleStatus.Ready), true, false)]
    [InlineData(nameof(PluginLifecycleStatus.InitializationFailed), false, true)]
    [InlineData(nameof(PluginLifecycleStatus.InitializationTimedOut), false, true)]
    [InlineData(nameof(PluginLifecycleStatus.HostCancelled), false, true)]
    [InlineData(nameof(PluginLifecycleStatus.Stopping), false, false)]
    [InlineData(nameof(PluginLifecycleStatus.Stopped), false, false)]
    [InlineData(nameof(PluginLifecycleStatus.ShutdownFailed), false, true)]
    [InlineData(nameof(PluginLifecycleStatus.ShutdownTimedOut), false, true)]
    public void 所有生命周期阶段产生正确的筛选事实(string statusName, bool available, bool problem)
    {
        var status = Enum.Parse<PluginLifecycleStatus>(statusName);
        var registry = CreateRegistry(lifecycle: true);
        var states = new PluginLifecycleStateStore(registry);
        states.SetState(new(Owner, status) { Duration = TimeSpan.FromMilliseconds(12), ErrorCode = "TEST_FAILURE" });
        var item = Assert.Single(new PluginStatusQuery(registry, new PluginAvailabilityReadModel(states)).Capture());
        Assert.Equal(available, item.IsAvailable);
        Assert.Equal(problem, item.HasProblem);
        Assert.Equal("12 ms", item.DurationText);
    }

    [Fact]
    public void 同一候选早期目录和后期身份诊断合并且复制内容保持脱敏()
    {
        using var context = new TestHostContext();
        using var diagnostics = HostDiagnosticSession.Start(context.TempDirectory);
        diagnostics.Report(new(HostDiagnosticCodes.PluginEntryInvalid, HostDiagnosticPhase.PluginRootDiscovery)
            { PluginDirectory = "BrokenPlugin", Exception = new Exception("secret-canary-123") });
        diagnostics.Report(new(HostDiagnosticCodes.PluginServiceRegistrationFailed, HostDiagnosticPhase.PluginServiceRegistration)
            { PluginDirectory = "brokenplugin", PluginId = Owner });
        var empty = new PluginRegistry([], []);
        var query = new PluginStatusQuery(empty, new PluginAvailabilityReadModel(new PluginLifecycleStateStore(empty)), diagnostics);
        var item = Assert.Single(query.Capture());
        Assert.Equal(Owner.Value, item.PluginId);
        Assert.Equal("plugin:" + Owner.Value, item.Key);
        Assert.True(item.HasProblem);
        Assert.False(item.IsAvailable);
        Assert.Equal(2, item.Diagnostics.Count);
        Assert.Equal(["目录发现", "服务注册"], item.Diagnostics.Select(record => record.PhaseText));
        var model = new PluginStatusWindowViewModel(query, TimeProvider.System);
        model.Refresh();
        Assert.Contains(HostDiagnosticCodes.PluginEntryInvalid, model.CreateDiagnosticText());
        Assert.DoesNotContain("secret-canary-123", model.CreateDiagnosticText());
    }

    [Fact]
    public void 无身份候选保留目录并按异常优先展示且注册插件诊断不重复计数()
    {
        using var context = new TestHostContext();
        using var diagnostics = HostDiagnosticSession.Start(context.TempDirectory);
        diagnostics.Report(new(HostDiagnosticCodes.PluginEntryInvalid, HostDiagnosticPhase.PluginRootDiscovery)
            { PluginDirectory = "ZBroken" });
        var registry = CreateRegistry();
        var query = new PluginStatusQuery(registry, new PluginAvailabilityReadModel(new PluginLifecycleStateStore(registry)), diagnostics);
        Assert.Equal("目录：ZBroken", query.Capture()[0].PluginId);
        diagnostics.Report(new(HostDiagnosticCodes.LifecycleInitializeFailed, HostDiagnosticPhase.PluginLifecycle) { PluginId = Owner });
        var items = query.Capture();
        Assert.Equal(2, items.Count);
        var registered = Assert.Single(items, item => item.PluginId == Owner.Value);
        Assert.True(registered.IsAvailable);
        Assert.True(registered.HasProblem);
        Assert.Single(registered.Diagnostics);
    }

    [Theory]
    [InlineData(nameof(HostDiagnosticPhase.PluginModuleDiscovery), HostDiagnosticCodes.PluginModuleActivationFailed)]
    [InlineData(nameof(HostDiagnosticPhase.PluginServiceRegistration), HostDiagnosticCodes.PluginServiceRegistrationFailed)]
    [InlineData(nameof(HostDiagnosticPhase.HostContainerBuild), HostDiagnosticCodes.PluginContainerBuildFailed)]
    public void 尚未提交注册表的插件仍可从后期启动失败诊断找到(string phaseName, string code)
    {
        var phase = Enum.Parse<HostDiagnosticPhase>(phaseName);
        using var context = new TestHostContext();
        using var diagnostics = HostDiagnosticSession.Start(context.TempDirectory);
        diagnostics.Report(new(code, phase) { PluginId = Owner });
        var registry = new PluginRegistry([], []);
        var item = Assert.Single(new PluginStatusQuery(registry, new PluginAvailabilityReadModel(new PluginLifecycleStateStore(registry)), diagnostics).Capture());
        Assert.Equal(Owner.Value, item.PluginId);
        Assert.True(item.HasProblem);
        Assert.Empty(item.Contributions);
    }

    [Fact]
    public void 刷新搜索与选择保持且错误恢复不丢失旧快照()
    {
        var first = Item("a", true, false);
        var second = Item("b", false, true) with { Diagnostics = [new("现在", "错误", "加载", "LOAD_CODE", "错误说明", "")] };
        var query = new MutableQuery { Items = [first, second] };
        var model = new PluginStatusWindowViewModel(query, TimeProvider.System);
        model.Refresh();
        model.SelectedItem = second;
        model.SearchText = "  load_code  ";
        model.SelectedFilter = "异常";
        Assert.Equal("b", Assert.Single(model.VisibleItems).Key);
        query.Items = [first, second with { VersionText = "2.0.0" }];
        model.Refresh();
        Assert.Equal("b", model.SelectedItem!.Key);
        Assert.Equal("2.0.0", model.SelectedItem.VersionText);
        Assert.Equal("  load_code  ", model.SearchText);
        Assert.Equal(2, model.TotalCount);
        Assert.Equal(1, model.AvailableCount);
        Assert.Equal(1, model.ProblemCount);
        var timestamp = model.UpdatedText;
        query.Fail = true;
        model.Refresh();
        Assert.True(model.HasError);
        Assert.Equal(timestamp, model.UpdatedText);
        Assert.Equal("b", model.SelectedItem.Key);
        query.Fail = false;
        query.Items = [];
        model.Refresh();
        Assert.False(model.HasError);
        Assert.True(model.HasNoResults);
        Assert.False(model.HasSelection);
        Assert.Empty(model.CreateDiagnosticText());
        Assert.Equal("本次会话未发现插件。", model.EmptyText);
    }

    [Fact]
    public void 空结果有明确说明且可用筛选支持有警告的插件()
    {
        var query = new MutableQuery { Items = [Item("a", true, true), Item("b", false, true), Item("c", false, false)] };
        var model = new PluginStatusWindowViewModel(query, TimeProvider.System);
        model.Refresh();
        model.SelectedFilter = "可用";
        Assert.Equal("a", Assert.Single(model.VisibleItems).Key);
        model.SearchText = "missing";
        Assert.True(model.HasNoResults);
        Assert.Contains("没有匹配", model.EmptyText);
        model.SearchText = "";
        model.SelectedFilter = "异常";
        Assert.Equal(2, model.VisibleItems.Count);
    }

    private static PluginStatusItem Item(string key, bool available, bool problem) =>
        new(key, "assembly", "状态", "—", "可用性", "详情") { IsAvailable = available, HasProblem = problem };

    private static PluginRegistry CreateRegistry(bool lifecycle = false)
    {
        var manifest = new PluginManifest(2, Owner, new Version(1, 0, 0), new("Test.dll", "Test.Module"),
            new(new Version(3, 0, 0), new Version(4, 0, 0)));
        var documentId = new DocumentTypeId(Owner.Value + ".document.sample");
        return new PluginRegistry(
            [new(manifest, typeof(PluginStatusTests).Assembly, typeof(PluginStatusTests), [], [], [], [])],
            [new(Owner, new(documentId, "文档", "说明", "测试"), typeof(object), typeof(UserControl),
                () => throw new InvalidOperationException("查询不能创建 View"), false)],
            [new(Owner, new(new(Owner.Value + ".tool.sample"), "工具", "说明", ToolDockSide.Right, ToolCloseBehavior.Hide),
                typeof(object), typeof(UserControl), () => throw new InvalidOperationException("查询不能创建 Tool"))],
            lifecycle ? [new(Owner, typeof(PluginStatusTests))] : [],
            workbenchCommands: [new(Owner, new(new(Owner.Value + ".command.sample"), "命令", "说明"), documentId)]);
    }

    private sealed class MutableQuery : IPluginStatusQuery
    {
        public IReadOnlyList<PluginStatusItem> Items { get; set; } = [];
        public bool Fail { get; set; }
        public IReadOnlyList<PluginStatusItem> Capture() => Fail ? throw new InvalidOperationException("不能进入界面的异常正文") : Items;
    }
}
