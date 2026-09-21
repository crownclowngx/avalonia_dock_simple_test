using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>携带 Workspace 已经提交的新活动 Document Adapter。</summary>
internal sealed class ActiveDocumentChangedEventArgs(
    ManagedDocumentDockable? document) : EventArgs
{
    /// <summary>获取新的活动实例；当前没有活动 Document 时为 null。</summary>
    internal ManagedDocumentDockable? Document { get; } = document;
}

/// <summary>
/// 拥有一个 HostRuntime 内唯一的工作区会话及其全部运行时对象。
/// </summary>
/// <remarks>
/// Session 只负责工作区所有权：Root/Document Dock、已创建 Tool、已拥有 Document，以及创建、
/// 发布、显隐、关闭和退出的提交顺序。Dock Framework override 由 <see cref="HostDockFactory"/>
/// 负责，持久化文件和布局格式仍由现有 Coordinator/Store 负责，避免把新类型变成另一个万能类。
/// </remarks>
internal sealed partial class WorkspaceSession : IWorkspaceDockCallbacks, IDisposable
{
    private readonly WorkspaceCatalog _catalog;
    private readonly IHostDockableFactory _dockableFactory;
    private readonly DocumentPersistenceStateStore _documentPersistenceStates;
    private readonly DocumentCloseCoordinator _documentCloseCoordinator;
    private readonly DockWindowCloseCoordinator _windowCloseCoordinator;
    private readonly DocumentRecoveryRegistry _documentRecoveryRegistry;
    private readonly IHostDiagnosticSink? _diagnostics;
    private readonly DockDocumentLifetime _documentLifetime;
    private readonly HashSet<Document> _ownedDocuments =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Tool> _createdTools = [];
    private readonly DockWorkspaceBuilder _workspaceBuilder;
    private readonly ToolDockCoordinator _toolDockCoordinator;
    private IRootDock? _rootDock;
    private DocumentDock? _documentDock;
    private readonly DocumentCreationTargetResolver _creationTargets = new();
    private ManagedDocumentDockable? _publishedActiveDocument;
    private bool _acceptingCreations = true;
    private bool _suppressToolHiddenNotification;
    private bool _disposed;
    private DocumentCloseApproval? _applicationCloseApproval;
    private bool _applicationClosePending;
    private bool _shutdownStarted;

    /// <summary>创建具备完整正确性依赖的工作区会话。</summary>
    internal WorkspaceSession(
        HostDockFactory dockFactory,
        WorkspaceCatalog catalog,
        IHostDockableFactory dockableFactory,
        DocumentPersistenceStateStore documentPersistenceStates,
        DocumentCloseCoordinator documentCloseCoordinator,
        DocumentRecoveryRegistry documentRecoveryRegistry,
        DockDocumentLifetime documentLifetime,
        IHostDiagnosticSink? diagnostics = null)
    {
        DockFactory = dockFactory ?? throw new ArgumentNullException(nameof(dockFactory));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _dockableFactory = dockableFactory ?? throw new ArgumentNullException(nameof(dockableFactory));
        _documentPersistenceStates = documentPersistenceStates ??
            throw new ArgumentNullException(nameof(documentPersistenceStates));
        _documentCloseCoordinator = documentCloseCoordinator ??
            throw new ArgumentNullException(nameof(documentCloseCoordinator));
        _documentRecoveryRegistry = documentRecoveryRegistry ??
            throw new ArgumentNullException(nameof(documentRecoveryRegistry));
        _documentLifetime = documentLifetime ??
            throw new ArgumentNullException(nameof(documentLifetime));
        _diagnostics = diagnostics;
        _windowCloseCoordinator = new DockWindowCloseCoordinator(_documentCloseCoordinator,
            action => Avalonia.Threading.Dispatcher.UIThread.Post(action, Avalonia.Threading.DispatcherPriority.Background),
            window => DockFactory.WindowContext.UseOwner(window.Host as Avalonia.Controls.Window));
        _documentCloseCoordinator.StateChanged += OnCloseStateChanged;
        _workspaceBuilder = new DockWorkspaceBuilder(DockFactory);
        _toolDockCoordinator = new ToolDockCoordinator(
            DockFactory,
            _workspaceBuilder,
            GetToolAlignment,
            reason => _diagnostics?.Report(new HostDiagnosticDraft("LAYOUT_TOOL_NORMALIZATION_SKIPPED", HostDiagnosticPhase.Layout)
            { StableId = reason }));
    }

