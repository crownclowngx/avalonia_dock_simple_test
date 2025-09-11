using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Models.DocumentCreation;
using MyAvaloniaManagement.ViewModels.Hello;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagement.ViewModels;

public class ManagementFactory : Factory
{
    private readonly Dictionary<string, IDocumentCreationStrategy> _strategies;
    private IRootDock? _rootDock;
    private DocumentDock? _documentDock;
    private ITool?  _plugGroupMenuTool;
    
    // 存储文档类型元数据
    private readonly Dictionary<string, DocumentMetadata> _documentMetadata;
    
    public ManagementFactory()
    {
        _strategies = new Dictionary<string, IDocumentCreationStrategy>();
        _documentMetadata = new Dictionary<string, DocumentMetadata>();
        RegisterAllStrategiesAutomatically();
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
        var plugGroupMenuViewModel = new PlugGroupMenuViewModel()
        {
            Id = "plugGroupMenuViewModel",
            Title = "插件工具",
            CanClose = false,
        };
        
        var fileSystemTreeViewModel = new FileSystemTreeViewModel()
        {
            Id = "fileSystemTree",
            Title = "文件系统",
            CanClose = false,
        };

        var tools = new ProportionalDock
        {
            Proportion = 0.2,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>
            (
                new ToolDock
                {
                    ActiveDockable = plugGroupMenuViewModel,
                    VisibleDockables = CreateList<IDockable>
                    (
                        plugGroupMenuViewModel,
                        fileSystemTreeViewModel
                    ),
                    Alignment = Alignment.Right,
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
                documentDock,
                new ProportionalDockSplitter(),
                tools
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
        _plugGroupMenuTool = plugGroupMenuViewModel;
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
}