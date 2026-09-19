using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Enablement;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.ViewModels.PluginStatus;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>真实窗口、查询、持久化与绑定共同验证开关；没有热加载替身来掩盖生产调用路径。</summary>
public sealed class PluginEnablementUiTests
{
    [AvaloniaFact]
    public async Task 看板开关保存并改回取消待重启且重开保留选择()
    {
        using var fixture = new Fixture();
        fixture.Service.ShowOrActivate();
        await Flush();
        var window = fixture.Service.CurrentWindow!;
        var model = (PluginStatusWindowViewModel)window.DataContext!;
        var toggle = window.FindControl<ToggleSwitch>("PluginEnablementSwitch")!;
        Assert.False(toggle.IsChecked);
        Assert.True(toggle.IsEnabled);
        Click(window, toggle);
        await model.ToggleEnablementCommand.ExecutionTask!;
        await Flush();
        Assert.True(toggle.IsChecked);
        Assert.True(model.SelectedItem!.RequiresRestart);
        Assert.True(model.SelectedItem.IsDisabled);
        Assert.Contains("已保存", model.EnablementFeedback);
        Assert.True(fixture.Store.Load().Settings!.IsEnabled(Fixture.Id));
        model.SelectedFilter = "待重启";
        Assert.Single(model.VisibleItems);
        Assert.True(toggle.Focus());
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        await model.ToggleEnablementCommand.ExecutionTask!;
        await Flush();
        Assert.Empty(model.VisibleItems);
        model.SelectedFilter = "全部";
        window.Close();
        fixture.Service.ShowOrActivate();
        await Flush();
        window = fixture.Service.CurrentWindow!;
        model = (PluginStatusWindowViewModel)window.DataContext!;
        Assert.True(model.SelectedItem!.IsDisabled);
        Assert.False(model.SelectedItem.RequiresRestart);
        Assert.False(window.FindControl<ToggleSwitch>("PluginEnablementSwitch")!.IsChecked);
        fixture.Context.Workspace.BeginShutdown();
        Assert.False(model.CanChangeEnablement);
        Assert.False(window.FindControl<ToggleSwitch>("PluginEnablementSwitch")!.IsEnabled);
    }

    [AvaloniaFact]
    public async Task 外部写冲突恢复开关且重新读取后可操作并验证窄窗口主题()
    {
        using var fixture = new Fixture();
        fixture.Service.ShowOrActivate();
        await Flush();
        var window = fixture.Service.CurrentWindow!;
        var model = (PluginStatusWindowViewModel)window.DataContext!;
        var toggle = window.FindControl<ToggleSwitch>("PluginEnablementSwitch")!;
        Assert.True(fixture.Store.TrySave(fixture.Store.Load(), new([])).Success);
        Click(window, toggle);
        await model.ToggleEnablementCommand.ExecutionTask!;
        await Flush();
        Assert.False(toggle.IsChecked);
        Assert.Contains("其他实例", model.EnablementFeedback);
        await model.ReloadEnablementAsync();
        await Flush();
        Assert.True(toggle.IsChecked);
        Assert.True(model.SelectedItem!.RequiresRestart);
        window.Width = 680; window.Height = 600;
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            window.RequestedThemeVariant = theme;
            await Flush();
            Assert.InRange(toggle.DesiredSize.Width, 1, toggle.Bounds.Width + 0.5);
            Assert.True(toggle.IsEffectivelyVisible);
            var output = Path.Combine(AppContext.BaseDirectory, "TestResults", "v13-ui");
            Directory.CreateDirectory(output);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(output, theme == ThemeVariant.Light ? "enablement-light.png" : "enablement-dark.png"), PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public async Task 保存期间禁用重复操作且关窗不撤销已接受提交()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(inner => new DelayedActions(inner, entered, release));
        fixture.Service.ShowOrActivate();
        var model = (PluginStatusWindowViewModel)fixture.Service.CurrentWindow!.DataContext!;
        var saving = model.ToggleEnablementAsync();
        await entered.Task;
        Assert.False(model.CanChangeEnablement);
        Assert.False(model.CanReloadEnablement);
        await model.ToggleEnablementAsync();
        fixture.Service.CurrentWindow.Close();
        release.SetResult();
        await saving;
        Assert.True(fixture.Store.Load().Settings!.IsEnabled(Fixture.Id));
        fixture.Service.ShowOrActivate();
        var reopened = (PluginStatusWindowViewModel)fixture.Service.CurrentWindow!.DataContext!;
        Assert.True(reopened.SelectedItem!.NextStartupEnabled);
        Assert.True(reopened.SelectedItem.RequiresRestart);
    }

