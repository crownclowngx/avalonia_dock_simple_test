using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.Models.FileSystem;

namespace MyAvaloniaManagement.ViewModels.Bindings;

/// <summary>文件树 View 所需状态和命令的最小内部绑定端口。</summary>
/// <remarks>
/// 端口刻意不暴露存储服务或消息总线。设计样例因此可以用固定内存节点满足绑定，生产
/// ViewModel 则继续通过构造注入执行文件选择和打开动作。
/// </remarks>
internal interface IFileSystemTreeViewBindings
{
    ObservableCollection<FileSystemNode> RootNodes { get; }

    string SelectedFolderPath { get; }

    FileSystemNode? SelectedNode { get; set; }

    IAsyncRelayCommand SelectFolderCommand { get; }

    IAsyncRelayCommand OpenFileCommand { get; }

    IRelayCommand RefreshNodeCommand { get; }

    IRelayCommand RefreshAllCommand { get; }
}
