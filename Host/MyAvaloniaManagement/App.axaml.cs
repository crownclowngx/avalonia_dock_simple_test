using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Startup;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Presentation;

namespace MyAvaloniaManagement;

/// <summary>
/// Avalonia 应用入口，负责加载生产资源并转交桌面生命周期。
/// </summary>
internal sealed partial class App : Application
{
    private readonly IHostDesktopShell? _desktopShell;
    private readonly ViewLocator? _viewLocator;
    private DocumentControlRecycling? _documentControlRecycling;
    private readonly StartupDesktopShell? _startupShell;
    private bool _workbenchResourcesInstalled;
    internal Task StartupCompletion { get; private set; } = Task.CompletedTask;
    /// <summary>进程创建到首帧/工作台就绪的诊断时间，仅作观测，不参与成功判定或人为延时。</summary>
    internal System.TimeSpan? StartupFirstFrameElapsed { get; set; }
    internal System.TimeSpan? StartupReadyElapsed { get; set; }

    /// <summary>生产入口只注入轻量启动依赖；完整工作台尚未组合，不提前解析业务容器。</summary>
    internal App(StartupCoordinator startup, HostDiagnosticSession diagnostics)
    {
        _startupShell = new StartupDesktopShell(startup, diagnostics);
    }

    /// <summary>取得当前 App 所属 Host 容器的回收器，供所有权边界校验。</summary>
    internal DocumentControlRecycling ControlRecycling => _documentControlRecycling ??
        throw new System.InvalidOperationException("工作台资源尚未安装。");

    /// <summary>使用明确的桌面生命周期策略创建应用。</summary>
    /// <remarks>
    /// App 不允许无参生产构造。设计器和 Headless 测试若只需资源，可注入自己的内部 Shell；
    /// 这样不会为了框架入口重新引入全局 Service Locator。
    /// </remarks>
    internal App(
        IHostDesktopShell desktopShell,
        ViewLocator viewLocator,
        DocumentControlRecycling documentControlRecycling)
    {
        _desktopShell = desktopShell ??
            throw new System.ArgumentNullException(nameof(desktopShell));
        _viewLocator = viewLocator ??
            throw new System.ArgumentNullException(nameof(viewLocator));
        _documentControlRecycling = documentControlRecycling ??
            throw new System.ArgumentNullException(nameof(documentControlRecycling));
    }

    /// <summary>
    /// 加载 App.axaml 中声明的主题和全局资源。
    /// </summary>
    public override void Initialize()
    {
        if (_startupShell is not null)
        {
            // 首屏只有基础主题和错误界面所需颜色，Dock 与插件 ViewLocator 延迟安装。
            InstallMinimalResources();
            return;
        }
        InstallWorkbenchResources(_viewLocator!, ControlRecycling);
    }

    /// <summary>在同一 Application 中安装一次完整工作台资源，保留容器唯一实例和明确所有权。</summary>
    internal void InstallWorkbenchResources(ViewLocator viewLocator, DocumentControlRecycling recycling)
    {
        if (_workbenchResourcesInstalled) throw new System.InvalidOperationException("工作台资源不能重复安装。");
        _workbenchResourcesInstalled = true;
        _documentControlRecycling = recycling;
        Styles.Clear();
        Resources.Clear();
        AvaloniaXamlLoader.Load(this);
        // XAML 只声明 DynamicResource 契约，不自行 new 回收器。在资源加载完成后
        // 安装当前 DI 容器的唯一实例，使 App、Style 和关闭链共享明确所有权。
        Resources[DocumentControlRecycling.ResourceKey] = _documentControlRecycling;
        // XAML 不再通过无参构造创建静态 Locator。把 Runtime 独占实例安装到应用级模板集合，
        // 可确保并行测试或未来多 Runtime 场景不会共享插件 View 映射。
        DataTemplates.Add(viewLocator);
    }

    /// <summary>
    /// 首屏和启动失败共用最小资源。完整 XAML 装配若中途失败，也不让错误窗口依赖半安装的
    /// Dock/插件模板；调用前先关闭尚未交接的业务窗口，避免清理顺序反过来触发业务模板重建。
    /// </summary>
    internal void InstallMinimalResources()
    {
        Styles.Clear();
        DataTemplates.Clear();
        Resources.Clear();
        Styles.Add(new FluentTheme());
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        Resources["AppSecondaryTextBrush"] = new SolidColorBrush(Color.Parse(dark ? "#B8B8B8" : "#666666"));
        Resources["AppWarningPanelBrush"] = new SolidColorBrush(Color.Parse(dark ? "#3A2A16" : "#FFF4CE"));
        Resources["AppWarningBorderBrush"] = new SolidColorBrush(Color.Parse(dark ? "#FFD89B3C" : "#C28A00"));
    }
    /// <summary>
    /// 在 Avalonia 框架初始化完成后把经典桌面生命周期交给注入的 Shell。
    /// </summary>
    /// <remarks>
    /// App 不解释生产窗口或 Smoke 政策；这些行为由 Shell 集中拥有。非桌面生命周期只加载
    /// 资源，不会意外创建窗口。
    /// </remarks>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (_startupShell is not null) StartupCompletion = _startupShell.Attach(this, desktop);
            else _desktopShell!.Attach(this, desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