    [AvaloniaFact]
    public async Task 配置错误时管理窗口可打开且开关只读()
    {
        using var fixture = new Fixture(invalid: true);
        fixture.Service.ShowOrActivate();
        await Flush();
        var model = (PluginStatusWindowViewModel)fixture.Service.CurrentWindow!.DataContext!;
        Assert.True(model.HasEnablementNotice);
        Assert.Contains("配置损坏", model.EnablementNotice);
        Assert.False(model.CanChangeEnablement);
        Assert.False(model.SelectedItem!.IsDisabled);
        Assert.Null(model.SelectedNextStartupEnabled);
    }

    private static void Click(Window window, Control control)
    {
        window.UpdateLayout();
        Assert.True(control.Bounds.Width > 0);
        // ToggleSwitch 的 Content 位于轨道上方；点击轨道而非两行之间的空白。
        var point = control.TranslatePoint(new Point(20, control.Bounds.Height - 12), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static async Task Flush()
    { await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }

    private sealed class DelayedActions(IPluginEnablementActions inner, TaskCompletionSource entered, TaskCompletionSource release) : IPluginEnablementActions
    {
        public PluginEnablementReadResult Current => inner.Current;
        public async Task<PluginEnablementSaveResult> SetEnabledAsync(PluginId id, bool enabled)
        { entered.SetResult(); await release.Task; return await inner.SetEnabledAsync(id, enabled); }
        public Task ReloadAsync() => inner.ReloadAsync();
    }

    private sealed class Fixture : IDisposable
    {
        internal static readonly PluginId Id = new("myavalonia.plugin.ui-enablement");
        private readonly string _root = Path.Combine(Path.GetTempPath(), "enablement-ui", Guid.NewGuid().ToString("N"));
        internal PluginEnablementSettingsStore Store { get; }
        internal UiTestContext Context { get; }
        internal PluginStatusWindowService Service { get; }
        private readonly Window _owner = new();
        internal Fixture(Func<IPluginEnablementActions, IPluginEnablementActions>? wrap = null, bool invalid = false)
        {
            Directory.CreateDirectory(_root);
            Store = new(Path.Combine(_root, PluginEnablementSettingsStore.FileName));
            if (invalid) File.WriteAllText(Store.SettingsPath, "{}");
            else Assert.True(Store.TrySave(Store.Load(), new([Id])).Success);
            var startup = Store.Load();
            IPluginEnablementActions actions = new PluginEnablementService(Store, startup, [Id]);
            if (wrap is not null) actions = wrap(actions);
            var manifest = new PluginManifest(2, Id, new(1, 0, 0, 0), new("Plugin.dll", "Plugin.Module"), new(new(3, 0, 0, 0), new(4, 0, 0, 0)));
            var snapshot = new PluginDiscoverySnapshot([], new Dictionary<System.Reflection.Assembly, PluginManifest>(),
                new Dictionary<System.Reflection.Assembly, Type>(), [], [new(_root, manifest)], startup);
            Context = new UiTestContext((services, _) =>
            {
                services.AddSingleton(snapshot);
                services.AddSingleton<IPluginEnablementState>(actions);
                services.AddSingleton(actions);
            });
            Service = Context.Provider.GetRequiredService<PluginStatusWindowService>();
            _owner.Show(); Service.Attach(_owner);
        }
        public void Dispose()
        { Service.Dispose(); _owner.Close(); Context.Dispose(); Directory.Delete(_root, true); }
    }
}
