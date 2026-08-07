using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Message;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Save;
using Newtonsoft.Json;

namespace MyAvaloniaManagement.ViewModels;

/// <summary>
/// 协调主窗口布局、插件文档创建、文件打开保存以及宿主消息。
/// </summary>
/// <remarks>
/// 本类只负责用例编排：Dock 行为交给 <see cref="ManagementFactory"/>，
/// 布局持久化交给 <see cref="DockLayoutLifecycle"/>，文件系统交给
/// <see cref="IHostStorageService"/>。拆开基础设施依赖后，核心流程无需创建真实窗口即可测试。
/// </remarks>
public partial class MainWindowViewModel : ObservableObject, IDropTarget
{
    private readonly ManagementFactory _factory;
    private readonly PluginMenuService _pluginMenuService;
    private readonly IMessengerService _messengerService;
    private readonly DockLayoutLifecycle _layoutLifecycle;
    private readonly IHostStorageService _storageService;
    private readonly ApplicationThemeService _themeService;
    private ApplicationThemeMode _themeMode;
    private IRootDock? _layout;

    [ObservableProperty]
    private string _documentOperationError = string.Empty;

    public bool HasDocumentOperationError => !string.IsNullOrWhiteSpace(DocumentOperationError);

    /// <summary>
    /// 获取或设置当前主窗口使用的 Dock 根布局。
    /// </summary>
    public IRootDock? Layout
    {
        get => _layout;
        set => SetProperty(ref _layout, value);
    }

    /// <summary>
    /// 获取按菜单分类组织的可创建文档元数据。
    /// </summary>
    public Dictionary<string, List<DocumentMetadata>> DocumentMetadataByCategory =>
        _pluginMenuService?.GetDocumentMetadataByCategory() ?? [];

    public bool IsSystemTheme =>
        _themeMode == ApplicationThemeMode.System;

    public bool IsLightTheme =>
        _themeMode == ApplicationThemeMode.Light;

    public bool IsDarkTheme =>
        _themeMode == ApplicationThemeMode.Dark;

    /// <summary>
    /// 使用显式依赖创建主窗口 ViewModel。
    /// </summary>
    /// <param name="factory">管理工厂</param>
    /// <param name="pluginMenuService">插件菜单服务</param>
    /// <param name="messengerService">消息服务</param>
    /// <param name="layoutLifecycle">Dock 布局准备、恢复和保存生命周期。</param>
    /// <param name="storageService">文件选择与文本读写服务。</param>
    /// <param name="themeService">应用主题切换与持久化服务。</param>
    /// <remarks>
    /// 该构造函数供依赖注入和测试使用。所有外部副作用均由参数提供，
    /// 因此可以验证打开、保存和消息流程，而不需要依赖静态全局状态。
    /// </remarks>
    internal MainWindowViewModel(
        ManagementFactory factory,
        PluginMenuService pluginMenuService,
        IMessengerService messengerService,
        DockLayoutLifecycle layoutLifecycle,
        IHostStorageService storageService,
        ApplicationThemeService themeService)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _pluginMenuService = pluginMenuService ?? throw new ArgumentNullException(nameof(pluginMenuService));
        _messengerService = messengerService ?? throw new ArgumentNullException(nameof(messengerService));
        _layoutLifecycle = layoutLifecycle ?? throw new ArgumentNullException(nameof(layoutLifecycle));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _themeMode = _themeService.CurrentMode;
        
        Layout = _layoutLifecycle.Prepare(_factory);
        