    /// <summary>取得只处理 Dock Framework 的适配工厂。</summary>
    internal HostDockFactory DockFactory { get; }

    /// <summary>工具结构恢复记录不持有业务实例，随唯一 Session 管理。</summary>
    internal DockLayoutWorkspaceState LayoutState { get; } = new();

    /// <summary>取得当前会话唯一根布局；布局尚未建立时返回 null。</summary>
    internal IRootDock? RootDock => _rootDock;

    /// <summary>向布局基础设施提供只读 Tool 实例索引，所有写入仍只发生在 Session 内。</summary>
    internal IReadOnlyDictionary<string, Tool> CreatedTools => _createdTools;

    internal bool CanCreateDocuments => !_disposed && _acceptingCreations && _documentDock is not null;
    internal bool CanOperateTools => !_disposed && _acceptingCreations && _rootDock is not null;
    internal IReadOnlyList<ToolCatalogEntry> GetRegisteredTools() => _catalog.GetRegisteredTools();

    /// <summary>当前工作区完成一次用户可见提交后触发的定向通知。</summary>
    /// <remarks>订阅者必须按自身生命周期解除订阅；该事件不承载任意消息，也不进入 SDK。</remarks>
    internal event EventHandler? LayoutChanged;

    /// <summary>当前活动 Document 实例真正变化后触发的独立语义通知。</summary>
    /// <remarks>
    /// 该通知只比较 Session 所拥有的 Adapter 引用，不复用布局变化，也不因 Tool 激活而误报。
    /// 消费者必须成对退订；事件只在 Host internal 边界使用。
    /// </remarks>
    internal event EventHandler<ActiveDocumentChangedEventArgs>? ActiveDocumentChanged;

    IRootDock? IWorkspaceDockCallbacks.RootDock => _rootDock;

    IReadOnlyCollection<string> IWorkspaceDockCallbacks.CreatedToolIds =>
        _createdTools.Keys.ToArray();

    /// <summary>列出当前会话拥有并仍存活的 Managed Document。</summary>
    internal IReadOnlyList<ManagedDocumentDockable> GetDocuments() =>
        _ownedDocuments.OfType<ManagedDocumentDockable>().ToArray();

    /// <summary>取得当前活动 Document，不向调用方暴露 Root Dock 遍历。</summary>
    internal ManagedDocumentDockable? GetActiveDocument() => _publishedActiveDocument;

    /// <summary>冻结新建入口，排空全部文档命令；许可只到 Runtime 最终关闭或用户取消时释放。</summary>
    internal async Task<bool> PrepareApplicationCloseAsync()
    {
        if (_applicationClosePending || _applicationCloseApproval is not null || _disposed || DockFactory.WindowContext.IsPaletteBusy) return false;
        _applicationClosePending = true;
        _acceptingCreations = false;
        var targets = GetDocuments();
        try
        {
            using var owner = DockFactory.WindowContext.UseOwner(DockFactory.WindowContext.MainWindow);
            var approval = await _documentCloseCoordinator.PrepareRangeCloseAsync(targets, isApplicationExit: true);
            if (approval is null) return false;
            if (_disposed || !targets.ToHashSet().SetEquals(GetDocuments())) { approval.Dispose(); return false; }
            _applicationCloseApproval = approval;
            return true;
        }
        finally
        {
            _applicationClosePending = false;
            if (_applicationCloseApproval is null && !_disposed && !_shutdownStarted) _acceptingCreations = true;
        }
    }

    internal void CancelApplicationClose()
    {
        _applicationCloseApproval?.Dispose();
        _applicationCloseApproval = null;
        if (!_disposed && !_shutdownStarted) _acceptingCreations = true;
    }

    internal void CloseFloatingWindowsForExit()
    {
        if (_applicationCloseApproval is null || _rootDock is null) return;
        foreach (var window in DockTreeNavigator.EnumerateWindows(_rootDock).ToArray())
            if (window.Host is Avalonia.Controls.Window host) host.Close();
    }

