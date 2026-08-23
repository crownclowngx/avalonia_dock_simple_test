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
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>
/// 拥有一个 HostRuntime 内唯一的工作区会话及其全部运行时对象。
/// </summary>
/// <remarks>
/// Session 只负责工作区所有权：Root/Document Dock、已创建 Tool、已拥有 Document，以及创建、
/// 发布、显隐、关闭和退出的提交顺序。Dock Framework override 由 <see cref="HostDockFactory"/>
/// 负责，持久化文件和布局格式仍由现有 Coordinator/Store 负责，避免把新类型变成另一个万能类。
/// </remarks>
internal sealed class WorkspaceSession : IWorkspaceDockCallbacks, IDisposable
{
    private readonly WorkspaceCatalog _catalog;
    private readonly IHostDockableFactory _dockableFactory;
    private readonly DocumentPersistenceStateStore _documentPersistenceStates;
    private readonly DocumentCloseCoordinator _documentCloseCoordinator;
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
    private bool _acceptingCreations = true;
    private bool _suppressToolHiddenNotification;
    private bool _disposed;

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
        _workspaceBuilder = new DockWorkspaceBuilder(DockFactory);
        _toolDockCoordinator = new ToolDockCoordinator(
            DockFactory,
            _workspaceBuilder,
            GetToolAlignment);
    }

    /// <summary>取得只处理 Dock Framework 的适配工厂。</summary>
    internal HostDockFactory DockFactory { get; }

    /// <summary>取得当前会话唯一根布局；布局尚未建立时返回 null。</summary>
    internal IRootDock? RootDock => _rootDock;

    /// <summary>向布局基础设施提供只读 Tool 实例索引，所有写入仍只发生在 Session 内。</summary>
    internal IReadOnlyDictionary<string, Tool> CreatedTools => _createdTools;

    /// <summary>当前工作区完成一次用户可见提交后触发的定向通知。</summary>
    /// <remarks>订阅者必须按自身生命周期解除订阅；该事件不承载任意消息，也不进入 SDK。</remarks>
    internal event EventHandler? LayoutChanged;

    IRootDock? IWorkspaceDockCallbacks.RootDock => _rootDock;

    IReadOnlyCollection<string> IWorkspaceDockCallbacks.CreatedToolIds =>
        _createdTools.Keys.ToArray();

    /// <summary>列出当前会话拥有并仍存活的 Managed Document。</summary>
    internal IReadOnlyList<ManagedDocumentDockable> GetDocuments() =>
        _ownedDocuments.OfType<ManagedDocumentDockable>().ToArray();

    /// <summary>取得当前活动 Document，不向调用方暴露 Root Dock 遍历。</summary>
    internal ManagedDocumentDockable? GetActiveDocument() =>
        _documentDock?.ActiveDockable as ManagedDocumentDockable;

    /// <summary>判断窗口关闭是否存在需要确认的脏 Document。</summary>
    internal bool HasDirtyDocuments() =>
        GetDocuments().Any(_documentCloseCoordinator.IsDirty);

    /// <summary>汇总当前会话全部脏 Document 的窗口关闭确认。</summary>
    internal Task<bool> ConfirmWindowCloseAsync() =>
        _documentCloseCoordinator.ConfirmWindowCloseAsync(GetDocuments());

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
                dock.ActiveDockable = document;
                return true;
            }
        }

        if (_documentRecoveryRegistry.TryGetBySourcePath(filePath, out var recovered) &&
            DockTreeNavigator.FindDocumentDock(_rootDock, recovered) is { } recoveredDock)
        {
            recoveredDock.ActiveDockable = recovered;
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
        DocumentActivation activation)
    {
        ManagedDocumentDockable? pending = await CreateDocumentAsync(documentTypeId, activation);
        try
        {
            PublishDocument(pending);
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

    /// <summary>将候选 Document 提交到当前主文档 Dock，失败时撤销部分 Dock 写入。</summary>
    internal void PublishDocument(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var documentDock = _documentDock ??
            throw new InvalidOperationException("主文档 Dock 尚未初始化，无法发布 Document。");
        if (ContainsDocument(documentDock, document))
        {
            throw new InvalidOperationException("同一个 Document 实例不能重复发布到 Dock。");
        }

        try
        {
            documentDock.AddDocument(document);
            if (!ContainsDocument(documentDock, document))
            {
                throw new InvalidOperationException("主文档 Dock 未接受待发布的 Document。");
            }
        }
        catch
        {
            if (ContainsDocument(documentDock, document))
            {
                DockFactory.RemoveDockable(document, collapse: false);
            }
            throw;
        }
    }

    /// <summary>汇合创建失败、恢复失败、最终关闭和 Runtime 退出的 Document 释放。</summary>
    internal void ReleaseDocument(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
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
        foreach (var document in _ownedDocuments.ToArray())
        {
            ReleaseDocument(document);
        }
        _rootDock = null;
        _documentDock = null;
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
        return root;
    }

    /// <summary>确保指定方向存在稳定 ToolDock。</summary>
    internal ToolDock EnsureToolDock(IRootDock root, Alignment alignment) =>
        _toolDockCoordinator.EnsureToolDock(root, alignment);

    /// <summary>把隐藏 Tool 恢复到仍有效或按声明重建的稳定停靠区域。</summary>
    internal bool RestoreTool(IRootDock root, Tool tool) =>
        _toolDockCoordinator.RestoreTool(root, tool);

    /// <summary>显示并激活 Tool；只有完整成功后才发布一次布局变化。</summary>
    internal bool ShowTool(ToolTypeId toolTypeId)
    {
        ArgumentNullException.ThrowIfNull(toolTypeId);
        var changed = _toolDockCoordinator.ShowTool(
            _rootDock,
            _createdTools,
            toolTypeId.Value);
        if (changed)
        {
            NotifyLayoutChanged();
        }
        return changed;
    }

    /// <summary>把 Tool 管理器的目标显隐状态作为一次工作区提交执行。</summary>
    internal bool TrySetToolVisibility(string toolId, bool isVisible)
    {
        if (_rootDock is null ||
            string.IsNullOrWhiteSpace(toolId) ||
            !_createdTools.TryGetValue(toolId, out var tool) ||
            !tool.CanClose)
        {
            return false;
        }

        var currentDock = DockTreeNavigator.FindToolDock(_rootDock, tool);
        var isPinned = DockTreeNavigator.IsToolPinned(_rootDock, tool);
        var currentVisibility = currentDock is not null || isPinned;
        if (currentVisibility == isVisible)
        {
            return false;
        }

        if (isVisible)
        {
            if (!_toolDockCoordinator.RestoreTool(_rootDock, tool))
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
    internal void BeginShutdown() => _acceptingCreations = false;

    /// <summary>按 Document 在前、Tool 逆序在后的所有权顺序释放工作区。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _acceptingCreations = false;
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

    void IWorkspaceDockCallbacks.OnDockableDocked(IDockable? dockable, DockOperation operation) =>
        _toolDockCoordinator.OnDockableDocked(dockable, operation, _rootDock);

    void IWorkspaceDockCallbacks.OnDockableHidden(IDockable? dockable)
    {
        if (!_suppressToolHiddenNotification &&
            dockable is Tool tool &&
            _createdTools.Values.Contains(tool))
        {
            NotifyLayoutChanged();
        }
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
        foreach (var toolTypeId in GetAvailableToolDescriptors().Keys.Where(
                     id => id != HostExtensionIds.ToolManagement))
        {
            if (!TryCreateTool(toolTypeId, out var tool))
            {
                continue;
            }
            _createdTools[toolTypeId.Value] = tool!;
        }

        if (_catalog.TryGetTool(HostExtensionIds.ToolManagement, out _) &&
            TryCreateTool(HostExtensionIds.ToolManagement, out var managementTool))
        {
            _createdTools[managementTool!.Id] = managementTool;
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

    private void NotifyLayoutChanged() => LayoutChanged?.Invoke(this, EventArgs.Empty);

    private void EnsureAcceptingCreations()
    {
        if (!_acceptingCreations)
        {
            throw new ObjectDisposedException(
                nameof(WorkspaceSession),
                "宿主正在退出，不能创建新的工作区贡献。");
        }
    }

    private static bool ContainsDocument(DocumentDock dock, Document document) =>
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
