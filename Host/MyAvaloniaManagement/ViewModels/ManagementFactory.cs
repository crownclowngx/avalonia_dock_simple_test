using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

public class ManagementFactory : Factory
{
    private readonly Dictionary<string, IDocumentCreationStrategy> _strategies;
    private readonly Dictionary<string, IToolCreationStrategy> _toolStrategies;
    private IRootDock? _rootDock;
    private DocumentDock? _documentDock;
    private ITool?  _plugGroupMenuTool;
    
    // 存储文档类型元数据
    private readonly Dictionary<string, DocumentMetadata> _documentMetadata;
    
    // 存储Tool类型元数据
    private readonly Dictionary<string, ToolMetadata> _toolMetadata;
    
    // 存储已创建的Tool实例
    private readonly Dictionary<string, Tool> _createdTools;
    private readonly IServiceProvider _serviceProvider;
    private readonly PluginModuleCatalog _pluginModuleCatalog;
    private readonly DocumentScopeManager _documentScopeManager;

    internal IReadOnlyDictionary<string, Tool> CreatedTools => _createdTools;

    internal Alignment GetToolAlignment(string toolId) =>
        ToolDockPlacement.ParseAlignment(
            _toolMetadata.TryGetValue(toolId, out var metadata)
                ? metadata.Alignment
                : null);

