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
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Message;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.ViewModels.Hello;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.ViewModels;

/// <summary>
/// 负责发现文档/工具策略、创建 Dock 布局并协调工具与文档生命周期。
/// </summary>
/// <remarks>
/// 工厂是宿主 Dock 状态的唯一协调者。它持有稳定 ID 到策略、元数据和实例的映射，
/// 使插件扩展、布局恢复及工具显隐都围绕同一份注册结果工作。
/// </remarks>
public class ManagementFactory : Factory
{
    private readonly HostExtensionRegistry _extensions;
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
    private readonly IMessengerService _messengerService;

    internal IReadOnlyDictionary<string, Tool> CreatedTools => _createdTools;

    internal Alignment GetToolAlignment(string toolId) =>
        ToolDockPlacement.ParseAlignment(
            _extensions.ToolMetadata.TryGetValue(toolId, out var metadata)
                ? metadata.Alignment
                : null);

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
    public ManagementFactory(
        IServiceProvider serviceProvider,
        PluginModuleCatalog pluginModuleCatalog,
        DocumentScopeManager documentScopeManager,
        IMessengerService messengerService)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(pluginModuleCatalog);
        ArgumentNullException.ThrowIfNull(documentScopeManager);
        _documentLifetime = new DockDocumentLifetime(documentScopeManager);
        _workspaceBuilder = new DockWorkspaceBuilder(this);
        _messengerService = messengerService;
        _toolDockCoordinator = new ToolDockCoordinator(
            this,
            _workspaceBuilder,
            GetToolAlignment,
            messengerService);
        _extensions = new HostExtensionRegistry(
            serviceProvider,
            pluginModuleCatalog);
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
    /// 注册新的Tool策略
    /// </summary>
    /// <param name="strategy">Tool策略实例</param>
    public void RegisterToolStrategy(IToolCreationStrategy strategy)
        => _extensions.RegisterToolStrategy(strategy);

    /// <summary>
    /// 获取所有文档类型的元数据
    /// </summary>
    /// <returns>所有文档类型的元数据列表</returns>
    public IEnumerable<DocumentMetadata> GetAllDocumentMetadata()
        => _extensions.DocumentMetadata.Values;

    /// <summary>
    /// 展开所有可见文档策略的创建入口。未实现多入口契约的旧策略自动生成一个默认入口。
    /// </summary>
    public IEnumerable<DocumentCreationMenuEntry> GetAllDocumentCreationEntries()
        => _extensions.GetCreationEntries();
    
    /// <summary>
    /// 注册新的策略
    /// </summary>
    /// <param name="strategy">策略实例</param>
    public void RegisterStrategy(IDocumentCreationStrategy strategy)
        => _extensions.RegisterDocumentStrategy(strategy);

    /// <summary>
    /// 根据参数创建Document
    /// </summary>
    /// <param name="params">创建参数</param>
    /// <returns>创建的Document实例</returns>
    public Document CreateManagementNewDocument(DocumentCreationParams @params)
        => _extensions.CreateDocument(@params);

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
            [DockNameConstant.PlugGroupMenu] = ()  => layout,
            ["fileSystemTree"] = () => layout,
            ["toolManagement"] = () => layout,
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
                     id => id != DockNameConstant.ToolManagement))
        {
            if (!_extensions.TryGetToolStrategy(toolTypeId, out var strategy))
            {
                continue;
            }

            var tool = strategy.CreateTool();
            _createdTools[tool.Id] = tool;
            // 设置特定工具的引用
            if (tool.Id == DockNameConstant.PlugGroupMenu)
            {
                _plugGroupMenuTool = tool;
            }
        }
        
        // 再创建工具管理Tool（需要在其他Tool之后创建，因为它需要读取其他Tool的信息）
        if (_extensions.TryGetToolStrategy(
                DockNameConstant.ToolManagement,
                out var managementStrategy))
        {
            var managementTool = managementStrategy.CreateTool();
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
                _messengerService.Send(
                    new ToolVisibilityChangedMessage("ToolHidden"));
            }
            catch
            {
                // 服务未初始化时忽略
            }
        }
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
                _documentLifetime.Release(document);
            }
        }
    }
}
