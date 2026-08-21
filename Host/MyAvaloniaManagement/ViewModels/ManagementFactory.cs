using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.ViewModels.Hello;
using MyAvaloniaManagement.ViewModels.Tools;

namespace MyAvaloniaManagement.ViewModels;

/// <summary>
/// 负责发现文档/工具策略、创建 Dock 布局并协调工具与文档生命周期。
/// </summary>
/// <remarks>
/// 工厂是宿主 Dock 状态的唯一协调者。它持有稳定 ID 到策略、元数据和实例的映射，
/// 使插件扩展、布局恢复及工具显隐都围绕同一份注册结果工作。
/// </remarks>
internal sealed class ManagementFactory : Factory, IDisposable
{
    private readonly PluginRegistry _extensions;
    private readonly IHostDockableFactory _dockableFactory;
    private readonly IHostDiagnosticSink? _diagnostics;
    private readonly PluginAvailabilityReadModel _availability;
    private readonly HashSet<Document> _ownedDocuments = new(ReferenceEqualityComparer.Instance);
    private IRootDock? _rootDock;
    private DocumentDock? _documentDock;
    private ITool?  _plugGroupMenuTool;
    private bool _acceptingCreations = true;

    // 存储文档类型元数据

    // 存储Tool类型元数据

    // 存储已创建的Tool实例
    private readonly Dictionary<string, Tool> _createdTools;
    private readonly DockDocumentLifetime _documentLifetime;
    private readonly DockWorkspaceBuilder _workspaceBuilder;
    private readonly ToolDockCoordinator _toolDockCoordinator;
    private bool _suppressToolHiddenNotification;
    private readonly DocumentPersistenceStateStore _documentPersistenceStates;
    private readonly DocumentCloseCoordinator? _documentCloseCoordinator;
    private readonly DocumentRecoveryRegistry? _documentRecoveryRegistry;

    internal IReadOnlyDictionary<string, Tool> CreatedTools => _createdTools;

    /// <summary>获取当前 HostRuntime 唯一的根 Dock；布局尚未建立时返回 null。</summary>
    internal IRootDock? RootDock => _rootDock;

    /// <summary>
    /// 当当前根 Dock 已完整提交一次用户可见变化时触发。
    /// </summary>
    /// <remarks>
    /// 这是 Factory 与主窗口之间的定向通知，不接受任意事件类型，也不会进入 Plugin SDK。
    /// 订阅者必须按自身生命周期解除订阅，避免瞬态窗口被单例 Factory 持有。
    /// </remarks>
    internal event EventHandler? LayoutChanged;

    internal Alignment GetToolAlignment(string toolId) =>
        _extensions.TryResolveToolTypeId(toolId, out var typeId) &&
        typeId is not null &&
        _extensions.TryGetToolRegistration(typeId, out var registration) &&
        _availability.IsAvailable(registration.OwnerId)
            ? ToolDockPlacement.ToAlignment(registration.Descriptor.DockSide)
            : Alignment.Left;

    /// <summary>
    /// 创建宿主管理工厂。
    /// </summary>
    /// <param name="documentScopes">把关闭请求路由到宿主或对应插件 Document Scope 的协调器。</param>
    internal ManagementFactory(
        PluginRegistry extensions,
        IHostDockableFactory dockableFactory,
        DocumentScopeRegistry documentScopes,
        DocumentPersistenceStateStore? documentPersistenceStates = null,
        DocumentCloseCoordinator? documentCloseCoordinator = null,
        DocumentRecoveryRegistry? documentRecoveryRegistry = null,
        IHostDiagnosticSink? diagnostics = null,
        PluginAvailabilityReadModel? availability = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(dockableFactory);
        ArgumentNullException.ThrowIfNull(documentScopes);
        _documentLifetime = new DockDocumentLifetime();
        _workspaceBuilder = new DockWorkspaceBuilder(this);
        _documentPersistenceStates = documentPersistenceStates ?? new DocumentPersistenceStateStore();
        _documentCloseCoordinator = documentCloseCoordinator;
        _documentRecoveryRegistry = documentRecoveryRegistry;
        _toolDockCoordinator = new ToolDockCoordinator(
            this,
            _workspaceBuilder,
            GetToolAlignment);
        _extensions = extensions;
        _availability = availability ?? new PluginAvailabilityReadModel(
            new PluginLifecycleStateStore(extensions));
        _dockableFactory = dockableFactory;
        _diagnostics = diagnostics;
        _createdTools = [];
        // 启用 HideToolsOnClose：关闭工具时移入 HiddenDockables 而非真正移除，
        // 这样可以后续通过 RestoreDockable 恢复
        HideToolsOnClose = true;
    }