    public ManagementFactory(
        IServiceProvider serviceProvider,
        PluginModuleCatalog pluginModuleCatalog,
        DocumentScopeManager documentScopeManager)
    {
        _serviceProvider = serviceProvider;
        _pluginModuleCatalog = pluginModuleCatalog;
        _documentScopeManager = documentScopeManager;
        _strategies = [];
        _toolStrategies = [];
        _documentMetadata = [];
        _toolMetadata = [];
        _createdTools = [];
        // 启用 HideToolsOnClose：关闭工具时移入 HiddenDockables 而非真正移除，
        // 这样可以后续通过 RestoreDockable 恢复
        HideToolsOnClose = true;
        RegisterAllStrategiesAutomatically();
        RegisterAllToolStrategiesAutomatically();
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
            ToolMetadata = _toolMetadata,
            CreatedTools = _createdTools,
            RootDock = _rootDock
        };
    }
    
    
    
    /// <summary>
    /// 自动注册所有程序集中实现了 IToolCreationStrategy 接口的非抽象类
    /// 包括主程序集和特定子目录中的程序集
    /// </summary>
    private void RegisterAllToolStrategiesAutomatically()
    {
        // 获取当前程序集
        var currentAssembly = Assembly.GetExecutingAssembly();
        var assemblies = new List<Assembly> { currentAssembly };
        
        Console.WriteLine("开始加载和注册Tool创建策略...");
            
        // 从特定子目录加载其他程序集
        var pluginAssemblies = AssemblyLoaderHelper.LoadPluginsFromDirectories(AssemblyLoadConstant.PLUGINS_SUBDIRECTORY);
        assemblies.AddRange(pluginAssemblies);
        // 集成宿主可直接把已发现模块程序集交给 Catalog，而不必复制到生产插件目录。
        // 生产启动仍会得到相同集合；Distinct 避免同一程序集被扫描两次。
        assemblies.AddRange(_pluginModuleCatalog.Modules.Select(module => module.GetType().Assembly));
        assemblies = assemblies.Distinct().ToList();
        
        // 扫描所有程序集中的Tool策略类型
        foreach (var assembly in assemblies)
        {
            try
            {
                var managed = _pluginModuleCatalog.IsManaged(assembly);
                var strategyTypes = assembly.GetTypes()
                    .Where(t => typeof(IToolCreationStrategy).IsAssignableFrom(t) &&
                                !t.IsAbstract && !t.IsInterface &&
                                (managed || t.GetConstructor(Type.EmptyTypes) != null));
                
                // 为每个策略类型创建实例并注册
                foreach (var strategyType in strategyTypes)
                {
                    var strategy = PluginStrategyActivator.Create<IToolCreationStrategy>(
                        strategyType,
                        assembly,
                        _serviceProvider,
                        _pluginModuleCatalog);
                    RegisterToolStrategy(strategy);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"扫描程序集 {assembly.FullName} 中的Tool策略时出错: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// 注册新的Tool策略
    /// </summary>
    /// <param name="strategy">Tool策略实例</param>
    public void RegisterToolStrategy(IToolCreationStrategy strategy)
    {
        var metadata = strategy.GetMetadata();
        if (_toolStrategies.TryAdd(metadata.ToolTypeId, strategy))
        {
            // 同时注册元数据
            _toolMetadata.TryAdd(metadata.ToolTypeId, metadata);
        }
    }
    
    /// <summary>
    /// 自动注册所有程序集中实现了 IDocumentCreationStrategy 接口的非抽象类
    /// 包括主程序集和特定子目录中的程序集
    /// </summary>
    private void RegisterAllStrategiesAutomatically()
    {
        // 获取当前程序集
        var currentAssembly = Assembly.GetExecutingAssembly();
        var assemblies = new List<Assembly> { currentAssembly };
        
        Console.WriteLine("开始加载和注册文档创建策略...");
        Console.WriteLine($"当前程序集: {currentAssembly.FullName}");
            
        // 从特定子目录加载其他程序集
        Console.WriteLine($"尝试从目录 '{AssemblyLoadConstant.PLUGINS_SUBDIRECTORY}' 加载插件...");
        // 从特定子目录加载其他程序集
        var pluginAssemblies = AssemblyLoaderHelper.LoadPluginsFromDirectories(AssemblyLoadConstant.PLUGINS_SUBDIRECTORY);
        assemblies.AddRange(pluginAssemblies);
        assemblies.AddRange(_pluginModuleCatalog.Modules.Select(module => module.GetType().Assembly));
        assemblies = assemblies.Distinct().ToList();
        
        // 扫描所有程序集中的策略类型
        foreach (var assembly in assemblies)
        {
            try
            {
                var managed = _pluginModuleCatalog.IsManaged(assembly);
                var strategyTypes = assembly.GetTypes()
                    .Where(t => typeof(IDocumentCreationStrategy).IsAssignableFrom(t) &&
                                !t.IsAbstract && !t.IsInterface &&
                                (managed || t.GetConstructor(Type.EmptyTypes) != null));
                
                // 为每个策略类型创建实例并注册
                foreach (var strategyType in strategyTypes)
                {
                    var strategy = PluginStrategyActivator.Create<IDocumentCreationStrategy>(
                        strategyType,
                        assembly,
                        _serviceProvider,
                        _pluginModuleCatalog);
                    RegisterStrategy(strategy);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"扫描程序集 {assembly.FullName} 中的策略时出错: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 获取所有文档类型的元数据
    /// </summary>
    /// <returns>所有文档类型的元数据列表</returns>
    public IEnumerable<DocumentMetadata> GetAllDocumentMetadata()
    {
        return _documentMetadata.Values;
    }
    
    /// <summary>
    /// 注册新的策略
    /// </summary>
    /// <param name="strategy">策略实例</param>
    public void RegisterStrategy(IDocumentCreationStrategy strategy)
    {
        if (_strategies.TryAdd(strategy.GetMetadata().DocumentTypeId, strategy))
        {
            // 同时注册元数据
            var metadata = strategy.GetMetadata();
            _documentMetadata.TryAdd(metadata.DocumentTypeId, metadata);
        }
    }

    /// <summary>
    /// 根据参数创建Document
    /// </summary>
    /// <param name="params">创建参数</param>
    /// <returns>创建的Document实例</returns>
    public Document CreateManagementNewDocument(DocumentCreationParams @params)
    {
        ArgumentNullException.ThrowIfNull(@params);

        if (_strategies.TryGetValue(@params.DocumentType, out var strategy))
        {
            return strategy.CreateDocument(@params);
        }

        throw new System.NotSupportedException($"不支持的Document类型: {@params.DocumentType}");
    }

    public override IRootDock CreateLayout()
    {
        var untitledViewModel = new WelcomeViewModel()
        {
            Title = "欢迎",
            Text = "欢迎使用MyAvaloniaManagement",
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
        ArgumentNullException.ThrowIfNull(documentDock);

        // 根据对齐方式对Tool进行分组
        var toolsByAlignment = _createdTools.Values
            .ToLookup(tool => GetToolAlignment(tool.Id));
        var leftTools = toolsByAlignment[Alignment.Left].ToList();
        var rightTools = toolsByAlignment[Alignment.Right].ToList();
        var topTools = toolsByAlignment[Alignment.Top].ToList();
        var bottomTools = toolsByAlignment[Alignment.Bottom].ToList();

        var toolsLeft = CreateToolPane(
            DockLayoutIds.LeftPane,
            DockLayoutIds.LeftTools,
            Alignment.Left,
            leftTools,
            0.15);
        var toolsRight = CreateToolPane(
            DockLayoutIds.RightPane,
            DockLayoutIds.RightTools,
            Alignment.Right,
            rightTools,
            0.15);

        var centerRowsDockables = new List<IDockable>();
        if (topTools.Count > 0)
        {
            centerRowsDockables.Add(CreateToolPane(
                DockLayoutIds.TopPane,
                DockLayoutIds.TopTools,
                Alignment.Top,
                topTools,
                0.20));
            centerRowsDockables.Add(new ProportionalDockSplitter());
        }

        centerRowsDockables.Add(documentDock);

        if (bottomTools.Count > 0)
        {
            centerRowsDockables.Add(new ProportionalDockSplitter());
            centerRowsDockables.Add(CreateToolPane(
                DockLayoutIds.BottomPane,
                DockLayoutIds.BottomTools,
                Alignment.Bottom,
                bottomTools,
                0.20));
        }

        var workspaceCenterRows = new ProportionalDock
        {
            Id = DockLayoutIds.WorkspaceCenterRows,
            Orientation = Orientation.Vertical,
            IsCollapsable = false,
            Proportion = double.NaN,
            VisibleDockables = CreateList<IDockable>([.. centerRowsDockables]),
            ActiveDockable = documentDock
        };
        var windowLayout = CreateRootDock();
        windowLayout.Id = DockLayoutIds.Workspace;
        windowLayout.Title = "Default";
        DisableFloating(windowLayout);
        var windowLayoutContent = new ProportionalDock
        {
            Id = DockLayoutIds.WorkspaceColumns,
            Orientation = Orientation.Horizontal,
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>
            (
                toolsLeft,
                new ProportionalDockSplitter(),
                workspaceCenterRows,
                new ProportionalDockSplitter(),
                toolsRight
            )
        };

        windowLayout.IsCollapsable = false;
        windowLayout.VisibleDockables = CreateList<IDockable>(windowLayoutContent);
        windowLayout.ActiveDockable = windowLayoutContent;

        var rootDock = CreateRootDock();
        rootDock.Id = DockLayoutIds.Root;
        DisableFloating(rootDock);

        rootDock.IsCollapsable = false;
        rootDock.VisibleDockables = CreateList<IDockable>(windowLayout);
        rootDock.ActiveDockable = windowLayout;
        rootDock.DefaultDockable = windowLayout;

        _documentDock = documentDock;
        _rootDock = rootDock;
        return rootDock;
    }

    private ProportionalDock CreateToolPane(
        string paneId,
        string toolDockId,
        Alignment alignment,
        IReadOnlyList<Tool> tools,
        double proportion)
    {
        var toolDock = CreateStableToolDock(toolDockId, alignment, tools);

        return new ProportionalDock
        {
            Id = paneId,
            Proportion = proportion,
            CollapsedProportion = proportion,
            Orientation = Orientation.Vertical,
            IsCollapsable = true,
            VisibleDockables = CreateList<IDockable>(toolDock),
            ActiveDockable = toolDock
        };
    }

    private ToolDock CreateStableToolDock(
        string toolDockId,
        Alignment alignment,
        IReadOnlyList<Tool>? tools = null) =>
        new()
        {
            Id = toolDockId,
            ActiveDockable = tools?.FirstOrDefault(),
            VisibleDockables = tools is null
                ? CreateList<IDockable>()
                : CreateList<IDockable>([.. tools]),
            Alignment = ToolDockPlacement.NormalizeAlignment(alignment),
            GripMode = GripMode.Visible,
            IsCollapsable = true
        };

    /// <summary>
    /// Dock 在最后一个 Tool 隐藏时会移除空 ToolDock。恢复工具前重建稳定停靠点，
    /// 避免 OriginalOwner 指向已经脱离主布局的 Dock。
    /// </summary>
    internal ToolDock EnsureToolDock(
        IRootDock root,
        Alignment alignment)
    {
        ArgumentNullException.ThrowIfNull(root);
        alignment = ToolDockPlacement.NormalizeAlignment(alignment);
        var toolDockId = ToolDockPlacement.GetDockId(alignment);
        if (FindDockById<ToolDock>(root, toolDockId) is { } existingDock)
        {
            return existingDock;
        }

        var paneId = ToolDockPlacement.GetPaneId(alignment);
        var pane = FindDockById<ProportionalDock>(root, paneId);
        if (pane is null)
        {
            pane = CreateToolPane(
                paneId,
                toolDockId,
                alignment,
                [],
                ToolDockPlacement.GetDefaultProportion(alignment));
            InsertMissingPane(root, pane, alignment);
            return (ToolDock)pane.VisibleDockables![0];
        }

        var toolDock = CreateStableToolDock(toolDockId, alignment);
        AddDockable(pane, toolDock);
        pane.ActiveDockable = toolDock;
        return toolDock;
    }

    /// <summary>
    /// 优先恢复仍附着在主布局中的原 Owner；原 Owner 已被折叠移除时，
    /// 根据它的方向重建稳定 ToolDock 并通过 Factory API 加回工具。
    /// </summary>
    internal bool RestoreTool(
        IRootDock root,
        Tool tool)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(tool);

        var originalToolDock = tool.OriginalOwner as IToolDock;
        if (originalToolDock is IDock attachedOriginalOwner &&
            IsDockAttached(root, attachedOriginalOwner))
        {
            RestoreDockable(tool);
            if (IsDockableAttached(root, tool))
            {
                SetActiveDockable(tool);
                return true;
            }
        }

        var alignment = originalToolDock is null
            ? GetToolAlignment(tool.Id)
            : ToolDockPlacement.NormalizeAlignment(originalToolDock.Alignment);
        var targetDock = EnsureToolDock(root, alignment);

        RemoveFromHiddenDockables(root, tool);
        if (FindRoot(tool, _ => true) is { HiddenDockables: { } hidden })
        {
            hidden.Remove(tool);
        }

        tool.OriginalOwner = null;
        AddDockable(targetDock, tool);
        SetActiveDockable(tool);
        return true;
    }

    private void InsertMissingPane(
        IRootDock root,
        ProportionalDock pane,
        Alignment alignment)
    {
        if (alignment is not (Alignment.Top or Alignment.Bottom))
        {
            throw new InvalidOperationException(
                $"稳定停靠区域 '{pane.Id}' 已脱离主布局。");
        }

        var centerRows = FindDockById<ProportionalDock>(
            root,
            DockLayoutIds.WorkspaceCenterRows)
            ?? throw new InvalidOperationException(
                $"Dock '{DockLayoutIds.WorkspaceCenterRows}' was not found.");
        var documentIndex = centerRows.VisibleDockables?
            .ToList()
            .FindIndex(dockable => dockable.Id == DockLayoutIds.Documents) ?? -1;
        if (documentIndex < 0)
        {
            throw new InvalidOperationException(
                $"Dock '{DockLayoutIds.Documents}' was not found.");
        }

        var splitter = new ProportionalDockSplitter();
        if (alignment == Alignment.Top)
        {
            InsertDockable(centerRows, pane, documentIndex);
            InsertDockable(centerRows, splitter, documentIndex + 1);
        }
        else
        {
            InsertDockable(centerRows, splitter, documentIndex + 1);
            InsertDockable(centerRows, pane, documentIndex + 2);
        }
    }

    private static T? FindDockById<T>(
        IDock root,
        string id)
        where T : class, IDock
    {
        if (root is T typed && root.Id == id)
        {
            return typed;
        }

        if (root.VisibleDockables is null)
        {
            return null;
        }

        foreach (var child in root.VisibleDockables.OfType<IDock>())
        {
            if (FindDockById<T>(child, id) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    private static bool IsDockAttached(
        IDock root,
        IDock target)
    {
        if (ReferenceEquals(root, target))
        {
            return true;
        }

        return root.VisibleDockables?
            .OfType<IDock>()
            .Any(child => IsDockAttached(child, target)) == true;
    }

    private static bool IsDockableAttached(
        IDock root,
        IDockable target) =>
        root.VisibleDockables?.Any(dockable =>
            ReferenceEquals(dockable, target) ||
            dockable is IDock childDock &&
            IsDockableAttached(childDock, target)) == true;

    private static void RemoveFromHiddenDockables(
        IDock root,
        IDockable tool)
    {
        if (root is IRootDock { HiddenDockables: { } hidden })
        {
            hidden.Remove(tool);
        }

        if (root.VisibleDockables is null)
        {
            return;
        }

        foreach (var child in root.VisibleDockables.OfType<IDock>())
        {
            RemoveFromHiddenDockables(child, tool);
        }
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

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            ["plugGroupMenuViewModel"] = ()  => layout,
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
    /// 创建所有注册的Tool实例
    /// </summary>
    private void CreateAllTools()
    {
        // 先创建所有非工具管理的Tool
        foreach (var strategy in _toolStrategies.Values.Where(k => k.GetMetadata().ToolTypeId != DockNameConstant.ToolManagement))
        {
            var tool = strategy.CreateTool();
            _createdTools[tool.Id] = tool;
            // 设置特定工具的引用
            if (tool.Id == "plugGroupMenuViewModel")
            {
                _plugGroupMenuTool = tool;
            }
        }
        
        // 再创建工具管理Tool（需要在其他Tool之后创建，因为它需要读取其他Tool的信息）
        if (_toolStrategies.TryGetValue(DockNameConstant.ToolManagement, out var managementStrategy))
        {
            var managementTool = managementStrategy.CreateTool();
            _createdTools[managementTool.Id] = managementTool;
        }
    }

    /// <summary>
    /// 重写 OnDockableHidden：当工具被隐藏（如用户点击 X 关闭）时，
    /// 通知 ToolManagementViewModel 同步其 CheckBox 状态
    /// </summary>
    public override void OnDockableHidden(IDockable? dockable)
    {
        base.OnDockableHidden(dockable);
        
        if (dockable == null) return;
        
        // 只对我们管理的工具发送通知（排除 Document 等其他 Dockable）
        if (dockable is Tool && _createdTools.Values.Contains(dockable))
        {
            try
            {
                Business.Helpers.ServiceProvider.GetService<IMessengerService>()
                    ?.Send(new ToolVisibilityChangedMessage("ToolHidden"));
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
                if (Application.Current?.Resources["ControlRecyclingKey"]
                    is DocumentControlRecycling recycling)
                {
                    recycling.Remove(document);
                }

                _documentScopeManager.Release(document);
            }
        }
    }
}
