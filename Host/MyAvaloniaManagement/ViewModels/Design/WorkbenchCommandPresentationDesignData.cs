using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Input;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.ViewModels.Design;

/// <summary>供设计器预览主窗口命令绑定的纯内存样例。</summary>
/// <remarks>
/// 设计样例只满足编译绑定，不构造 Catalog、Context、Executor、Dispatcher 或生产 DI 容器。
/// 它与生产 Presentation 分离，避免设计器意外打开文件选择器或写入用户 Document。
/// </remarks>
internal sealed class WorkbenchCommandPresentationDesignData :
    IWorkbenchCommandPresentationBindings
{
    /// <summary>初始化无副作用的菜单与快捷键投影样例。</summary>
    internal WorkbenchCommandPresentationDesignData()
    {
        var open = new NoOperationPresentationCommand();
        var save = new NoOperationPresentationCommand();
        Menu = new DesignMenuProjection(open, save);
        KeyBindings = new DesignKeyBindingProjection(save);
    }

    /// <summary>获取设计器使用的纯内存菜单快照。</summary>
    public IWorkbenchMenuProjection Menu { get; }

    /// <summary>获取设计器使用的纯内存快捷键快照。</summary>
    public IWorkbenchKeyBindingProjection KeyBindings { get; }

    private sealed class DesignMenuProjection(
        IWorkbenchPresentationCommandBinding open,
        IWorkbenchPresentationCommandBinding save) : IWorkbenchMenuProjection
    {
        private readonly IReadOnlyList<WorkbenchMenuProjectionEntry> _file =
        [
            new WorkbenchMenuCommandProjectionEntry(
                new CommandPlacementId(
                    "myavalonia.host.command-placement.menu.file.open-document"),
                HostWorkbenchCommandIds.OpenDocument,
                "打开…",
                open),
            new WorkbenchMenuCommandProjectionEntry(
                new CommandPlacementId(
                    "myavalonia.host.command-placement.menu.file.save-document"),
                HostWorkbenchCommandIds.SaveDocument,
                "保存",
                save),
        ];

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<WorkbenchMenuProjectionEntry> GetItems(
            MenuLocationId locationId) =>
            locationId == WorkbenchMenuLocations.FileShared
                ? _file
                : [];
    }

    private sealed class DesignKeyBindingProjection(
        IWorkbenchPresentationCommandBinding save) : IWorkbenchKeyBindingProjection
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<WorkbenchKeyBindingProjectionEntry> Items { get; } =
        [
            new WorkbenchKeyBindingProjectionEntry(
                new CommandPlacementId(
                    "myavalonia.host.command-placement.key-binding.save-document"),
                HostWorkbenchCommandIds.SaveDocument,
                Key.S,
                KeyModifiers.Control,
                save),
        ];
    }

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
