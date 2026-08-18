using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Message;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.ViewModels.Hello;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Events;
using MyAvaloniaManagementCommon.Save;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.ViewModels;

/// <summary>
/// 负责发现文档/工具策略、创建 Dock 布局并协调工具与文档生命周期。
/// </summary>
/// <remarks>
/// 工厂是宿主 Dock 状态的唯一协调者。它持有稳定 ID 到策略、元数据和实例的映射，
/// 使插件扩展、布局恢复及工具显隐都围绕同一份注册结果工作。
/// </remarks>
internal sealed class ManagementFactory : Factory
{
    private readonly PluginRegistry _extensions;
    private IRootDock? _rootDock;
    private DocumentDock? _documentDock;
    private ITool?  _plugGroupMenuTool;
    
    // 存储文档类型元数据
    
    // 存储Tool类型元数据
    
    // 存储已创建的Tool实例
    private readonly Dictionary<string, Tool> _createdTools;
    private readonly DockDocumentLifetime _documentLifetime;
    private readonly DockWorkspaceBuilder _workspaceBuilder;
    private readonly ToolDockCoordinator _toolDockCoordinator;
    private readonly IHostEventBus _eventBus;
    private readonly DocumentPersistenceStateStore _documentPersistenceStates;
    private readonly DocumentCloseCoordinator? _documentCloseCoordinator;
    private readonly DocumentRecoveryRegistry? _documentRecoveryRegistry;

    internal IReadOnlyDictionary<string, Tool> CreatedTools => _createdTools;

    /// <summary>
    /// 把布局文件中的历史 Tool ID 归一化为当前规范 ID；未知值原样返回，交给运行时校验处理。
    /// </summary>
    internal string NormalizePersistedToolId(string toolId) =>
        _extensions.TryResolveToolTypeId(toolId, out var typeId) && typeId is not null
            ? typeId.Value
            : toolId;

    internal DocumentTypeId NormalizePersistedDocumentTypeId(DocumentTypeId documentTypeId) =>
        _extensions.ResolveDocumentTypeId(documentTypeId);

    internal Alignment GetToolAlignment(string toolId) =>
        _extensions.TryResolveToolTypeId(toolId, out var typeId) &&
        typeId is not null &&
        _extensions.ToolMetadata.TryGetValue(typeId, out var metadata)
            ? ToolDockPlacement.ToAlignment(metadata.DockSide)
            : Alignment.Left;