    /// <summary>按规范化路径激活已打开或恢复的 Document。</summary>
    internal bool TryActivateDocument(string filePath)
    {
        if (_rootDock is null)
        {
            return false;
        }

        foreach (var document in GetDocuments())
        {
            if (!_documentPersistenceStates.TryGet(document, out var state) ||
                !DocumentPathIdentity.Equals(state.FilePath, filePath))
            {
                continue;
            }

            var dock = DockTreeNavigator.FindDocumentDock(_rootDock, document);
            if (dock is not null)
            {
                ActivateDockable(document);
                return true;
            }
        }

        if (_documentRecoveryRegistry.TryGetBySourcePath(filePath, out var recovered) &&
            DockTreeNavigator.FindDocumentDock(_rootDock, recovered) is { } recoveredDock)
        {
            ActivateDockable(recovered);
            return true;
        }

        return false;
    }

    /// <summary>取得当前可用的全部 Document 创建菜单入口。</summary>
    internal IEnumerable<DocumentCreationMenuEntry> GetAllDocumentCreationEntries() =>
        _catalog.GetCreationEntries();

    /// <summary>解析当前可用且所有权已经冻结的 Document 注册。</summary>
    internal bool TryGetPersistablePluginDocumentRegistration(
        DocumentTypeId documentTypeId,
        out PluginDocumentRegistration registration)
        => _catalog.TryGetPersistablePluginDocument(documentTypeId, out registration);

    /// <summary>判断 Tool 是否存在于冻结 Registry，不把生命周期不可用误报为未声明。</summary>
    internal bool IsRegisteredTool(string toolId) =>
        ToolTypeId.TryParse(toolId, out var typeId) &&
        typeId is not null &&
        _catalog.IsRegisteredTool(toolId);

    /// <summary>判断 Tool 的所有者生命周期当前是否允许使用。</summary>
    internal bool IsToolAvailable(string toolId) =>
        _catalog.IsToolAvailable(toolId);

    /// <summary>取得可用 Tool 的冻结描述符，不创建任何模型。</summary>
    internal IReadOnlyDictionary<ToolTypeId, ToolDescriptor> GetAvailableToolDescriptors() =>
        _catalog.GetAvailableToolDescriptors();

    /// <summary>取得 Tool 的声明方向；不可用或未知 Tool 使用稳定 Left 防御值。</summary>
    internal Alignment GetToolAlignment(string toolId) =>
        _catalog.TryResolveToolTypeId(toolId, out var typeId) &&
        typeId is not null &&
        _catalog.TryGetTool(typeId, out var registration)
            ? ToolDockPlacement.ToAlignment(registration.Descriptor.DockSide)
            : Alignment.Left;

    /// <summary>创建并完全初始化一个尚未发布的 Managed Document。</summary>
    internal async ValueTask<ManagedDocumentDockable> CreateDocumentAsync(
        DocumentTypeId documentTypeId,
        DocumentActivation activation)
    {
        ArgumentNullException.ThrowIfNull(documentTypeId);
        ArgumentNullException.ThrowIfNull(activation);
        EnsureAcceptingCreations();
        if (!_catalog.TryGetDocument(documentTypeId, out var registration))
        {
            throw new NotSupportedException($"不支持的 Document 类型：{documentTypeId.Value}。");
        }

        ValidateActivation(documentTypeId, registration, activation);
        var document = await _dockableFactory.CreateDocumentAsync(documentTypeId, activation);
        var adapter = document as ManagedDocumentDockable ??
            throw new InvalidOperationException("V3 Document 工厂只能返回 ManagedDocumentDockable。");
        _ownedDocuments.Add(adapter);
        try
        {
            if (registration.IsPersistable)
            {
                var hostTitle = string.IsNullOrWhiteSpace(activation.Title)
                    ? registration.Descriptor.DisplayName
                    : activation.Title;
                _documentPersistenceStates.Register(adapter, hostTitle);
            }

            return adapter;
        }
        catch
        {
            ReleaseDocument(adapter);
            throw;
        }
    }

    /// <summary>创建并在全部初始化成功后原子发布 Document。</summary>
    internal async ValueTask<ManagedDocumentDockable> CreateAndPublishDocumentAsync(
        DocumentTypeId documentTypeId,
        DocumentActivation activation,
        DocumentCreationTarget? target = null)
    {
        ManagedDocumentDockable? pending = await CreateDocumentAsync(documentTypeId, activation);
        try
        {
            PublishDocument(pending, target);
            var published = pending;
            pending = null;
            return published;
        }
        finally
        {
            if (pending is not null)
            {
                ReleaseDocument(pending);
            }
        }
    }

