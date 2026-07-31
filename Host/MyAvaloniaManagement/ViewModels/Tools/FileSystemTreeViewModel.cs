using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Message;
using MyAvaloniaManagement.Models.FileSystem;
using MyAvaloniaManagementCommon.Message;

namespace MyAvaloniaManagement.ViewModels.Tools;

/// <summary>
/// 为文件系统工具提供根节点、选择、刷新和打开文件行为。
/// </summary>
/// <remarks>
/// 文件夹选择和文件存在性检查通过 <see cref="IHostStorageService"/> 完成，
/// 打开动作通过消息发布，使该工具不需要知道主窗口或文档创建细节。
/// </remarks>
public partial class FileSystemTreeViewModel : Tool
{
    private readonly IHostStorageService _storageService;
    private readonly IMessengerService _messengerService;

    [ObservableProperty]
    private ObservableCollection<FileSystemNode> _rootNodes = [];

    [ObservableProperty]
    private string _selectedPath = string.Empty;

    [ObservableProperty]
    private FileSystemNode? _selectedNode;
    
    /// <summary>
    /// 获取或设置用户选择的自定义文件夹绝对路径。
    /// </summary>
    [ObservableProperty]
    private string _selectedFolderPath = string.Empty;

    /// <summary>
    /// 获取或设置当前是否只展示用户选择的自定义文件夹。
    /// </summary>
    [ObservableProperty]
    private bool _showCustomFolder = false;

    /// <summary>
    /// 使用可替换的存储和消息服务创建文件树工具。
    /// </summary>
    /// <param name="storageService">文件夹选择和文件存在性服务。</param>
    /// <param name="messengerService">向主窗体发布打开文件消息的服务。</param>
    /// <param name="initializeTree">是否立即枚举系统驱动器；测试可关闭以避免依赖运行机器。</param>
    internal FileSystemTreeViewModel(
        IHostStorageService storageService,
        IMessengerService messengerService,
        bool initializeTree = true)
    {
        _storageService = storageService;
        _messengerService = messengerService;
        Id = "fileSystemTree";
        Title = "文件系统";
        CanClose = true;
        if (initializeTree)
        {
            InitializeTree();
        }
    }

    /// <summary>
    /// 使用应用全局服务创建实例，供设计器及兼容路径使用。
    /// </summary>
    public FileSystemTreeViewModel() : this(
        ServiceProvider.GetRequiredService<IHostStorageService>(),
        ServiceProvider.GetRequiredService<IMessengerService>())
    {
    }

    /// <summary>
    /// 初始化驱动器根节点；传入驱动器路径时只加载该驱动器。
    /// </summary>
    private void InitializeTree(string folderPath = "")
    {
        // 添加系统驱动器作为根节点
        var drives = System.IO.Directory.GetLogicalDrives();
        foreach (var drive in drives)
        {
            if(string.IsNullOrEmpty(folderPath) || drive == folderPath)
            {
                RootNodes.Add(new FileSystemNode(drive));
            }
        }
    }

    /// <summary>
    /// 展开指定文件系统节点，由节点自身执行延迟加载。
    /// </summary>
    [RelayCommand]
    public static void ExpandNode(FileSystemNode node)
    {
        node.IsExpanded = true;
    }

    /// <summary>
    /// 折叠指定文件系统节点。
    /// </summary>
    [RelayCommand]
    public static void CollapseNode(FileSystemNode node)
    {
        node.IsExpanded = false;
    }

    /// <summary>
    /// 同步当前选择节点及其路径。
    /// </summary>
    [RelayCommand]
    public void NodeSelected(FileSystemNode node)
    {
        SelectedNode = node;
        SelectedPath = node.Path;
    }
    
    /// <summary>
    /// 刷新当前选中节点的子项。
    /// </summary>
    [RelayCommand]
    public void RefreshNode()
    {
        SelectedNode?.Refresh();
    }

    /// <summary>
    /// 重新枚举所有驱动器根节点。
    /// </summary>
    [RelayCommand]
    public void RefreshAll()
    {
        RootNodes.Clear();
        InitializeTree();
    }
    
    /// <summary>
    /// 当选中项是现有文件时，向主窗口发送打开请求。
    /// </summary>
    [RelayCommand]
    public void OpenFile()
    {
        if (SelectedNode != null && _storageService.FileExists(SelectedNode.Path))
        {
            _messengerService.Send(new OpenFileMessage(SelectedNode.Path));
        }
    }
    
    /// <summary>
    /// 选择一个根目录，并根据驱动器根路径或普通目录更新展示模式。
    /// </summary>
    /// <remarks>
    /// 驱动器根继续沿用系统驱动器模式；普通目录只显示该目录，
    /// 避免在用户明确选择目录后仍枚举无关驱动器。
    /// </remarks>
    [RelayCommand]
    public async Task SelectFolder()
    {
        var folderPath = await _storageService.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            if (FileHelper.IsDrivePath(folderPath))
            {
                SelectedFolderPath = string.Empty;
                ShowCustomFolder = false;
                RootNodes.Clear();
                InitializeTree(folderPath);
            }
            else
            {
                SelectedFolderPath = Path.GetFullPath(folderPath);
                ShowCustomFolder = true;
            
                // 刷新根节点，添加自定义选择的文件夹
                RootNodes.Clear();
            
                // 添加选择的文件夹作为根节点
                if (Directory.Exists(SelectedFolderPath))
                {
                    RootNodes.Add(new FileSystemNode(SelectedFolderPath));
                }
            }

        }
    }
}