    /// <summary>
    /// 创建宿主管理工厂。
    /// </summary>
    /// <param name="serviceProvider">用于激活宿主策略和插件策略的服务提供器。</param>
    /// <param name="pluginModuleCatalog">已发现且受宿主管理的插件模块目录。</param>
    /// <param name="documentScopeManager">管理插件文档独立依赖注入作用域的服务。</param>
    /// <param name="messengerService">发布工具显隐变化的消息服务。</param>
    /// <remarks>
    /// 消息服务直接注入，避免关闭工具时回到静态 ServiceProvider 查找依赖，
    /// 从而使工厂的依赖关系可验证，也避免测试和多容器场景取到错误实例。
    /// </remarks>
    internal ManagementFactory(
        PluginRegistry extensions,
        DocumentScopeManager documentScopeManager,
        IHostEventBus eventBus,
        DocumentPersistenceStateStore? documentPersistenceStates = null,
        DocumentCloseCoordinator? documentCloseCoordinator = null,
        DocumentRecoveryRegistry? documentRecoveryRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(documentScopeManager);
        _documentLifetime = new DockDocumentLifetime(documentScopeManager);
        _workspaceBuilder = new DockWorkspaceBuilder(this);
        _eventBus = eventBus;
        _documentPersistenceStates = documentPersistenceStates ?? new DocumentPersistenceStateStore();
        _documentCloseCoordinator = documentCloseCoordinator;
        _documentRecoveryRegistry = documentRecoveryRegistry;
        _toolDockCoordinator = new ToolDockCoordinator(
            this,
            _workspaceBuilder,
            GetToolAlignment,
            eventBus);
        _extensions = extensions;
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
            ToolMetadata = _extensions.ToolMetadata,
            CreatedTools = _createdTools,
            RootDock = _rootDock
        };
    }

    internal ToolRegistrySnapshot GetToolRegistrySnapshot() =>
        new(_extensions.ToolMetadata, _createdTools);
    
    /// <summary>
    /// 获取所有文档类型的元数据
    /// </summary>
    /// <returns>所有文档类型的元数据列表</returns>
    public IEnumerable<DocumentMetadata> GetAllDocumentMetadata()
        => _extensions.DocumentMetadata.Values;

    /// <summary>
    /// 获取宿主在创建时绑定到 Document 的完整注册项。
    /// </summary>
    internal PluginDocumentRegistration? GetDocumentRegistration(Document document) =>
        _documentPersistenceStates.TryGet(document, out var state)
            ? state.Registration
            : null;

    /// <summary>
    /// 按信封中的规范主 ID 查找注册项；此入口不会接受或归一化历史别名。
    /// </summary>
    internal bool TryGetPersistedDocumentRegistration(
        DocumentTypeId documentTypeId,
        out PluginDocumentRegistration registration) =>
        _extensions.TryGetDocumentRegistration(documentTypeId, out registration);

    /// <summary>
    /// 展开所有可见文档策略的创建入口。未实现多入口契约的旧策略自动生成一个默认入口。
    /// </summary>
    public IEnumerable<DocumentCreationMenuEntry> GetAllDocumentCreationEntries()
        => _extensions.GetCreationEntries();
    
    /// <summary>
    /// 根据参数创建Document
    /// </summary>
    /// <param name="params">创建参数</param>
    /// <returns>创建的Document实例</returns>
    public Document CreateManagementNewDocument(DocumentCreationParams @params)
    {
        var document = _extensions.CreateDocument(@params);
        // 策略违反“每次创建返回新实例”时，可能把已经由 Dock 持有的对象再次返回。
        // 记住它在本次调用前是否已由宿主登记，使后续重复登记失败时只拒绝
        // 新请求，不会误删原标签的路径与所有权，也不会误释放其 Scope。
        var wasAlreadyRegistered = _documentPersistenceStates.TryGet(document, out _);
        try
        {
            if (document is ISavableDocument)
            {
                var contributor = new HostCompositionContributor(
                    document.GetType().FullName ?? document.GetType().Name,
                    document.GetType().Assembly.GetName().Name ?? "UnknownAssembly");
                var canonicalTypeId = NormalizePersistedDocumentTypeId(@params.DocumentTypeId);
                if (!_extensions.TryGetDocumentRegistration(canonicalTypeId, out var registration))
                {
                    throw new HostCompositionException([
                        new HostCompositionDiagnostic(
                            "DOCUMENT_REGISTRATION_MISSING",
                            @params.DocumentTypeId.Value,
                            [contributor])
                    ]);
                }

                if (document is not IDocumentSaveState)
                {
                    throw new HostCompositionException([
                        new HostCompositionDiagnostic(
                            "DOCUMENT_SAVE_STATE_MISSING",
                            @params.DocumentTypeId.Value,
                            [contributor])
                    ]);
                }

                // G8 后插件不再通过 SaveDocumentTypeId 自报身份。宿主把创建请求已经解析出的
                // 规范 Registry 注册项绑定到实例，保存、关闭和路径查重只读取这份事实。
                _documentPersistenceStates.Register(document, registration);
            }

            return document;
        }
        catch
        {
            // 策略可能已经通过 IDocumentScopeFactory 创建了独立 Scope。契约校验位于创建后，
            // 因而新实例失败时必须从与正常关闭相同的入口回滚。若策略错误地
            // 返回已登记实例，该实例仍属于原 Dock，此次失败不得释放它。
            if (!wasAlreadyRegistered)
            {
                ReleaseDocument(document);
            }

            throw;
        }
    }

    /// <summary>
    /// 创建 Document，并在同一个所有权边界内将其发布到主文档 Dock。
    /// </summary>
    /// <param name="params">传递给文档创建策略的强类型参数。</param>
    /// <returns>已经成功加入主文档 Dock 的 Document。</returns>
    /// <remarks>
    /// 创建策略可能返回由 <see cref="DocumentScopeManager"/> 托管的 scoped Document。
    /// 在成功发布以前，该对象仍属于本方法；只要 Dock 不存在、拒绝添加，或在激活、聚焦
    /// 阶段抛出异常，本方法都会撤销可能产生的半提交 Dock 状态，并通过与正常关闭相同的
    /// 生命周期入口释放对象。只有 <see cref="PublishDocument"/> 完整返回后，所有权才转交
    /// 给 Dock，后续由 <see cref="OnDockableClosed"/> 负责释放。
    /// </remarks>
    internal Document CreateAndPublishDocument(DocumentCreationParams @params)
    {
        ArgumentNullException.ThrowIfNull(@params);

        Document? pendingDocument = CreateManagementNewDocument(@params);
        try
        {
            PublishDocument(pendingDocument);
            var publishedDocument = pendingDocument;
            pendingDocument = null;
            return publishedDocument;
        }
        finally
        {
            if (pendingDocument is not null)
            {
                ReleaseDocument(pendingDocument);
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
            _documentLifetime.Release(document);
        }
        finally
        {
            // 创建失败、加载失败和正常关闭都汇入此入口。即使插件 Dispose 抛出异常，
            // 宿主也必须删除路径与恢复登记，避免已结束对象继续影响重复打开判断。
            _documentPersistenceStates.Remove(document);
            _documentRecoveryRegistry?.Clear(document);
        }
    }

    private static bool ContainsDocument(
        DocumentDock documentDock,
        Document document) =>
        documentDock.VisibleDockables?.Any(candidate =>
            ReferenceEquals(candidate, document)) == true;

    public override IRootDock CreateLayout()
    {
        var untitledViewModel = new WelcomeViewModel(toolId => ShowTool(toolId))
        {
            Title = "欢迎",
        };
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
            [HostExtensionIds.PluginMenu.Value] = ()  => layout,
            [HostExtensionIds.FileSystemTree.Value] = () => layout,
            [HostExtensionIds.ToolManagement.Value] = () => layout,
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
        foreach (var toolTypeId in _extensions.ToolMetadata.Keys.Where(
                     id => id != HostExtensionIds.ToolManagement))
        {
            if (!_extensions.TryGetToolStrategy(toolTypeId, out var strategy))
            {
                continue;
            }

            var tool = strategy.CreateTool();
            tool.Id = toolTypeId.Value;
            _createdTools[toolTypeId.Value] = tool;
            // 设置特定工具的引用
            if (toolTypeId == HostExtensionIds.PluginMenu)
            {
                _plugGroupMenuTool = tool;
            }
        }
        
        // 再创建工具管理Tool（需要在其他Tool之后创建，因为它需要读取其他Tool的信息）
        if (_extensions.TryGetToolStrategy(
                HostExtensionIds.ToolManagement,
                out var managementStrategy))
        {
            var managementTool = managementStrategy.CreateTool();
            managementTool.Id = HostExtensionIds.ToolManagement.Value;
            _createdTools[managementTool.Id] = managementTool;
        }
    }

    /// <summary>
    /// 重写 OnDockableHidden：当工具被隐藏（如用户点击 X 关闭）时，
    /// 通知 ToolManagementViewModel 同步其 CheckBox 状态
    /// </summary>
    /// <remarks>
    /// 只发布宿主管理工具的变化，并使用构造函数注入的消息服务，
    /// 避免静态服务定位器带来的隐藏依赖。
    /// </remarks>
    public override void OnDockableHidden(IDockable? dockable)
    {
        base.OnDockableHidden(dockable);
        
        if (dockable == null) return;
        
        // 只对我们管理的工具发送通知（排除 Document 等其他 Dockable）
        if (dockable is Tool && _createdTools.Values.Contains(dockable))
        {
            try
            {
                _eventBus.Publish(
                    new ToolVisibilityChangedMessage("ToolHidden"));
            }
            catch
            {
                // 服务未初始化时忽略
            }
        }
    }

    /// <summary>
    /// 在 Dock 真正移除 Document 前执行公共脏状态保护。
    /// </summary>
    public override bool OnDockableClosing(IDockable? dockable)
    {
        if (dockable is Document document &&
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
}
