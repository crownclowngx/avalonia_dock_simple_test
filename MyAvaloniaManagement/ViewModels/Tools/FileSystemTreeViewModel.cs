using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Message;
using MyAvaloniaManagement.Models.FileSystem;

namespace MyAvaloniaManagement.ViewModels.Tools;

public partial class FileSystemTreeViewModel : Tool
{
    [ObservableProperty]
    private ObservableCollection<FileSystemNode> _rootNodes = new();

    [ObservableProperty]
    private string _selectedPath = string.Empty;

    [ObservableProperty]
    private FileSystemNode? _selectedNode;
    
    // 添加选择的文件夹路径属性
    [ObservableProperty]
    private string _selectedFolderPath = string.Empty;

    // 添加是否显示自定义文件夹的标志
    [ObservableProperty]
    private bool _showCustomFolder = false;

    public FileSystemTreeViewModel()
    {
        Id = "fileSystemTree";
        Title = "文件系统";
        CanClose = false;
        InitializeTree();
    }

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

    [RelayCommand]
    public void ExpandNode(FileSystemNode node)
    {
        node.IsExpanded = true;
    }

    [RelayCommand]
    public void CollapseNode(FileSystemNode node)
    {
        node.IsExpanded = false;
    }

    [RelayCommand]
    public void NodeSelected(FileSystemNode node)
    {
        SelectedNode = node;
        SelectedPath = node.Path;
    }
    
    // 添加刷新选中节点命令
    [RelayCommand]
    public void RefreshNode()
    {
        if (SelectedNode != null)
        {
            SelectedNode.Refresh();
        }
    }

    // 添加刷新全部命令
    [RelayCommand]
    public void RefreshAll()
    {
        RootNodes.Clear();
        InitializeTree();
    }
    
    // 添加打开文件命令
    [RelayCommand]
    public void OpenFile()
    {
        if (SelectedNode != null && System.IO.File.Exists(SelectedNode.Path))
        {
            // 通过消息总线发送打开文件的请求
            if (AppServices.Instance.MessengerServiceDefault != null)
            {
                AppServices.Instance.MessengerServiceDefault.Send(new OpenFileMessage(SelectedNode.Path));
            }
        }
    }
    
    // 添加选择文件夹命令
    [RelayCommand]
    public async void SelectFolder()
    {
        // 使用正确的方式获取主窗口
        var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (mainWindow == null) return;

        var options = new FolderPickerOpenOptions
        {
            Title = "选择文件夹",
            AllowMultiple = false
        };

        var result = await mainWindow.StorageProvider.OpenFolderPickerAsync(options);
        if (result != null && result.Count > 0)
        {
            var selectedFolder = result[0];
            string folderPath = selectedFolder.TryGetLocalPath() ?? string.Empty;
            if (FileHelper.IsDrivePath(folderPath))
            {
                RootNodes.Clear();
                InitializeTree(folderPath);
            }
            else
            {
                SelectedFolderPath = Path.GetFullPath(selectedFolder.Path.LocalPath);
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