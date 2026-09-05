using DaTangAccountingHelpPlug.Plugin;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagement.Business.Workspace;
using MyPlugTest.Plugin;

namespace MyAvaloniaManagement.PluginTests;

public sealed class PluginCompatibilityTests
{
    [Fact]
    public void LayoutV2不再公开历史ToolId归一化入口()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddViewModels();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        _ = provider.GetRequiredService<WorkspaceSession>();

        Assert.Null(typeof(WorkspaceSession).GetMethod(
            "NormalizePersistedToolId",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic));
    }

    [Fact]
    public void 内置插件与夹具入口只实现最终V2模块契约()
    {
        Assert.DoesNotContain(
            typeof(IPluginModule).GetProperties(),
            property => property.Name == "PluginId");
        Assert.DoesNotContain(
            typeof(IPluginLifecycle).GetProperties(),
            property => property.Name == "PluginId");
        Assert.Equal(
            ["Configure"],
            typeof(IPluginModule).GetMethods().Select(method => method.Name));
        Assert.True(typeof(IPluginModule).IsAssignableFrom(typeof(LifecycleProbeModule)));
        Assert.True(typeof(IPluginModule).IsAssignableFrom(typeof(DaTangAccountingHelpPluginModule)));
        Assert.True(typeof(IPluginModule).IsAssignableFrom(typeof(MyPlugTestPluginModule)));
        Assert.True(typeof(IPluginModule).IsAssignableFrom(typeof(IndependentProbeModule)));
    }

    [Fact]
    public void 插件状态模型投影内置插件与夹具与G5生命周期声明()
    {
        var registry = new PluginRegistry(
            CreatePluginSnapshots(), [], [],
            [new PluginLifecycleDeclaration(
                new MyAvaloniaManagement.PluginSdk.PluginId(
                    "myavalonia.plugin.lifecycle-probe"),
                typeof(ReadyProbeLifecycle))]);
        var states = new PluginLifecycleStateStore(registry);
        states.SetState(new PluginLifecycleState(
            new MyAvaloniaManagement.PluginSdk.PluginId(
                "myavalonia.plugin.lifecycle-probe"),
            PluginLifecycleStatus.Ready));
        var viewModel = new PluginStatusViewModel(
            registry,
            availability: new PluginAvailabilityReadModel(states));

        Assert.Equal(4, viewModel.Items.Count);
        Assert.Equal(
            [
                "myavalonia.plugin.datang-accounting-help",
                "myavalonia.plugin.lifecycle-probe",
                "myavalonia.plugin.my-plug-test",
                "myavalonia.plugin.probe"
            ],
            viewModel.Items.Select(item => item.PluginId));
        Assert.Equal(
            "生命周期初始化成功",
            viewModel.Items.Single(item =>
                item.PluginId == "myavalonia.plugin.lifecycle-probe").StatusText);
        Assert.All(
            viewModel.Items.Where(item =>
                item.PluginId != "myavalonia.plugin.lifecycle-probe"),
            item => Assert.Contains("无需后台生命周期", item.StatusText));
    }

    [Theory]
    [InlineData("NotStarted", "等待生命周期初始化", "尚不可用")]
    [InlineData("Initializing", "正在初始化", "尚不可用")]
    [InlineData("InitializationFailed", "生命周期初始化失败", "已隔离")]
    [InlineData("InitializationTimedOut", "生命周期初始化超时", "已隔离")]
    [InlineData("HostCancelled", "生命周期被宿主取消", "已隔离")]
    [InlineData("Stopping", "正在停止", "正在退出")]
    [InlineData("Stopped", "生命周期已停止", "已停止")]
    [InlineData("ShutdownFailed", "生命周期停止失败", "正在退出")]
    [InlineData("ShutdownTimedOut", "生命周期停止超时", "正在退出")]
    public void 插件状态Tool投影初始化隔离与停止状态(
        string statusName,
        string expectedStatus,
        string expectedAvailability)
    {
        var status = Enum.Parse<PluginLifecycleStatus>(statusName);
        var owner = new MyAvaloniaManagement.PluginSdk.PluginId(
            "myavalonia.plugin.lifecycle-probe");
        var registry = new PluginRegistry(
            CreatePluginSnapshots(), [], [],
            [new PluginLifecycleDeclaration(owner, typeof(ReadyProbeLifecycle))]);
        var states = new PluginLifecycleStateStore(registry);
        states.SetState(new PluginLifecycleState(owner, status)
        {
            Stage = status is PluginLifecycleStatus.Stopping
                or PluginLifecycleStatus.Stopped
                or PluginLifecycleStatus.ShutdownFailed
                or PluginLifecycleStatus.ShutdownTimedOut
                ? PluginLifecycleStage.Shutdown
                : PluginLifecycleStage.Initialization,
            ErrorCode = status switch
            {
                PluginLifecycleStatus.InitializationFailed =>
                    HostDiagnosticCodes.LifecycleInitializeFailed,
                PluginLifecycleStatus.InitializationTimedOut =>
                    HostDiagnosticCodes.LifecycleInitializeTimeout,
                PluginLifecycleStatus.HostCancelled =>
                    HostDiagnosticCodes.LifecycleHostCancelled,
                PluginLifecycleStatus.ShutdownFailed =>
                    HostDiagnosticCodes.LifecycleShutdownFailed,
                PluginLifecycleStatus.ShutdownTimedOut =>
                    HostDiagnosticCodes.LifecycleShutdownTimeout,
                _ => null,
            },
        });

        var item = new PluginStatusViewModel(
            registry,
            availability: new PluginAvailabilityReadModel(states)).Items
            .Single(value => value.PluginId == owner.Value);

        Assert.Equal(expectedStatus, item.StatusText);
        Assert.Equal(expectedAvailability, item.AvailabilityText);
    }

    [Fact]
    public void 未注册生命周期的托管插件无需运行状态即可用()
    {
        var registry = new PluginRegistry(CreatePluginSnapshots(), [], [], []);
        var states = new PluginLifecycleStateStore(registry);
        var availability = new PluginAvailabilityReadModel(states);

        Assert.Empty(availability.LifecycleStates);
        Assert.All(
            CreatePluginSnapshots(),
            plugin => Assert.True(availability.IsAvailable(
                new MyAvaloniaManagement.PluginSdk.PluginId(
                    plugin.Manifest.PluginId.Value))));
    }

    private sealed class IndependentProbeModule : IPluginModule
    {
        public void Configure(IPluginRegistration registration) { }
    }

    private sealed class LifecycleProbeModule : IPluginModule
    {
        public void Configure(IPluginRegistration registration) => registration.UseLifecycle<ReadyProbeLifecycle>();
    }

    private sealed class ReadyProbeLifecycle : IPluginLifecycle
    {
        public Task InitializeAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ShutdownAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static IReadOnlyList<PluginRegistryPlugin> CreatePluginSnapshots() =>
    [
        Snapshot<LifecycleProbeModule>("myavalonia.plugin.lifecycle-probe"),
        Snapshot<DaTangAccountingHelpPluginModule>("myavalonia.plugin.datang-accounting-help"),
        Snapshot<MyPlugTestPluginModule>("myavalonia.plugin.my-plug-test"),
        Snapshot<IndependentProbeModule>("myavalonia.plugin.probe"),
    ];

    private static PluginRegistryPlugin Snapshot<TModule>(string pluginId)
    {
        var assembly = typeof(TModule).Assembly;
        var manifest = new PluginManifest(
            PluginManifestReader.CurrentSchemaVersion,
            new PluginId(pluginId),
            new Version(1, 0, 0, 0),
            new PluginEntryPoint(
                $"{assembly.GetName().Name}.dll",
                typeof(TModule).FullName!),
            new PluginVersionRange(new Version(1, 0, 0, 0), new Version(2, 0, 0, 0)));
        return new PluginRegistryPlugin(
            manifest, assembly, typeof(TModule), [], [], [], []);
    }

}
