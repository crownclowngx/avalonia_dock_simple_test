using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;

namespace MyAvaloniaManagement.ViewModels.Bindings;

/// <summary>主窗口 XAML 实际消费的最小绑定端口。</summary>
/// <remarks>
/// 设计意图：View 不需要知道文档事务、插件菜单或布局存储等实现依赖。生产 ViewModel 与
/// 纯内存设计数据都实现这份内部端口，使编译绑定可以保留，同时设计器不必构造生产对象图。
/// 该接口仅服务 Host XAML，不属于 Plugin SDK。
/// </remarks>
internal interface IMainWindowViewBindings
{
    IRootDock? Layout { get; }

    string DocumentOperationError { get; }

    bool HasDocumentOperationError { get; }

    bool IsSystemTheme { get; }

    bool IsLightTheme { get; }

    bool IsDarkTheme { get; }

    IAsyncRelayCommand OpenDocumentCommand { get; }

    IAsyncRelayCommand SaveDocumentCommand { get; }

    IRelayCommand DismissDocumentOperationErrorCommand { get; }

    IRelayCommand<string?> SetThemeCommand { get; }
}
