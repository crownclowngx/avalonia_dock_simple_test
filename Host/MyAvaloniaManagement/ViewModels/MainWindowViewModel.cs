using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.ViewModels.Bindings;

namespace MyAvaloniaManagement.ViewModels;

/// <summary>
/// 负责主窗口绑定状态、命令和定向协调，并把 Dock 布局及文档持久化委托给内部服务。
/// 该边界让 ViewModel 保持 UI 协调职责，不直接承担文件事务和 Dock 树遍历。
/// </summary>
internal sealed partial class MainWindowViewModel : ObservableObject, IDropTarget, IMainWindowViewBindings, IDisposable
{
    private readonly ManagementFactory _factory;
    private readonly PluginMenuService _pluginMenuService;
    private readonly DockLayoutLifecycle _layoutLifecycle;
    private readonly ApplicationThemeService _themeService;
    private readonly DocumentPersistenceCoordinator _documents;
    private readonly DocumentOperationState _documentOperationState;
    private ApplicationThemeMode _themeMode;
    private IRootDock? _layout;

    public string DocumentOperationError => _documentOperationState.Error;

    public bool HasDocumentOperationError => _documentOperationState.HasError;

    public IRootDock? Layout
    {
        get => _layout;
        set => SetProperty(ref _layout, value);
    }

    public bool IsSystemTheme => _themeMode == ApplicationThemeMode.System;

    public bool IsLightTheme => _themeMode == ApplicationThemeMode.Light;

    public bool IsDarkTheme => _themeMode == ApplicationThemeMode.Dark;

    internal MainWindowViewModel(
        ManagementFactory factory,
        PluginMenuService pluginMenuService,
        DockLayoutLifecycle layoutLifecycle,
        ApplicationThemeService themeService,
        DocumentPersistenceCoordinator documents,
        DocumentOperationState documentOperationState,
        DocumentCloseCoordinator documentCloseCoordinator)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _pluginMenuService = pluginMenuService ??
            throw new ArgumentNullException(nameof(pluginMenuService));
        _layoutLifecycle = layoutLifecycle ??
            throw new ArgumentNullException(nameof(layoutLifecycle));
        _themeService = themeService ??
            throw new ArgumentNullException(nameof(themeService));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _documentOperationState = documentOperationState ??
            throw new ArgumentNullException(nameof(documentOperationState));
        _documentCloseCoordinator = documentCloseCoordinator;
        _themeMode = _themeService.CurrentMode;

        // Factory 和文档状态都由根容器持有，而主窗口是瞬态对象。先登记定向通知，
        // Dispose 时再成对解除，避免单例服务通过委托延长窗口生命周期。
        _factory.LayoutChanged += OnLayoutChanged;
        _documentOperationState.Changed += OnDocumentOperationStateChanged;
        try
        {
            Layout = _layoutLifecycle.Prepare(_factory);
        }
        catch
        {
            // 布局准备失败时构造函数不会返回，DI 容器也就没有对象可供释放。
            // 必须在这里主动撤销已登记的委托，避免单例状态持有一个半构造窗口。
            ReleaseCoordinationSubscriptions();
            throw;
        }
    }

    private readonly DocumentCloseCoordinator _documentCloseCoordinator;

    internal void ApplyPendingLayout()
    {
        if (Layout is not { } current)
        {
            return;
        }

        var applied = _layoutLifecycle.ApplyPending(current, _factory);
        if (!ReferenceEquals(applied, current))
        {
            Layout = applied;
        }
    }

    internal void SaveLayout()
    {
        if (Layout is { } root)
        {
            _layoutLifecycle.Save(root, _factory);
        }
    }

    /// <summary>
    /// 在主窗口真正退出前汇总处理全部脏 Document。
    /// </summary>
    internal Task<bool> ConfirmWindowCloseAsync()
    {
        var documents = DocumentWorkspace.GetDocuments(Layout);
        return _documentCloseCoordinator.ConfirmWindowCloseAsync(documents);
    }

    /// <summary>
    /// 同步判断窗口关闭是否需要进入异步确认。干净窗口保持 Avalonia 原生的一次关闭路径，
    /// 避免无意义地取消后重入，也让布局保存和自动化退出保持同步可观察。
    /// </summary>
    internal bool HasDirtyDocuments() =>
        DocumentWorkspace.GetDocuments(Layout)
            .Any(_documentCloseCoordinator.IsDirty);

    /// <summary>
    /// 解除当前瞬态窗口对根级协调对象的定向通知。
    /// </summary>
    public void Dispose()
    {
        ReleaseCoordinationSubscriptions();
    }

    private void ReleaseCoordinationSubscriptions()
    {
        // .NET 事件解除不存在匹配委托时是安全的，因此该入口天然支持重复 Dispose。
        _factory.LayoutChanged -= OnLayoutChanged;
        _documentOperationState.Changed -= OnDocumentOperationStateChanged;
    }

    private void OnLayoutChanged(object? sender, EventArgs args) =>
        OnPropertyChanged(nameof(Layout));

    private void OnDocumentOperationStateChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(DocumentOperationError));
        OnPropertyChanged(nameof(HasDocumentOperationError));
    }

    [RelayCommand]
    public async Task CreateDocument(string documentType)
    {
        _documentOperationState.Apply(await _documents.CreateDocumentAsync(
            DocumentTypeId.Parse(documentType)));
    }

    [RelayCommand]
    public async Task OpenDocument()
    {
        _documentOperationState.Apply(await _documents.OpenSelectedAsync(Layout));
    }

    public async Task OpenDocumentByPath(string filePath)
    {
        _documentOperationState.Apply(await _documents.OpenPathAsync(filePath, Layout));
    }

    [RelayCommand]
    public async Task SaveDocument()
    {
        _documentOperationState.Apply(await _documents.SaveActiveAsync());
    }

    [RelayCommand]
    private void SetTheme(string? modeName)
    {
        if (!Enum.TryParse<ApplicationThemeMode>(
                modeName,
                ignoreCase: false,
                out var mode) ||
            !Enum.IsDefined(mode))
        {
            return;
        }

        _themeService.SetMode(mode);
        _themeMode = mode;
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    [RelayCommand]
    private void DismissDocumentOperationError() =>
        _documentOperationState.Clear();

    public void DragOver(object? sender, DragEventArgs e)
    {
    }

    public void Drop(object? sender, DragEventArgs e)
    {
    }
}