    /// <summary>
    /// 获取工具管理所需的所有数据
    /// </summary>
    /// <returns>包含工具元数据、已创建工具和根停靠点的结构</returns>
    public ToolManagementData? GetToolManagementData()
    {
        if (_rootDock == null)
        {
            return null;
        }

        return new ToolManagementData
        {
            ToolMetadata = GetAvailableToolDescriptors(),
            CreatedTools = _createdTools,
            RootDock = _rootDock
        };
    }

    internal ToolRegistrySnapshot GetToolRegistrySnapshot() =>
        new(GetAvailableToolDescriptors(), _createdTools);

    internal IEnumerable<DocumentCreationMenuEntry> GetAllDocumentCreationEntries() =>
        _extensions.GetCreationEntries().Where(entry =>
            _extensions.TryGetDocumentRegistration(entry.DocumentTypeId, out var registration) &&
            _availability.IsAvailable(registration.OwnerId));

    internal bool TryGetDocumentRegistration(
        DocumentTypeId documentTypeId,
        out PluginDocumentRegistration registration)
    {
        if (_extensions.TryGetDocumentRegistration(documentTypeId, out registration) &&
            _availability.IsAvailable(registration.OwnerId))
        {
            return true;
        }

        registration = null!;
        return false;
    }

    /// <summary>布局恢复先区分“未声明”和“已声明但生命周期不可用”，两者都禁止部分恢复。</summary>
    internal bool IsRegisteredTool(string toolId) =>
        MyAvaloniaManagement.PluginSdk.ToolTypeId.TryParse(toolId, out var typeId) &&
        typeId is not null &&
        _extensions.TryGetToolRegistration(typeId, out _);

    internal bool IsToolAvailable(string toolId) =>
        MyAvaloniaManagement.PluginSdk.ToolTypeId.TryParse(toolId, out var typeId) &&
        typeId is not null &&
        _extensions.TryGetToolRegistration(typeId, out var registration) &&
        _availability.IsAvailable(registration.OwnerId);

