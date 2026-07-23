using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DaTangAccountingHelpPlug.Models;

public partial class FileSystemNode : ObservableObject
{
    public FileSystemNode(string path)
    {
        Path = path;
        // 对于驱动器路径，直接使用完整路径作为名称
        Name = IsDrivePath(path) ? path : (System.IO.Path.GetFileName(path) ?? path);
        if (string.IsNullOrEmpty(Name))
        {
            Name = path;
        }

        IsDirectory = Directory.Exists(path);
        if (IsDirectory)
        {
            _children = new ObservableCollection<FileSystemNode>();
            // 延迟加载子节点
            _areChildrenLoaded = false;
        }
    }

    [ObservableProperty] private string _path;

    [ObservableProperty] private string _name;

    [ObservableProperty] private bool _isDirectory;

    [ObservableProperty] private bool _isExpanded;

    [ObservableProperty] private bool _isSelected;

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

    // 判断是否为驱动器路径
    private bool IsDrivePath(string path)
    {
        try
        {
            // 检查是否为驱动器根目录（如 C:\、D:\）
            if (path.Length >= 3 && path[1] == ':' && path[2] == '\\' &&
                (path.Length == 3 || (path.Length > 3 && path[3] == '\\')))
            {
                return true;
            }

            // 检查是否为 UNC 路径
            if (path.StartsWith("\\"))
            {
                return true;
            }

            // 对于其他情况，检查路径的根目录是否等于其自身
            DirectoryInfo dirInfo = new DirectoryInfo(path);
            return dirInfo.Root.FullName.Equals(path, System.StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // 如果出现异常，默认不视为驱动器路径
            return false;
        }
    }
}