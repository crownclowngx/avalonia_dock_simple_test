using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Message;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;

namespace MyAvaloniaManagement.ViewModels;

/// <summary>
/// 负责主窗口绑定状态、命令和消息编排，并把 Dock 布局及文档持久化委托给内部服务。
/// 该边界让 ViewModel 保持 UI 协调职责，不直接承担文件事务和 Dock 树遍历。
/// </summary>
public partial class MainWindowViewModel : ObservableObject, IDropTarget
{
    private readonly ManagementFactory _factory;
    private readonly PluginMenuService _pluginMenuService;
    private readonly IMessengerService _messengerService;
    private readonly DockLayoutLifecycle _layoutLifecycle;
    private readonly ApplicationThemeService _themeService;
    private readonly DocumentPersistenceCoordinator _documents;
    private ApplicationThemeMode _themeMode;
    private IRootDock? _layout;

    [ObservableProperty]
    private string _documentOperationError = string.Empty;

    public bool HasDocumentOperationError =>
        !string.IsNullOrWhiteSpace(DocumentOperationError);

    public IRootDock? Layout
    {
        get => _layout;
        set => SetProperty(ref _layout, value);
    }

    public Dictionary<string, List<DocumentMetadata>> DocumentMetadataByCategory =>
        _pluginMenuService?.GetDocumentMetadataByCategory() ?? [];

    public bool IsSystemTheme => _themeMode == ApplicationThemeMode.System;

    public bool IsLightTheme => _themeMode == ApplicationThemeMode.Light;

    public bool IsDarkTheme => _themeMode == ApplicationThemeMode.Dark;

    internal MainWindowViewModel(
        ManagementFactory factory,
        PluginMenuService pluginMenuService,
        IMessengerService messengerService,
        DockLayoutLifecycle layoutLifecycle,
        IHostStorageService storageService,
        ApplicationThemeService themeService)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _pluginMenuService = pluginMenuService ??
            throw new ArgumentNullException(nameof(pluginMenuService));
        _messengerService = messengerService ??
            throw new ArgumentNullException(nameof(messengerService));
        _layoutLifecycle = layoutLifecycle ??
            throw new ArgumentNullException(nameof(layoutLifecycle));
        ArgumentNullException.ThrowIfNull(storageService);
        _themeService = themeService ??
            throw new ArgumentNullException(nameof(themeService));
        _documents = new DocumentPersistenceCoordinator(factory, storageService);
        _themeMode = _themeService.CurrentMode;

        Layout = _layoutLifecycle.Prepare(_factory);
        RegisterMessageHandlers();
    }

    /// <summary>
    /// 为 XAML 设计器和历史调用方保留的兼容构造函数。
    /// 生产环境仍通过依赖注入解析显式构造函数，避免新增业务逻辑依赖静态定位器。
    /// </summary>
    public MainWindowViewModel() : this(
        ServiceProvider.GetRequiredService<ManagementFactory>(),
        ServiceProvider.GetRequiredService<PluginMenuService>(),
        ServiceProvider.GetRequiredService<IMessengerService>(),
        ServiceProvider.GetRequiredService<DockLayoutLifecycle>(),
        ServiceProvider.GetRequiredService<IHostStorageService>(),
        ServiceProvider.GetRequiredService<ApplicationThemeService>())
    {
    }

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

    private void RegisterMessageHandlers()
    {
        _messengerService.Register<MainWindowViewModel, OpenFileMessage>(
            this,
            static (recipient, message) =>
                recipient.ObserveOpenMessage(message.FilePath));

        _messengerService.Register<MainWindowViewModel, UpdateLayoutMessage>(
            this,
            static (recipient, _) =>
                recipient.OnPropertyChanged(nameof(Layout)));
    }

    private void ObserveOpenMessage(string filePath) =>
        _ = ObserveOpenMessageAsync(filePath);

    private async Task ObserveOpenMessageAsync(string filePath)
    {
        try
        {
            await OpenDocumentByPath(filePath);
        }
        catch (Exception exception)
        {
            DocumentOperationError =
                "无法打开文件：宿主处理文档时发生意外错误。原文件未被修改。";
            Console.Error.WriteLine(
                $"DocumentPersistence errorCode=DOCUMENT_MESSAGE_FAILED type={exception.GetType().Name}");
        }
    }

    [RelayCommand]
    public void CreateDocument(string documentType) =>
        _documents.CreateDocument(documentType);

    [RelayCommand]
    public async Task OpenDocument()
    {
        ApplyOperationResult(await _documents.OpenSelectedAsync(Layout));
    }

    public async Task OpenDocumentByPath(string filePath)
    {
        ApplyOperationResult(await _documents.OpenPathAsync(filePath, Layout));
    }

    [RelayCommand]
    public async Task SaveDocument()
    {
        ApplyOperationResult(await _documents.SaveActiveAsync());
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
        DocumentOperationError = string.Empty;

    partial void OnDocumentOperationErrorChanged(string value) =>
        OnPropertyChanged(nameof(HasDocumentOperationError));

    private void ApplyOperationResult(DocumentOperationResult result)
    {
        if (result.ShouldUpdateError)
        {
            DocumentOperationError = result.Error;
        }
    }

    public void DragOver(object? sender, DragEventArgs e)
    {
    }

    public void Drop(object? sender, DragEventArgs e)
    {
    }
}