    /// <summary>创建并完全初始化一个尚未发布的 V2 Document Adapter。</summary>
    /// <remarks>
    /// Creation Intent 必须先与冻结 Descriptor 核对。工厂返回后模型、Adapter 和 View 已完整就绪，
    /// 但 Dock 尚未取得所有权；调用者失败时必须通过 <see cref="ReleaseDocument"/> 回滚。
    /// </remarks>
    internal async ValueTask<ManagedDocumentDockable> CreateManagementNewDocumentAsync(
        DocumentTypeId documentTypeId,
        DocumentActivationContext context)
    {
        ArgumentNullException.ThrowIfNull(documentTypeId);
        EnsureAcceptingCreations();
        ArgumentNullException.ThrowIfNull(context);
        if (!_extensions.TryGetDocumentRegistration(documentTypeId, out var registration))
        {
            throw new NotSupportedException($"不支持的 Document 类型：{documentTypeId.Value}。");
        }

        if (context.CreationIntentId is { } intentId &&
            !registration.Descriptor.CreationIntents.Any(item => item.IntentId == intentId))
        {
            throw new ArgumentException(
                $"Document 创建意图 {intentId.Value} 未在 Descriptor 中声明。",
                nameof(context));
        }

        var document = await _dockableFactory.CreateDocumentAsync(documentTypeId, context);
        var adapter = document as ManagedDocumentDockable ??
            throw new InvalidOperationException("V2 Document 工厂只能返回 ManagedDocumentDockable。");
        _ownedDocuments.Add(adapter);
        try
        {
            if (registration.IsPersistable)
            {
                var hostTitle = string.IsNullOrWhiteSpace(context.Title)
                    ? registration.Descriptor.DisplayName
                    : context.Title;
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

    internal async ValueTask<ManagedDocumentDockable> CreateAndPublishDocumentAsync(
        DocumentTypeId documentTypeId,
        DocumentActivationContext context)
    {
        ManagedDocumentDockable? pending =
            await CreateManagementNewDocumentAsync(documentTypeId, context);
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

    /// <summary>
    /// 将已经完成初始化的 Document 发布到主文档 Dock。
    /// </summary>
    /// <param name="document">尚未由 Dock 接管所有权的 Document。</param>
    /// <remarks>
    /// Dock 的添加过程不是原子操作：实现会依次加入集合、激活并聚焦 Document。
    /// 因此不能仅以 <c>AddDocument</c> 是否开始执行判断成功，而要在完整调用返回后确认
    /// 对象确实存在于可见集合中。任何异常都会先撤销已经写入的 Dock 状态，再把异常交还
    /// 调用方；Scope 的释放由仍持有待提交引用的调用方统一完成。
    /// </remarks>
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
                RemoveDockable(document, collapse: false);
            }

            throw;
        }
    }

    /// <summary>
    /// 释放尚未发布或已经正常关闭的 Document。
    /// </summary>
    /// <param name="document">需要结束宿主所有权的 Document。</param>
    /// <remarks>
    /// 正常 Dock 关闭、创建失败和恢复失败必须汇合到同一释放实现，才能稳定保持
    /// “移除控件回收缓存、发出关闭取消、释放 Document 与 scoped 依赖”的既有顺序。
    /// 非托管 Document 与重复调用由底层 Scope 管理器幂等处理。
    /// </remarks>
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
            // 创建失败、加载失败和正常关闭都汇入此入口。即使插件 Dispose 抛出异常，
            // 宿主也必须删除路径与恢复登记，避免已结束对象继续影响重复打开判断。
            if (document is ManagedDocumentDockable adapter)
            {
                _documentPersistenceStates.Remove(adapter);
                _documentRecoveryRegistry?.Clear(adapter);
            }
        }
    }

    private static bool ContainsDocument(
        DocumentDock documentDock,
        Document document) =>
        documentDock.VisibleDockables?.Any(candidate =>
            ReferenceEquals(candidate, document)) == true;

    public override IRootDock CreateLayout()
    {
        EnsureAcceptingCreations();
        // Welcome 与 Tool 一样必须经声明式目录激活，并在发布前完成 Adapter 与 View 预构建。
        // Welcome 是中央工作区的必需项，因此创建失败由调用方中止整个布局初始化。
        var welcomeCreation = _dockableFactory.CreateDocumentAsync(
            HostExtensionIds.V2WelcomeDocument,
            new DocumentActivationContext("欢迎"));
        if (!welcomeCreation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException(
                "Host 内建 Welcome 必须同步完成初始化；布局创建不会阻塞等待任意插件代码。");
        }

        var untitledViewModel = welcomeCreation.Result;
        _ownedDocuments.Add(untitledViewModel);
        var documentDock = new DocumentDock
        {
            Id = DockLayoutIds.Documents,
            Title = "Files",
            IsCollapsable = false,
            Proportion = double.NaN,
            VisibleDockables = CreateList<IDockable>
            (
                untitledViewModel
            )

        };
        // 创建所有注册的Tool
        CreateAllTools();
        return CreateWorkspaceLayout(documentDock);
    }

    internal IRootDock CreateWorkspaceLayout(DocumentDock documentDock)
    {
        var rootDock = _workspaceBuilder.CreateWorkspaceLayout(
            documentDock,
            _createdTools.Values,
            GetToolAlignment);

        _documentDock = documentDock;
        _rootDock = rootDock;
        return rootDock;
    }

    /// <summary>
    /// Dock 在最后一个 Tool 隐藏时会移除空 ToolDock。恢复工具前重建稳定停靠点，
    /// 避免 OriginalOwner 指向已经脱离主布局的 Dock。
    /// </summary>
    internal ToolDock EnsureToolDock(
        IRootDock root,
        Alignment alignment)
        => _toolDockCoordinator.EnsureToolDock(root, alignment);

    /// <summary>
    /// 优先恢复仍附着在主布局中的原 Owner；原 Owner 已被折叠移除时，
    /// 根据它的方向重建稳定 ToolDock 并通过 Factory API 加回工具。
    /// </summary>
    internal bool RestoreTool(
        IRootDock root,
        Tool tool)
        => _toolDockCoordinator.RestoreTool(root, tool);

    /// <summary>
    /// 显示并激活指定的宿主工具；已隐藏的工具会先恢复到稳定停靠区域。
    /// </summary>
    internal bool ShowTool(string toolId)
        => _toolDockCoordinator.ShowTool(_rootDock, _createdTools, toolId);

    /// <summary>
    /// 将 Tool 管理器提交的目标可见状态应用到当前 Dock 树。
    /// </summary>
    /// <returns>仅当目标存在、允许关闭且实际完成状态变化时返回 true。</returns>
    /// <remarks>
    /// 隐藏动作最终会回调 <see cref="OnDockableHidden"/>。本方法在调用期间暂时抑制该回调的
    /// 通知，等活动项调整也完成后再统一提交一次变化；恢复动作则在成功附着后直接提交。
    /// 这样主窗口不会看到半完成布局，也不会为一次用户操作收到两次刷新。
    /// </remarks>
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
        var isCurrentlyVisible = currentDock is not null || isPinned;
        if (isCurrentlyVisible == isVisible)
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
            HideDockable(tool);
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

    /// <summary>
    /// 提交一次已经完成的 Dock 变化：先让 Tool 管理器读取最终 Dock 树，再通知主窗口刷新布局绑定。
    /// </summary>
    internal void NotifyLayoutChanged()
    {
        if (_createdTools.TryGetValue(
                HostExtensionIds.V2ToolManagement.Value,
                out var managementTool) &&
            managementTool is ManagedToolDockable
            {
                Model: IToolVisibilityStateSink visibilityStateSink
            })
        {
            visibilityStateSink.SyncToolsVisibility();
        }

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Dock 在 Top/Bottom 拆分时会创建局部临时 ToolDock。将其中工具立即迁移到
    /// 工作区全宽的稳定停靠点，使拖拽完成后的结构与快照恢复结构保持一致。
    /// </summary>
    public override void OnDockableDocked(
        IDockable? dockable,
        DockOperation operation)
    {
        base.OnDockableDocked(dockable, operation);
        _toolDockCoordinator.OnDockableDocked(
            dockable,
            operation,
            _rootDock);
    }

    /// <summary>
    /// 禁止从当前主窗体 Dock 树创建独立浮动窗口，同时保留拖动和停靠能力。
    /// </summary>
    internal static void DisableFloating(IRootDock rootDock)
    {
        ArgumentNullException.ThrowIfNull(rootDock);
        rootDock.RootDockCapabilityPolicy = new DockCapabilityPolicy
        {
            CanFloat = false
        };
    }

    /// <summary>
    /// 主工作区不支持把单个 Dockable 浮动为独立窗口。
    /// </summary>
    public override void FloatDockable(IDockable dockable)
    {
    }

    /// <summary>
    /// 主工作区不支持把单个 Dockable 浮动为独立窗口。
    /// </summary>
    public override void FloatDockable(
        IDockable dockable,
        DockWindowOptions? options)
    {
    }

    /// <summary>
    /// 主工作区不支持把整个 Dock 浮动为独立窗口。
    /// </summary>
    public override void FloatAllDockables(IDockable dockable)
    {
    }

    /// <summary>
    /// 主工作区不支持把整个 Dock 浮动为独立窗口。
    /// </summary>
    public override void FloatAllDockables(
        IDockable dockable,
        DockWindowOptions? options)
    {
    }

    /// <summary>
    /// 初始化 Dock 定位器，并把稳定工具 ID 映射到当前布局。
    /// </summary>
    /// <remarks>
    /// 插件菜单在 ContextLocator、已创建工具字典和 DockableLocator 中使用同一个 ID，
    /// 防止布局可见但通过 <c>DockableLocator["Plug"]</c> 无法取得真实工具。
    /// </remarks>
    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            [HostExtensionIds.V2PluginMenu.Value] = ()  => layout,
            [HostExtensionIds.V2FileSystemTree.Value] = () => layout,
            [HostExtensionIds.V2ToolManagement.Value] = () => layout,
        };

        // 动态注册所有已创建的工具到 ContextLocator（包括插件工具）
        foreach (var tool in _createdTools.Values)
        {
            if (!ContextLocator.ContainsKey(tool.Id))
            {
                ContextLocator[tool.Id] = () => layout;
            }
        }

        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            ["Root"] = () => _rootDock,
            [DockLayoutIds.Workspace] = () => _rootDock?.ActiveDockable,
            [DockLayoutIds.Documents] = () => _documentDock,
            // 历史 Harness 和插件仍可用 Files 查找；实际持久化 ID 固定为 Documents。
            ["Files"] = () => _documentDock,
            ["Plug"] = () => _plugGroupMenuTool,
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow()
        };

        base.InitLayout(layout);
    }

    /// <summary>
    /// 按依赖顺序创建所有已注册的 Tool 实例。
    /// </summary>
    /// <remarks>
    /// 工具管理器需要读取其他工具的元数据与实例，所以必须最后创建。
    /// </remarks>
    private void CreateAllTools()
    {
        // 先创建所有非工具管理的Tool
        foreach (var toolTypeId in GetAvailableToolDescriptors().Keys.Where(
                     id => id != HostExtensionIds.V2ToolManagement))
        {
            if (!TryCreateTool(toolTypeId, out var tool))
            {
                continue;
            }
            _createdTools[toolTypeId.Value] = tool!;
            // 设置特定工具的引用
            if (toolTypeId == HostExtensionIds.V2PluginMenu)
            {
                _plugGroupMenuTool = tool;
            }
        }

        // 再创建工具管理Tool（需要在其他Tool之后创建，因为它需要读取其他Tool的信息）
        if (_extensions.TryGetToolRegistration(
                HostExtensionIds.V2ToolManagement,
                out var managementRegistration) &&
            _availability.IsAvailable(managementRegistration.OwnerId))
        {
            if (TryCreateTool(HostExtensionIds.V2ToolManagement, out var managementTool))
            {
                _createdTools[managementTool!.Id] = managementTool;
            }
        }
    }

    /// <summary>原子创建单个 Tool Adapter；失败项不会进入布局或已创建索引。</summary>
    private bool TryCreateTool(
        MyAvaloniaManagement.PluginSdk.ToolTypeId toolTypeId,
        out Tool? tool)
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

    /// <summary>
    /// 当 Tool 通过关闭按钮等 Dock 原生入口隐藏后，提交布局和可见状态变化。
    /// </summary>
    /// <remarks>
    /// 只处理当前 Factory 创建的 Tool，Document 和未知 Dockable 不进入此协调链。
    /// Tool 管理器主动隐藏时由 <see cref="TrySetToolVisibility"/> 在全部步骤完成后统一通知，
    /// 因此这里尊重短暂的抑制标志，保证一次变化只有一个提交点。
    /// </remarks>
    public override void OnDockableHidden(IDockable? dockable)
    {
        base.OnDockableHidden(dockable);

        if (!_suppressToolHiddenNotification &&
            dockable is Tool &&
            _createdTools.Values.Contains(dockable))
        {
            NotifyLayoutChanged();
        }
    }

    /// <summary>
    /// 在 Dock 真正移除 Document 前执行公共脏状态保护。
    /// </summary>
    public override bool OnDockableClosing(IDockable? dockable)
    {
        if (dockable is ManagedDocumentDockable document &&
            _documentCloseCoordinator is not null &&
            !_documentCloseCoordinator.TryBeginDockClose(
                document,
                () => CloseDockable(document)))
        {
            return false;
        }

        // base.OnDockableClosing 可能继续执行 Document.OnClose。只有协调器已经允许本次关闭
        // 后才调用它，保证被用户取消的首次请求不会触发插件自己的关闭副作用。
        return base.OnDockableClosing(dockable);
    }

    /// <summary>
    /// Dock 已经完成关闭后，释放托管 Document 对应的依赖注入作用域。
    /// </summary>
    /// <remarks>
    /// 不能在 OnDockableClosing 或 Document.OnClose 中释放：这些阶段仍可能取消关闭。
    /// 使用 finally 可以保证即使其他关闭通知处理器抛出异常，已经从 Dock 移除的 Document
    /// 仍会释放播放器、定时器和文件句柄。历史插件 Document 不在管理器中，调用不会改变其行为。
    /// </remarks>
    public override void OnDockableClosed(IDockable? dockable)
    {
        try
        {
            base.OnDockableClosed(dockable);
        }
        finally
        {
            if (dockable is Document document)
            {
                // Dock 的内容回收缓存默认会永久强引用已关闭的 Document。
                // 最终关闭时只移除当前项，保留其他标签的控件复用行为。
                ReleaseDocument(document);
            }
        }
    }

    /// <summary>
    /// 在插件 Provider 释放前结束仍存活的 Adapter、View 和 Document Scope。
    /// </summary>
    public void Dispose()
    {
        _acceptingCreations = false;
        List<Exception>? releaseFailures = null;
        foreach (var document in _ownedDocuments.ToArray())
        {
            try
            {
                ReleaseDocument(document);
            }
            catch (Exception exception)
            {
                (releaseFailures ??= []).Add(exception);
            }
        }

        foreach (var tool in _createdTools.Values.OfType<IDisposable>().Reverse())
        {
            try
            {
                tool.Dispose();
            }
            catch (Exception exception)
            {
                // Runtime 退出必须尝试释放所有 Adapter/View；最后统一抛出便于诊断，
                // 不能让一个插件 View 的 Dispose 把其他插件资源留在已结束进程中。
                (releaseFailures ??= []).Add(exception);
            }
        }
        _createdTools.Clear();

        if (releaseFailures is not null)
        {
            throw new AggregateException("一个或多个 Host Dock Adapter 释放失败。", releaseFailures);
        }
    }

    /// <summary>由 HostRuntime 在释放任何 Adapter 前调用，阻止退出过程产生新的对象图。</summary>
    internal void BeginShutdown() => _acceptingCreations = false;

    private void EnsureAcceptingCreations()
    {
        if (!_acceptingCreations)
        {
            throw new ObjectDisposedException(nameof(ManagementFactory), "宿主正在退出，不能创建新的贡献。");
        }
    }

    private IReadOnlyDictionary<
        MyAvaloniaManagement.PluginSdk.ToolTypeId,
        MyAvaloniaManagement.PluginSdk.UI.ToolDescriptor> GetAvailableToolDescriptors() =>
        _extensions.Tools
            .Where(item => _availability.IsAvailable(item.OwnerId))
            .ToDictionary(item => item.Descriptor.ToolTypeId, item => item.Descriptor);
}
