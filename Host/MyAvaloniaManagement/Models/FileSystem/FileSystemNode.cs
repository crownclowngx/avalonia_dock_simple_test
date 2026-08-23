using System.Collections.ObjectModel;
using System.IO;
using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MyAvaloniaManagement.Models.FileSystem;

internal sealed partial class FileSystemNode : ObservableObject
{
    public FileSystemNode(string path)
    {
        Path = path;
        // 本地驱动器根和 UNC 共享根没有可用的末级目录名，直接展示规范路径。
        Name = FileSystemPath.TryNormalize(path, out var normalized) &&
               normalized.Kind is FileSystemPathKind.LocalDriveRoot or
                   FileSystemPathKind.UncShareRoot
            ? normalized.NormalizedPath
            : (System.IO.Path.GetFileName(path) ?? path);
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

    /// <summary>创建不访问文件系统的设计时节点树。</summary>
    /// <remarks>
    /// 该入口为 internal，且显式接收已经确定的名称、目录标记和子节点。设计器因此不会因
    /// 预览一个 View 而读取开发机器目录；生产路径仍使用基于真实路径的公开构造函数。
    /// </remarks>
    internal static FileSystemNode CreateDesignSample(
        string path,
        string name,
        bool isDirectory,
        params FileSystemNode[] children)
    {
        var node = new FileSystemNode(path, name, isDirectory);
        node._children = new ObservableCollection<FileSystemNode>(children);
        node._areChildrenLoaded = true;
        return node;
    }

    /// <summary>
    /// 使用已由存储端口确认存在的规范路径创建目录根节点。
    /// </summary>
    /// <remarks>
    /// 该入口不重复访问真实 UNC 共享，使 ViewModel 的“校验后一次提交”保持可测。
    /// 子节点仍只在用户展开时延迟枚举。
    /// </remarks>
    internal static FileSystemNode CreateDirectoryRoot(string normalizedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPath);
        var name = FileSystemPath.TryNormalize(normalizedPath, out var path) &&
                   path.Kind is FileSystemPathKind.LocalDriveRoot or
                       FileSystemPathKind.UncShareRoot
            ? path.NormalizedPath
            : (System.IO.Path.GetFileName(normalizedPath) ?? normalizedPath);
        if (string.IsNullOrEmpty(name))
        {
            name = normalizedPath;
        }

        var node = new FileSystemNode(normalizedPath, name, isDirectory: true)
        {
            _areChildrenLoaded = false,
        };
        return node;
    }

    private FileSystemNode(string path, string name, bool isDirectory)
    {
        _path = path;
        _name = name;
        _isDirectory = isDirectory;
        _children = [];
        _areChildrenLoaded = true;
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

   
}
