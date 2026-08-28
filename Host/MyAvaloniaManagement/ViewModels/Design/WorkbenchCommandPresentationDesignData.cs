using System;
using System.ComponentModel;
using MyAvaloniaManagement.Business.Presentation.Commands;

namespace MyAvaloniaManagement.ViewModels.Design;

/// <summary>供设计器预览主窗口命令绑定的纯内存样例。</summary>
/// <remarks>
/// 设计样例只满足编译绑定，不构造 Catalog、Context、Executor、Dispatcher 或生产 DI 容器。
/// 它与生产 Presentation 分离，避免设计器意外打开文件选择器或写入用户 Document。
/// </remarks>
internal sealed class WorkbenchCommandPresentationDesignData :
    IWorkbenchCommandPresentationBindings
{
    /// <summary>初始化两个无副作用的同步样例命令。</summary>
    internal WorkbenchCommandPresentationDesignData()
    {
        Open = new NoOperationPresentationCommand();
        Save = new NoOperationPresentationCommand();
    }

    /// <summary>获取设计器使用的无副作用打开命令。</summary>
    public IWorkbenchPresentationCommandBinding Open { get; }

    /// <summary>获取设计器使用的无副作用保存命令。</summary>
    public IWorkbenchPresentationCommandBinding Save { get; }

    /// <summary>设计器专用的恒 Enabled、无副作用命令。</summary>
    private sealed class NoOperationPresentationCommand :
        IWorkbenchPresentationCommandBinding
    {
        public bool IsEnabled => true;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }
}
