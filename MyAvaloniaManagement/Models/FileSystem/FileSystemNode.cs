using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MyAvaloniaManagement.Models.FileSystem;

public partial class FileSystemNode : ObservableObject
{
    public FileSystemNode(string path)
    {
        Path = path;
        // 对于驱动器路径，直接使用完整路径作为名称
        Name = IsDrivePath(path) ? path : (System.IO.Path.GetFileName(path) ?? path);
        IsDirectory = Directory.Exists(path);
        if (IsDirectory)
        {
            _children = new ObservableCollection<FileSystemNode>();
            // 延迟加载子节点
            _areChildrenLoaded = false;
        }
    }

    [ObservableProperty]
    private string _path;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isDirectory;

    [ObservableProperty]
    private bool _isExpanded;
    
    [ObservableProperty]
    private bool _isSelected;

    private ObservableCollection<FileSystemNode>? _children;
    public ObservableCollection<FileSystemNode> Children
    {
        get
        {
            if (!_areChildrenLoaded && IsDirectory)
            {
                LoadChildren();
            }
            return _children ?? new ObservableCollection<FileSystemNode>();
        }
    }

    private bool _areChildrenLoaded = true;

    private void LoadChildren()
    {
        if (!IsDirectory || _areChildrenLoaded)
            return;

        try
        {
            _children?.Clear();
            var directories = Directory.GetDirectories(Path);
            foreach (var dir in directories)
            {
                _children?.Add(new FileSystemNode(dir));
            }

            var files = Directory.GetFiles(Path);
            foreach (var file in files)
            {
                _children?.Add(new FileSystemNode(file));
            }
        }
        catch (System.UnauthorizedAccessException)
        {
            // 处理无权限访问的情况
        }
        catch (System.IO.DirectoryNotFoundException)
        {
            // 处理目录不存在的情况
        }
        
        _areChildrenLoaded = true;
    }
    // 添加刷新方法
    public void Refresh()
    {
        if (!IsDirectory)
            return;
            
        // 重置子节点加载状态，下次访问Children属性时会重新加载
        _areChildrenLoaded = false;
        // 触发PropertyChanged事件，通知UI刷新
        OnPropertyChanged(nameof(Children));
    }

    // 辅助方法：判断是否为驱动器路径
    private bool IsDrivePath(string path)
    {
        // 驱动器路径格式通常为 "C:\" 这样的形式
        return path.Length >= 3 && path[1] == ':' && path[2] == '\\' && (path.Length == 3 || path[3] == '\\');
    }
}