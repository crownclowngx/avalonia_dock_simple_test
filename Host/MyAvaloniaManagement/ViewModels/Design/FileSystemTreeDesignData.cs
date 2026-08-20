using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.Models.FileSystem;
using MyAvaloniaManagement.ViewModels.Bindings;

namespace MyAvaloniaManagement.ViewModels.Design;

/// <summary>供文件树设计预览使用的固定内存样例。</summary>
/// <remarks>
/// 节点通过显式样例构造函数创建，不调用 <c>Directory.Exists</c>、驱动器枚举或延迟目录加载；
/// 所有命令均无副作用。生产 View 运行时的 DataContext 仍由 Tool 策略注入。
/// </remarks>
internal sealed class FileSystemTreeDesignData : IFileSystemTreeViewBindings
{
    public FileSystemTreeDesignData()
    {
        var documents = FileSystemNode.CreateDesignSample(
            "C:\\Users\\示例\\Documents",
            "Documents",
            true,
            FileSystemNode.CreateDesignSample(
                "C:\\Users\\示例\\Documents\\项目说明.md",
                "项目说明.md",
                false),
            FileSystemNode.CreateDesignSample(
                "C:\\Users\\示例\\Documents\\示例数据.json",
                "示例数据.json",
                false));
        RootNodes = new ObservableCollection<FileSystemNode> { documents };
        SelectedNode = documents;
        SelectFolderCommand = new AsyncRelayCommand(() => System.Threading.Tasks.Task.CompletedTask);
        OpenFileCommand = new AsyncRelayCommand(() => System.Threading.Tasks.Task.CompletedTask);
        RefreshNodeCommand = new RelayCommand(() => { });
        RefreshAllCommand = new RelayCommand(() => { });
    }

    public ObservableCollection<FileSystemNode> RootNodes { get; }

    public string SelectedFolderPath => "C:\\Users\\示例\\Documents";

    public FileSystemNode? SelectedNode { get; set; }

    public IAsyncRelayCommand SelectFolderCommand { get; }

    public IAsyncRelayCommand OpenFileCommand { get; }

    public IRelayCommand RefreshNodeCommand { get; }

    public IRelayCommand RefreshAllCommand { get; }
}
