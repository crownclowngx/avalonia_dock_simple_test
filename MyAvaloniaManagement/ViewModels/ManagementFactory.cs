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
using MyAvaloniaManagement.ViewModels.Hello;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;
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
    public ManagementFactory()
    {
        _strategies = new Dictionary<string, IDocumentCreationStrategy>();
        _toolStrategies = new Dictionary<string, IToolCreationStrategy>();
        _documentMetadata = new Dictionary<string, DocumentMetadata>();
        _toolMetadata = new Dictionary<string, ToolMetadata>();
        _createdTools = new Dictionary<string, Tool>();
        RegisterAllStrategiesAutomatically();
        RegisterAllToolStrategiesAutomatically();
    }
    
    public void SyncToolsVisibility()
    {
        var toolManagementViewModel = _createdTools[DockNameConstant.ToolManagement] as ToolManagementViewModel;
        toolManagementViewModel?.SyncToolsVisibility();
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
        
        // 扫描所有程序集中的Tool策略类型
        foreach (var assembly in assemblies)
        {
            try
            {
                var strategyTypes = assembly.GetTypes()
                    .Where(t => typeof(IToolCreationStrategy).IsAssignableFrom(t) && 
                                !t.IsAbstract && !t.IsInterface && 
                                t.GetConstructor(Type.EmptyTypes) != null);
                
                // 为每个策略类型创建实例并注册
                foreach (var strategyType in strategyTypes)
                {
                    var strategy = (IToolCreationStrategy)Activator.CreateInstance(strategyType);
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
        if (!_toolStrategies.ContainsKey(metadata.ToolTypeId))
        {
            _toolStrategies.Add(metadata.ToolTypeId, strategy);
            // 同时注册元数据
            if (!_toolMetadata.ContainsKey(metadata.ToolTypeId))
            {
                _toolMetadata.Add(metadata.ToolTypeId, metadata);
            }
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
        
        // 扫描所有程序集中的策略类型
        foreach (var assembly in assemblies)
        {
            try
            {
                var strategyTypes = assembly.GetTypes()
                    .Where(t => typeof(IDocumentCreationStrategy).IsAssignableFrom(t) && 
                                !t.IsAbstract && !t.IsInterface && 
                                t.GetConstructor(Type.EmptyTypes) != null);
                
                // 为每个策略类型创建实例并注册
                foreach (var strategyType in strategyTypes)
                {
                    var strategy = (IDocumentCreationStrategy)Activator.CreateInstance(strategyType);
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
        if (!_strategies.ContainsKey(strategy.GetMetadata().DocumentTypeId))
        {
            _strategies.Add(strategy.GetMetadata().DocumentTypeId, strategy);
            // 同时注册元数据
            var metadata = strategy.GetMetadata();
            if (!_documentMetadata.ContainsKey(metadata.DocumentTypeId))
            {
                _documentMetadata.Add(metadata.DocumentTypeId,metadata);
            }
        }
    }

    /// <summary>
    /// 根据参数创建Document
    /// </summary>
    /// <param name="params">创建参数</param>
    /// <returns>创建的Document实例</returns>
    public Document CreateManagementNewDocument(DocumentCreationParams @params)
    {
        if (@params == null)
        {
            throw new System.ArgumentNullException(nameof(@params));
        }

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
                        rightTools.ToArray()
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
                        leftTools.ToArray()
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
        };

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
        foreach (var strategy in _toolStrategies.Values.Where(k=>k.GetMetadata().ToolTypeId!=DockNameConstant.ToolManagement))
        {
            var tool = strategy.CreateTool();
            _createdTools[tool.Id] = tool;
            // 设置特定工具的引用
            if (tool.Id == "plugGroupMenuViewModel")
            {
                _plugGroupMenuTool = tool;
            }
        }
        var managementTool = _toolStrategies.Values.First(k => k.GetMetadata().ToolTypeId == DockNameConstant.ToolManagement).CreateTool();
        _createdTools[managementTool.Id] = managementTool;
    }


}