        // 注册消息接收器，用于接收打开文件的请求
        RegisterMessageHandlers();
    }

    /// <summary>
    /// 使用应用全局服务创建实例。
    /// </summary>
    /// <remarks>
    /// 保留无参构造是为了兼容 XAML 设计器和历史调用路径；正式运行时仍由容器解析依赖。
    /// </remarks>
    public MainWindowViewModel() : this(
        ServiceProvider.GetRequiredService<ManagementFactory>(),
        ServiceProvider.GetRequiredService<PluginMenuService>(),
        ServiceProvider.GetRequiredService<IMessengerService>(),
        ServiceProvider.GetRequiredService<DockLayoutLifecycle>(),
        ServiceProvider.GetRequiredService<IHostStorageService>(),
        ServiceProvider.GetRequiredService<ApplicationThemeService>())
    {
    }

    /// <summary>
    /// 在主窗口真正打开后应用准备阶段读取到的待恢复布局。
    /// </summary>
    /// <remarks>
    /// 把恢复延后到窗口 Opened 阶段，可以确保控件、Dock 宿主和资源已经完成初始化。
    /// </remarks>
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

    /// <summary>
    /// 保存当前可用的 Dock 根布局。
    /// </summary>
    /// <remarks>
    /// 此方法由窗口 Closing 生命周期调用，使真实退出和自动化冒烟走同一条生产路径。
    /// </remarks>
    internal void SaveLayout()
    {
        if (Layout is { } root)
        {
            _layoutLifecycle.Save(root, _factory);
        }
    }
    
    /// <summary>
    /// 注册打开文件和布局刷新消息处理器。
    /// </summary>
    /// <remarks>
    /// 消息服务通过构造函数注入，避免 ViewModel 在测试中依赖全局 Messenger。
    /// </remarks>
    private void RegisterMessageHandlers()
    {
        _messengerService.Register<MainWindowViewModel, OpenFileMessage>(
            this, 
            (recipient, message) => 
            {
                // 当接收到打开文件的消息时，调用OpenDocumentByPath方法
                recipient.OpenDocumentByPath(message.FilePath).ConfigureAwait(false);
            }
        );
        
        // 注册布局更新消息处理
        _messengerService.Register<MainWindowViewModel, UpdateLayoutMessage>(
            this, 
            (recipient, _) => 
            {
                // 通知UI更新布局
                recipient.OnPropertyChanged(nameof(Layout));
            }
        );
    }

    /// <summary>
    /// 根据插件文档类型创建文档并加入主文档区域。
    /// </summary>
    /// <param name="documentType">插件注册的文档类型 ID。</param>
    [RelayCommand]
    public void CreateDocument(String documentType)
    {
        var document = _factory?.CreateManagementNewDocument(new DocumentCreationParams(documentType));
        var files = _factory?.GetDockable<IDocumentDock>("Files") as DocumentDock;
        if (document != null)
        {
            files?.AddDocument(document);
        }
    }

    /// <summary>
    /// 显示多文件选择器并依次打开用户选择的文档。
    /// </summary>
    [RelayCommand]
    public async Task OpenDocument()
    {
        var paths = await _storageService.PickOpenFilesAsync();
        await OpenAllFiles(paths);
    }

    /// <summary>
    /// 通过消息或其他宿主入口打开指定路径的文档。
    /// </summary>
    /// <param name="filePath">文件路径字符串</param>
    public async Task OpenDocumentByPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) ||
            !_storageService.FileExists(filePath))
        {
            Console.WriteLine($"文件不存在: {filePath}");
            return;
        }

        await OpenAllFiles([filePath]);
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

    /// <summary>
    /// 逐个处理一批文档路径。
    /// </summary>
    /// <remarks>
    /// 每个文件拥有独立异常边界。重复文件只激活已有标签，损坏文件或未知类型
    /// 只跳过当前项，避免一个失败项提前终止整个批次。
    /// </remarks>
    private async Task OpenAllFiles(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) ||
                    !_storageService.FileExists(path))
                {
                    continue;
                }

                var normalizedPath = NormalizePath(path);
                if (LoadAndActiveIfNowTabHasThisDocument(normalizedPath))
                {
                    continue;
                }

                await LoadAndCreateNewDocument(normalizedPath);
            }
            catch (Exception ex)
            {
                // 单个文件失败不应阻止本次批量选择中的其他文件。
                var fileName = Path.GetFileName(path);
                var reason = ex is DocumentLoadException
                    ? ex.Message
                    : ex is JsonException
                        ? "文件结构损坏或不是受支持的 Document。"
                        : "读取文件失败，请检查文件是否仍然存在且可访问。";
                DocumentOperationError = $"无法打开“{fileName}”：{reason} 原文件未被修改。";
                Console.WriteLine($"打开文档错误: {ex.GetType().Name}");
            }
        }
    }

    [RelayCommand]
    private void DismissDocumentOperationError() => DocumentOperationError = string.Empty;

    partial void OnDocumentOperationErrorChanged(string value) =>
        OnPropertyChanged(nameof(HasDocumentOperationError));

    /// <summary>
    /// 如果规范化路径对应的文档已经打开，则激活现有标签。
    /// </summary>
    /// <returns>找到并激活已有文档时返回 <see langword="true"/>。</returns>
    private bool LoadAndActiveIfNowTabHasThisDocument(string filePath)
    {
        if (Layout is IDock rootDock)
        {
            foreach (var dockable in FindAllDocuments(rootDock))
            {
                if (dockable is ISavableDocument doc &&
                    PathsEqual(doc.FilePath, filePath))
                {
                    // 找到已打开的文档，高亮对应的选项卡
                    if (Layout is IRootDock root)
                    {
                        // 获取包含此文档的文档停靠容器
                        var documentDock = FindDocumentDock(root, dockable);
                        if (documentDock != null)
                        {
                            // 激活此文档
                            documentDock.ActiveDockable = dockable;
                            // 通知UI更新
                            OnPropertyChanged(nameof(Layout));
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 单次读取文件，反序列化元数据，并由注册策略创建对应文档。
    /// </summary>
    /// <remarks>
    /// 文件内容只读取和反序列化一次，随后同一份元数据直接交给文档加载，
    /// 防止两次读取之间文件变化，也消除无用途的中间内存流。
    /// </remarks>
    private async Task LoadAndCreateNewDocument(string filePath)
    {
        var content = await _storageService.ReadAllTextAsync(filePath);

        // 反序列化文档数据
        var documentData = JsonConvert.DeserializeObject<DocumentSaveData>(content);
        if (documentData == null)
            throw new DocumentLoadException("文档信封为空，无法识别文档类型。");

            // 根据DocumentTypeId创建对应的文档
            var document = _factory?.CreateManagementNewDocument(
                new DocumentCreationParams(documentData.DocumentTypeId) { Title = documentData.Title });

            if (document is ISavableDocument savableDocument)
            {
                savableDocument.FilePath = filePath;
                savableDocument.LoadDocumentByMetaData(documentData);

                var filesDock = _factory?.GetDockable<IDocumentDock>("Files") as DocumentDock;
                if (document != null)
                {
                    filesDock?.AddDocument(document);
                }
            }
    }

    /// <summary>
    /// 保存当前激活的可保存文档。
    /// </summary>
    /// <remarks>
    /// 新文档根据其 DocumentMetadata 生成保存类型和扩展名；已有文档直接覆盖原路径。
    /// 保存成功后统一同步文件路径、标题和序列化元数据，保证 UI 与磁盘状态一致。
    /// </remarks>
    [RelayCommand]
    public async Task SaveDocument()
    {
        var activeDocument = GetActiveDocument();
        if (activeDocument is ISavableDocument savableDocument)
        {
            var savePathPolicy = activeDocument as IDocumentSavePathPolicy;
            var originalPath = savableDocument.FilePath;
            string? filePath;
            if (string.IsNullOrEmpty(savableDocument.FilePath)
                || savePathPolicy?.RequiresSaveAs == true)
            {
                var metadata = _factory.GetAllDocumentMetadata()
                    .FirstOrDefault(m =>
                        m.DocumentTypeId == savableDocument.SaveDocumentTypeId);
                filePath = await _storageService.PickSaveFileAsync(metadata);
            }
            else
            {
                filePath = savableDocument.FilePath;
            }

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                filePath = NormalizePath(filePath);
                if (savePathPolicy?.RequiresSaveAs == true
                    && !string.IsNullOrWhiteSpace(originalPath)
                    && PathsEqual(originalPath, filePath))
                {
                    DocumentOperationError = $"{savePathPolicy.SaveAsReason} 请选择不同的文件路径。";
                    return;
                }
                // 从文件路径中提取文件名并设置为文档标题
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var saveData = savableDocument.CreateSaveDocumentMetaData(filePath);
                saveData.Title = fileName;
                await _storageService.WriteAllTextAsync(
                    filePath,
                    JsonConvert.SerializeObject(saveData, Formatting.Indented));
                if (activeDocument is Document document)
                {
                    document.Title = fileName;
                }
                savableDocument.FilePath = filePath;
                savePathPolicy?.NotifySaveCompleted(filePath);
                DocumentOperationError = string.Empty;
            }
        }
    }

    /// <summary>
    /// 获取当前激活的文档
    /// </summary>
    /// <returns>当前激活的文档，如果没有则返回null</returns>
    private IDockable? GetActiveDocument()
    {
        var filesDock = _factory?.GetDockable<IDocumentDock>("Files") as DocumentDock;
        return filesDock?.ActiveDockable;
    }

    /// <summary>
    /// 递归查找 Dock 树中的所有文档。
    /// </summary>
    private static List<IDockable> FindAllDocuments(IDock dock)
    {
        var results = new List<IDockable>();

        if (dock.VisibleDockables != null)
        {
            foreach (var dockable in dock.VisibleDockables)
            {
                if (dockable is IDock childDock)
                {
                    results.AddRange(FindAllDocuments(childDock));
                }
                else if (dockable is Document)
                {
                    results.Add(dockable);
                }
            }
        }

        return results;
    }
    
    /// <summary>
    /// 递归查找直接包含指定文档的文档 Dock。
    /// </summary>
    private static IDocumentDock? FindDocumentDock(IDock dock, IDockable document)
    {
        if (dock is IDocumentDock docDock && 
            docDock.VisibleDockables != null && 
            docDock.VisibleDockables.Contains(document))
        {
            return docDock;
        }
    
        if (dock.VisibleDockables != null)
        {
            foreach (var dockable in dock.VisibleDockables)
            {
                if (dockable is IDock childDock)
                {
                    var result = FindDocumentDock(childDock, document);
                    if (result != null)
                        return result;
                }
            }
        }
    
        return null;
    }

    /// <summary>
    /// 按 Windows 文件系统规则比较两个路径是否指向同一文件。
    /// </summary>
    /// <remarks>
    /// 先转为绝对规范路径，再进行不区分大小写比较，以覆盖相对路径、
    /// 大小写差异和目录分隔形式差异；非法路径按不相等处理。
    /// </remarks>
    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) ||
            string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                NormalizePath(left),
                NormalizePath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) when (
            left.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
            right.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
        }
    }

    /// <summary>
    /// 将文件路径规范化为绝对路径，作为文档去重和持久化的统一形式。
    /// </summary>
    private static string NormalizePath(string path) => Path.GetFullPath(path);

    /// <inheritdoc />
    public void DragOver(object? sender, DragEventArgs e)
    {
    }

    /// <inheritdoc />
    public void Drop(object? sender, DragEventArgs e)
    {
    }
}
