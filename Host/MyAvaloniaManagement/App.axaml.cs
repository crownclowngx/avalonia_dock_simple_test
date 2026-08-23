using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Presentation;

namespace MyAvaloniaManagement;

/// <summary>
/// Avalonia 应用入口，负责加载生产资源并转交桌面生命周期。
/// </summary>
internal sealed partial class App : Application
{
    private readonly IHostDesktopShell _desktopShell;
    private readonly ViewLocator _viewLocator;
    private readonly DocumentControlRecycling _documentControlRecycling;

    /// <summary>取得当前 App 所属 Host 容器的回收器，供所有权边界校验。</summary>
    internal DocumentControlRecycling ControlRecycling => _documentControlRecycling;

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
        AvaloniaXamlLoader.Load(this);
        // XAML 只声明 DynamicResource 契约，不自行 new 回收器。在资源加载完成后
        // 安装当前 DI 容器的唯一实例，使 App、Style 和关闭链共享明确所有权。
        Resources[DocumentControlRecycling.ResourceKey] = _documentControlRecycling;
        // XAML 不再通过无参构造创建静态 Locator。把 Runtime 独占实例安装到应用级模板集合，
        // 可确保并行测试或未来多 Runtime 场景不会共享插件 View 映射。
        DataTemplates.Add(_viewLocator);
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
            _desktopShell.Attach(this, desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
