using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Helpers;
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
            Id = "Files",
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
        
        // 根据对齐方式对Tool进行分组
        var leftTools = _createdTools.Values
            .Where(t => _toolMetadata.TryGetValue(t.Id, out var metadata) && 
                        metadata.Alignment.Equals("Left", StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        var rightTools = _createdTools.Values
            .Where(t => _toolMetadata.TryGetValue(t.Id, out var metadata) && 
                        metadata.Alignment.Equals("Right", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var toolsRight = new ProportionalDock
        {
            Proportion = 0.15,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>
            (
                new ToolDock
                {
                    ActiveDockable = rightTools.Find(k=>k.Id == "plugGroupMenuViewModel"),
                    VisibleDockables = CreateList<IDockable>
                    (
                        [.. rightTools]
                    ),
                    Alignment = Alignment.Right,
                    GripMode = GripMode.Visible
                }
            )
        };
        
        var toolsLeft = new ProportionalDock
        {
            Proportion = 0.15,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>
            (
                new ToolDock
                {
                    ActiveDockable = leftTools.Find(k=>k.Id=="fileSystemTree"),
                    VisibleDockables = CreateList<IDockable>
                    (
                        [.. leftTools]
                    ),
                    Alignment = Alignment.Left,
                    GripMode = GripMode.Visible
                }
            )
        };
        var windowLayout = CreateRootDock();
        windowLayout.Title = "Default";
        var windowLayoutContent = new ProportionalDock
        {
            Orientation = Orientation.Horizontal,
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>
            (
                toolsLeft,
                new ProportionalDockSplitter(),
                documentDock,
                new ProportionalDockSplitter(),
                toolsRight
            )
        };

        windowLayout.IsCollapsable = false;
        windowLayout.VisibleDockables = CreateList<IDockable>(windowLayoutContent);
        windowLayout.ActiveDockable = windowLayoutContent;

        var rootDock = CreateRootDock();

        rootDock.IsCollapsable = false;
        rootDock.VisibleDockables = CreateList<IDockable>(windowLayout);
        rootDock.ActiveDockable = windowLayout;
        rootDock.DefaultDockable = windowLayout;

        _documentDock = documentDock;
        _rootDock = rootDock;
        return rootDock;
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
                _documentScopeManager.Release(document);
            }
        }
    }
}