    /// <summary>捕获来源窗口的文档组；只借用布局身份，面板结束后不保留这一请求。</summary>
    internal DocumentCreationTarget CaptureDocumentCreationTarget(IRootDock source) => _creationTargets.Capture(source);

    /// <summary>确认目标根仍属于当前会话且未开始整窗关闭，不以全局活动文档推断窗口归属。</summary>
    private bool IsCreationRootAvailable(IRootDock root)
    {
        if (!CanCreateDocuments) return false;
        if (ReferenceEquals(root, _rootDock)) return true;
        var window = _rootDock is null ? null : DockTreeNavigator.EnumerateWindows(_rootDock)
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Layout, root));
        return window is not null && !_windowCloseCoordinator.IsClosing(window) &&
            window.Host?.IsTracked == true;
    }

    /// <summary>
    /// 初始化完成后在 UI 调用链重验并直接发布到最终组。null 保留既有入口的主默认组语义；
    /// 带来源的请求按窗口内回退规则解析。失败只撤销本次插入，不能先在主窗发布再搬运。
    /// </summary>
    internal void PublishDocument(Document document, DocumentCreationTarget? target = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureAcceptingCreations();
        var documentDock = _rootDock is { } root && _documentDock is { } main
            ? _creationTargets.Resolve(root, main, target, IsCreationRootAvailable) : null;
        if (documentDock is null) throw new InvalidOperationException("没有可接收新文档的工作区分组。");
        if (DockTreeNavigator.FindDocumentDock(_rootDock!, document) is not null)
        {
            throw new InvalidOperationException("同一个 Document 实例不能重复发布到 Dock。");
        }

        try
        {
            documentDock.AddDocument(document);
            if (!ContainsDocument(documentDock, document))
            {
                throw new InvalidOperationException("目标文档 Dock 未接受待发布的 Document。");
            }
            PublishActiveDocumentIfChanged(document as ManagedDocumentDockable);
        }
        catch
        {
            if (ContainsDocument(documentDock, document))
            {
                DockFactory.RemoveDockable(document, collapse: false);
            }
            throw;
        }
        if (document is ManagedDocumentDockable page) TrackPublishedPage(page);
    }

    /// <summary>汇合创建失败、恢复失败、最终关闭和 Runtime 退出的 Document 释放。</summary>
    internal void ReleaseDocument(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document is ManagedDocumentDockable page) UntrackPublishedPage(page);
        if (ReferenceEquals(_documentDock?.ActiveDockable, document))
        {
            // 某些 Dock 关闭路径会先回调 OnDockableClosed，稍后才整理 ActiveDockable。
            // Host 必须在释放 Adapter/Scope 前先撤回 Context，确保状态层有机会退订旧 Target。
            _documentDock.ActiveDockable = _documentDock.VisibleDockables?
                .OfType<ManagedDocumentDockable>()
                .FirstOrDefault(candidate => !ReferenceEquals(candidate, document));
            PublishActiveDocumentIfChanged();
        }
        if (ReferenceEquals(_publishedActiveDocument, document))
        {
            var next = GetDocuments().FirstOrDefault(candidate => !ReferenceEquals(candidate, document));
            // 先撤回活动 Target 再释放 Scope，分割区最后一个标签也遵守这一顺序。
            _publishedActiveDocument = null;
            ActiveDocumentChanged?.Invoke(this, new ActiveDocumentChangedEventArgs(null));
            if (next is not null) PublishActiveDocumentIfChanged(next);
        }
        _documentCloseCoordinator.CompleteDockClose(document as ManagedDocumentDockable);
        try
        {
            _ownedDocuments.Remove(document);
            _documentLifetime.Release(document);
        }
        finally
        {
            if (document is ManagedDocumentDockable adapter)
            {
                _documentPersistenceStates.Remove(adapter);
                _documentRecoveryRegistry.Clear(adapter);
            }
        }
    }

    /// <summary>创建或返回当前 HostRuntime 已经建立的唯一默认布局。</summary>
    IRootDock IWorkspaceDockCallbacks.CreateLayout() => CreateLayout();

    internal IRootDock CreateLayout()
    {
        EnsureAcceptingCreations();
        if (_rootDock is not null)
        {
            return _rootDock;
        }

        var welcome = _dockableFactory.CreateHostDocument(
            HostExtensionIds.WelcomeDocument,
            new NewDocumentActivation("欢迎"));
        _ownedDocuments.Add(welcome);
        try
        {
            CreateAllTools();
            var documentDock = new DocumentDock
            {
                Id = DockLayoutIds.Documents,
                Title = "Files",
                IsCollapsable = false,
                Proportion = double.NaN,
                VisibleDockables = DockFactory.CreateList<IDockable>(welcome)
            };
            return CommitWorkspaceLayout(documentDock);
        }
        catch
        {
            ReleaseDocument(welcome);
            throw;
        }
    }

    /// <summary>仅供布局恢复失败时丢弃受污染的启动布局并重建完整默认布局。</summary>
    internal IRootDock RecreateLayoutAfterFailedRestore()
    {
        _documentDock = null;
        PublishActiveDocumentIfChanged();
        foreach (var document in _ownedDocuments.ToArray())
        {
            ReleaseDocument(document);
        }
        _rootDock = null;
        return CreateLayout();
    }

    /// <summary>提交测试或默认构造的 Document Dock，Session 在此成为 Root 的唯一所有者。</summary>
    internal IRootDock CommitWorkspaceLayout(DocumentDock documentDock)
    {
        ArgumentNullException.ThrowIfNull(documentDock);
        var root = _workspaceBuilder.CreateWorkspaceLayout(
            documentDock,
            _createdTools.Values,
            GetToolAlignment);
        _documentDock = documentDock;
        _rootDock = root;
        PublishActiveDocumentIfChanged();
        foreach (var page in GetDocuments().Where(IsPublishedPage)) TrackPublishedPage(page);
        return root;
    }

    /// <summary>提交已经构造完成的恢复容器，继续复用唯一文档集合及其活动实例。</summary>
    internal void CommitRestoredLayout(IRootDock root, DocumentDock documentDock)
    {
        _rootDock = root;
        _documentDock = documentDock;
        PublishActiveDocumentIfChanged();
        NotifyPagesChanged();
    }

    /// <summary>确保指定方向存在稳定 ToolDock。</summary>
    internal ToolDock EnsureToolDock(IRootDock root, Alignment alignment) =>
        _toolDockCoordinator.EnsureToolDock(root, alignment);

    /// <summary>把隐藏 Tool 恢复到仍有效或按声明重建的稳定停靠区域。</summary>
    internal bool RestoreTool(IRootDock root, Tool tool) =>
        LayoutState.TryRestoreFloatingTool(this, tool) || _toolDockCoordinator.RestoreTool(root, tool);

    /// <summary>显示并激活 Tool；只有完整成功后才发布一次布局变化。</summary>
    internal bool ShowTool(ToolTypeId toolTypeId)
    {
        ArgumentNullException.ThrowIfNull(toolTypeId);
        if (!CanOperateTools || !IsToolAvailable(toolTypeId.Value)) return false;
        using var change = DockFactory.BeginLayoutChange();
        if (_createdTools.TryGetValue(toolTypeId.Value, out var hiddenTool))
            LayoutState.TryRestoreFloatingTool(this, hiddenTool);
        var changed = _toolDockCoordinator.ShowTool(
            _rootDock,
            _createdTools,
            toolTypeId.Value);
        if (changed)
        {
            ActivateDockable(_createdTools[toolTypeId.Value]);
            NotifyLayoutChanged();
        }
        return changed;
    }

    internal ToolOperationResult OpenTool(string toolId)
    {
        if (!CanOperateTools) return new(ToolOperationStatus.NotReady, "工作区尚未就绪或正在退出");
        if (!IsRegisteredTool(toolId)) return new(ToolOperationStatus.NotFound, "工具已不存在");
        if (!IsToolAvailable(toolId)) return new(ToolOperationStatus.Unavailable, "插件暂不可用");
        if (!_createdTools.ContainsKey(toolId)) return new(ToolOperationStatus.Unavailable, "工具激活失败，请查看插件诊断");
        try
        {
            return ShowTool(new ToolTypeId(toolId)) ? new(ToolOperationStatus.Changed) :
                new(ToolOperationStatus.Failed, "无法恢复工具");
        }
        catch (Exception exception)
        {
            ReportToolFailure(toolId, exception);
            NotifyLayoutChanged();
            return new(ToolOperationStatus.Failed, "显示工具失败，请查看诊断");
        }
    }

    internal ToolOperationResult SetToolVisibility(string toolId, bool visible)
    {
        if (!CanOperateTools) return new(ToolOperationStatus.NotReady, "工作区尚未就绪或正在退出");
        if (!IsRegisteredTool(toolId)) return new(ToolOperationStatus.NotFound, "工具已不存在");
        if (!_createdTools.TryGetValue(toolId, out var tool)) return new(ToolOperationStatus.Unavailable, "工具没有可用实例");
        if (visible && !IsToolAvailable(toolId)) return new(ToolOperationStatus.Unavailable, "插件暂不可用");
        var isVisible = DockTreeNavigator.IsDockableAttached(_rootDock!, tool) || DockTreeNavigator.IsToolPinned(_rootDock!, tool);
        if (isVisible == visible) return new(ToolOperationStatus.AlreadySatisfied);
        try
        {
            return TrySetToolVisibility(toolId, visible) ? new(ToolOperationStatus.Changed) :
                new(ToolOperationStatus.Failed, "工具布局未接受显隐操作");
        }
        catch (Exception exception)
        {
            ReportToolFailure(toolId, exception);
            NotifyLayoutChanged();
            return new(ToolOperationStatus.Failed, "修改工具布局失败，请查看诊断");
        }
    }

    /// <summary>全量目标在提交前捕获；失败保留成功结果，最终只发布一次布局快照。</summary>
    internal ToolBatchResult HideAllTools()
    {
        var results = new Dictionary<string, ToolOperationResult>(StringComparer.Ordinal);
        _toolBatchDepth++;
        try
        {
            foreach (var id in _createdTools.Keys.ToArray()) results[id] = SetToolVisibility(id, false);
        }
        finally { _toolBatchDepth--; NotifyLayoutChanged(); }
        return new(results);
    }

    private void ReportToolFailure(string id, Exception exception)
    {
        try { _diagnostics?.Report(new HostDiagnosticDraft(HostDiagnosticCodes.ToolLayoutOperationFailed, HostDiagnosticPhase.Layout)
            { StableId = id, Exception = exception }); }
        catch { /* 诊断失败不能改变已经发生的布局提交。 */ }
    }

    /// <summary>把 Tool 管理器的目标显隐状态作为一次工作区提交执行。</summary>
    internal bool TrySetToolVisibility(string toolId, bool isVisible)
    {
        if (!CanOperateTools || _rootDock is null ||
            string.IsNullOrWhiteSpace(toolId) ||
            !_createdTools.TryGetValue(toolId, out var tool) ||
            !tool.CanClose || (isVisible && !IsToolAvailable(toolId)))
        {
            return false;
        }

        var currentDock = DockTreeNavigator.FindToolDock(_rootDock, tool) as IDock ?? DockTreeNavigator.FindDocumentDock(_rootDock, tool);
        var isPinned = DockTreeNavigator.IsToolPinned(_rootDock, tool);
        var currentVisibility = currentDock is not null || isPinned;
        if (currentVisibility == isVisible)
        {
            return false;
        }

        using var change = DockFactory.BeginLayoutChange();
        if (isVisible)
        {
            if (!RestoreTool(_rootDock, tool))
            {
                return false;
            }
            NotifyLayoutChanged();
            return true;
        }

        var nextActive = currentDock?.VisibleDockables?
            .FirstOrDefault(candidate => !ReferenceEquals(candidate, tool));
        _suppressToolHiddenNotification = true;
        try
        {
            DockFactory.HideDockable(tool);
            // 最后一个浮动工具会先走可取消窗口关闭。仍在原树中意味着未提交隐藏，
            // 此时不能清空活动项或向工具中心报告成功；模型、View 及原布局继续由本会话持有。
            if (DockTreeNavigator.IsDockableAttached(_rootDock, tool) || DockTreeNavigator.IsToolPinned(_rootDock, tool))
                return false;
            if (currentDock is not null)
            {
                currentDock.ActiveDockable = nextActive;
            }
        }
        finally
        {
            _suppressToolHiddenNotification = false;
        }
        NotifyLayoutChanged();
        return true;
    }

    /// <summary>由 HostRuntime 先关闭创建入口，再开始释放 Adapter。</summary>
    internal void BeginShutdown()
    {
        _shutdownStarted = true;
        _acceptingCreations = false;
        NotifyLayoutChanged();
    }

    /// <summary>按 Document 在前、Tool 逆序在后的所有权顺序释放工作区。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _documentCloseCoordinator.StateChanged -= OnCloseStateChanged;
        _windowCloseCoordinator.Dispose();
        _applicationCloseApproval?.Dispose();
        _applicationCloseApproval = null;
        _acceptingCreations = false;
        _documentDock = null;
        PublishActiveDocumentIfChanged();
        List<Exception>? failures = null;
        foreach (var document in _ownedDocuments.ToArray())
        {
            TryRelease(() => ReleaseDocument(document), ref failures);
        }
        foreach (var tool in _createdTools.Values.OfType<IDisposable>().Reverse())
        {
            TryRelease(tool.Dispose, ref failures);
        }
        _createdTools.Clear();
        if (failures is not null)
        {
            throw new AggregateException("一个或多个 Workspace Adapter 释放失败。", failures);
        }
    }

    IDockable? IWorkspaceDockCallbacks.ResolveDockable(string dockableId) => dockableId switch
    {
        DockLayoutIds.Documents => _documentDock,
        _ when _createdTools.TryGetValue(dockableId, out var tool) => tool,
        _ => null,
    };

    void IWorkspaceDockCallbacks.OnDockableDocked(IDockable? dockable, DockOperation operation)
    {
        NotifyLayoutChanged();
    }

    void IWorkspaceDockCallbacks.OnDockSplitCompleted(IDock originalTarget, IDockable insertedDock, DockOperation operation)
    {
        // Session 确认主窗口归属；局部遍历不进入 Windows，避免浮窗同名 Dock 命中主骨架策略。
        if (_rootDock is not { } root ||
            !DockTreeNavigator.Enumerate(root).Any(item => ReferenceEquals(item, originalTarget)) ||
            !DockTreeNavigator.Enumerate(root).Any(item => ReferenceEquals(item, insertedDock))) return;
        _toolDockCoordinator.OnDockSplitCompleted(originalTarget, insertedDock, operation, root);
    }

    void IWorkspaceDockCallbacks.OnDockableHidden(IDockable? dockable)
    {
        if (!_suppressToolHiddenNotification &&
            dockable is Tool tool &&
            _createdTools.Values.Contains(tool))
        {
            NotifyLayoutChanged();
        }
    }

    void IWorkspaceDockCallbacks.OnActiveDockableChanged(IDockable? dockable)
    {
        if (_rootDock is { } root) _creationTargets.Remember(root, dockable);
        // 分割后的页面属于其他 DocumentDock；直接使用真实激活通知，不能只读取最初的主 Dock。
        // 工具获得焦点不会替换文档命令的活动目标，沿用已有活动页面引用。
        PublishActiveDocumentIfChanged(dockable is null ? null : dockable as ManagedDocumentDockable ?? _publishedActiveDocument);
    }

    bool IWorkspaceDockCallbacks.OnDockableClosing(IDockable? dockable)
    {
        if (dockable is ManagedDocumentDockable document &&
            !_documentCloseCoordinator.TryBeginDockClose(
                document,
                () => DockFactory.CloseDockable(document)))
        {
            return false;
        }
        return true;
    }

    void IWorkspaceDockCallbacks.OnDockableClosed(IDockable? dockable)
    {
        if (dockable is Document document)
        {
            ReleaseDocument(document);
        }
    }

    void IWorkspaceDockCallbacks.OnDockableCloseRejected(IDockable? dockable)
    {
        if (dockable is ManagedDocumentDockable document)
        {
            _documentCloseCoordinator.ReopenAfterDockRejection(document);
        }
    }

    bool IWorkspaceDockCallbacks.OnWindowClosing(IDockWindow window) =>
        _applicationCloseApproval is not null || _windowCloseCoordinator.TryBeginClose(window, () =>
        {
            // 使用原生 Close 重试同一窗口；不能在此释放 Runtime 或 Provider。
            if (window.Host is Avalonia.Controls.Window host) host.Close();
            else window.Exit();
        });

    void IWorkspaceDockCallbacks.OnWindowCloseCompleted(IDockWindow window)
    {
        _windowCloseCoordinator.Complete(window);
        NotifyLayoutChanged();
    }

    void IWorkspaceDockCallbacks.OnLayoutChanging() => CaptureLayoutState();
    void IWorkspaceDockCallbacks.OnLayoutChanged()
    {
        CaptureLayoutState();
        NotifyLayoutChanged();
        NotifyPagesChanged();
    }

    private void CaptureLayoutState()
    {
        if (_disposed || _rootDock is null || DockFactory.DockableLocator is null) return;
        try { LayoutState.Capture(this); }
        catch (Exception exception)
        {
            _diagnostics?.Report(new HostDiagnosticDraft("LAYOUT_CAPTURE_FAILED", HostDiagnosticPhase.Layout) { Exception = exception });
        }
    }

    private static void ValidateActivation(
        DocumentTypeId documentTypeId,
        IWorkspaceDocumentRegistration registration,
        DocumentActivation activation)
    {
        switch (activation)
        {
            case NewDocumentActivation { CreationIntentId: { } intentId }
                when !registration.Descriptor.CreationIntents.Any(item => item.IntentId == intentId):
                throw new ArgumentException(
                    $"Document 创建意图 {intentId.Value} 未在 Descriptor 中声明。",
                    nameof(activation));
            case NewDocumentActivation:
                return;
            case RestoreDocumentActivation when !registration.IsPersistable:
                throw new NotSupportedException(
                    $"Document 类型 {documentTypeId.Value} 未声明持久化能力，不能使用恢复激活。");
            case RestoreDocumentActivation:
                return;
            default:
                throw new NotSupportedException(
                    $"不支持的 Document 激活类型：{activation.GetType().FullName}。");
        }
    }

    private void CreateAllTools()
    {
        if (_createdTools.Count != 0)
        {
            return;
        }
        foreach (var toolTypeId in GetAvailableToolDescriptors().Keys)
        {
            if (!TryCreateTool(toolTypeId, out var tool))
            {
                continue;
            }
            _createdTools[toolTypeId.Value] = tool!;
        }

    }

    private bool TryCreateTool(ToolTypeId toolTypeId, out Tool? tool)
    {
        try
        {
            tool = _dockableFactory.CreateTool(toolTypeId);
            return true;
        }
        catch (Exception exception)
        {
            tool = null;
            _diagnostics?.Report(new HostDiagnosticDraft(
                HostDiagnosticCodes.ToolAdapterActivationFailed,
                HostDiagnosticPhase.ExtensionDiscovery)
            {
                StableId = toolTypeId.Value,
                Exception = exception,
            });
            return false;
        }
    }

    private int _toolBatchDepth;
    private void NotifyLayoutChanged()
    {
        if (_toolBatchDepth != 0 || DockFactory.IsLayoutChangeInProgress) return;
        foreach (EventHandler handler in LayoutChanged?.GetInvocationList() ?? [])
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception exception) { ReportToolFailure("workspace", exception); }
        }
    }

    /// <summary>沿用唯一活动页引用；显式页面优先，布局建立和释放时回退主 DocumentDock。</summary>
    private void PublishActiveDocumentIfChanged(ManagedDocumentDockable? selected = null)
    {
        var current = selected ?? _documentDock?.ActiveDockable as ManagedDocumentDockable;
        if (current is not null && !_ownedDocuments.Contains(current)) current = null;
        if (ReferenceEquals(_publishedActiveDocument, current))
        {
            return;
        }

        _publishedActiveDocument = current;
        ActiveDocumentChanged?.Invoke(this, new ActiveDocumentChangedEventArgs(current));
    }

    private void EnsureAcceptingCreations()
    {
        if (_disposed || !_acceptingCreations)
        {
            throw new ObjectDisposedException(
                nameof(WorkspaceSession),
                "宿主正在退出，不能创建新的工作区贡献。");
        }
    }

    private static bool ContainsDocument(IDocumentDock dock, Document document) =>
        dock.VisibleDockables?.Any(candidate => ReferenceEquals(candidate, document)) == true;

    private static void TryRelease(Action release, ref List<Exception>? failures)
    {
        try
        {
            release();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
    }
}
