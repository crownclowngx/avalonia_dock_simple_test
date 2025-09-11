using System.Collections.ObjectModel;
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

    public FileSystemTreeViewModel()
    {
        Id = "fileSystemTree";
        Title = "文件系统";
        CanClose = false;
        InitializeTree();
    }

    private void InitializeTree()
    {
        // 添加系统驱动器作为根节点
        var drives = System.IO.Directory.GetLogicalDrives();
        foreach (var drive in drives)
        {
            RootNodes.Add(new FileSystemNode(drive));
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
}