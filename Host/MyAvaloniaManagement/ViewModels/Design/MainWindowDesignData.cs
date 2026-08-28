using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.ViewModels.Bindings;
using MyAvaloniaManagement.Business.Presentation.Commands;

namespace MyAvaloniaManagement.ViewModels.Design;

/// <summary>供 Avalonia 设计器显示的纯内存主窗口样例。</summary>
/// <remarks>
/// 此对象不创建容器、不扫描插件、不读取布局文件，也不复用生产 ViewModel 的部分初始化
/// 构造。它完整实现 XAML 的窄绑定端口，因此设计预览与生产运行之间不存在隐藏的全局前置条件。
/// </remarks>
internal sealed class MainWindowDesignData : IMainWindowViewBindings
{
    public MainWindowDesignData()
    {
        Layout = CreateLayout();
        WorkbenchCommands = new WorkbenchCommandPresentationDesignData();
        DismissDocumentOperationErrorCommand = new RelayCommand(NoOperation);
        SetThemeCommand = new RelayCommand<string?>(_ => { });
    }

    public IRootDock Layout { get; }

    public string DocumentOperationError => "设计预览：文档操作提示显示在这里。";

    public bool HasDocumentOperationError => true;

    public bool IsSystemTheme => true;

    public bool IsLightTheme => false;

    public bool IsDarkTheme => false;

    public IWorkbenchCommandPresentationBindings WorkbenchCommands { get; }

    public IRelayCommand DismissDocumentOperationErrorCommand { get; }

    public IRelayCommand<string?> SetThemeCommand { get; }

    private static IRootDock CreateLayout()
    {
        var welcome = new Document
        {
            Id = "design-welcome",
            Title = "欢迎",
        };
        var documents = new DocumentDock
        {
            Id = "Documents",
            Title = "文档",
            VisibleDockables = new List<IDockable> { welcome },
            ActiveDockable = welcome,
        };
        var fileTool = new Tool
        {
            Id = "design-files",
            Title = "文件",
        };
        var tools = new ToolDock
        {
            Id = "LeftTools",
            Alignment = Alignment.Left,
            VisibleDockables = new List<IDockable> { fileTool },
            ActiveDockable = fileTool,
            Proportion = 0.22,
        };
        var workspace = new ProportionalDock
        {
            Id = "Workspace",
            Orientation = Orientation.Horizontal,
            VisibleDockables = new List<IDockable>
            {
                tools,
                new ProportionalDockSplitter(),
                documents,
            },
            ActiveDockable = documents,
        };
        return new RootDock
        {
            Id = "Root",
            VisibleDockables = new List<IDockable> { workspace },
            ActiveDockable = workspace,
            DefaultDockable = workspace,
        };
    }

    private static void NoOperation()
    {
    }
}
