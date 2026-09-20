using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Input;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Business.Search;
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
        Palette = new DesignPaletteProjection(open, save);
    }

    /// <summary>获取设计器使用的纯内存菜单快照。</summary>
    public IWorkbenchMenuProjection Menu { get; }

    /// <summary>获取设计器使用的纯内存快捷键快照。</summary>
    public IWorkbenchKeyBindingProjection KeyBindings { get; }

    /// <summary>获取设计器使用的纯内存 Command Palette 快照。</summary>
    public IWorkbenchCommandPaletteProjection Palette { get; }

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

    /// <summary>让设计器能够预览 Palette 布局，但不构造生产查询和执行对象图。</summary>
    private sealed class DesignPaletteProjection(
        IWorkbenchPresentationCommandBinding open,
        IWorkbenchPresentationCommandBinding save) : IWorkbenchCommandPaletteProjection
    {
        public MyAvaloniaManagement.Business.Layout.DocumentCreationTarget? CaptureCreationTarget(Dock.Model.Controls.IRootDock? source) => null;
        private readonly IReadOnlyList<WorkbenchCommandPaletteProjectionEntry> _items =
        [
            new(new PagePaletteIdentity(new WorkspacePageId(Guid.Parse("00000000-0000-0000-0000-000000000001"))),
                "欢迎", "开始使用工作台", "", true, open)
                { SourceText = "主程序", InstanceText = "页面 1 · 当前", ExecuteHint = "回到“欢迎”（页面 1）" },
            new(new PagePaletteIdentity(new WorkspacePageId(Guid.Parse("00000000-0000-0000-0000-000000000002"))),
                "欢迎", "第二个独立实例", "", true, open)
                { SourceText = "主程序", InstanceText = "页面 2", ExecuteHint = "回到“欢迎”（页面 2）" },
            new(new FunctionPaletteIdentity(HostExtensionIds.WelcomeDocument, null), "欢迎主程序", "在新标签中开始使用", "", true, open)
                { SourceText = "主程序", ExecuteHint = "新开“欢迎主程序”" },
            new(new ToolPaletteIdentity(HostExtensionIds.FileSystemTree), "文件系统浏览器", "已隐藏", "", true, open)
                { SourceText = "主程序", ActionText = "显示", ExecuteHint = "显示“文件系统浏览器”" },
            new WorkbenchCommandPaletteProjectionEntry(
                new CommandPaletteIdentity(HostWorkbenchCommandIds.OpenDocument),
                "打开…",
                "打开一个已保存的文档",
                string.Empty,
                true,
                open),
            new WorkbenchCommandPaletteProjectionEntry(
                new CommandPaletteIdentity(HostWorkbenchCommandIds.SaveDocument),
                "保存",
                "保存当前文档",
                "Ctrl+S",
                false,
                save) { SourceText = "主程序", UnavailableReason = "当前页面不支持保存" },
        ];

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<WorkbenchCommandPaletteProjectionEntry> GetItems(string? query)
        {
            var normalized = query?.Trim() ?? string.Empty;
            return WorkbenchPaletteOrdering.Sort(_items.Select(item => item with
                { MatchRank = WorkbenchTextMatch.Rank(item.SearchName, normalized, item.Description, item.SourceText) })
                .Where(item => item.MatchRank < int.MaxValue));
        }
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
