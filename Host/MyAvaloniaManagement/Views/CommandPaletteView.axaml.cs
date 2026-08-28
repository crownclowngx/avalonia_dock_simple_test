using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.ViewModels.Bindings;

namespace MyAvaloniaManagement.Views;

/// <summary>负责最小 Command Palette 的查询、选择和键盘会话。</summary>
/// <remarks>
/// 本 View 只保存窗口级临时交互状态，不读取 Catalog、Context、Document 或插件对象。命令状态和候选
/// 由只读投影提供，真正执行继续委托共享 Presentation Command，使菜单、快捷键和 Palette 保持单一路径。
/// </remarks>
internal sealed partial class CommandPaletteView : UserControl
{
    // Projection 由根级 Presentation 拥有；View 只在视觉树存活期间借用并成对订阅，绝不 Dispose 它。
    private IWorkbenchCommandPaletteProjection? _projection;
    // Attached 状态用于阻止 DataContext 在离树后重新挂接订阅。
    private bool _attached;
    // 会话状态只控制查询刷新与延迟焦点，不复制命令是否可执行等业务事实。
    private bool _sessionActive;

    public CommandPaletteView()
    {
        InitializeComponent();
        SearchBox.TextChanged += OnSearchTextChanged;
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>当窗口应关闭 Palette 并恢复先前焦点时发生。</summary>
    internal event EventHandler? CloseRequested;

    /// <summary>开始一个全新会话，清空查询并选择当前第一个结果。</summary>
    internal void BeginSession()
    {
        _sessionActive = true;
        SearchBox.Text = string.Empty;
        RefreshItems(preserveSelection: false);
        FocusSearchBox();
    }

    /// <summary>在快速重复打开时保留查询和选择，仅把输入焦点带回搜索框。</summary>
    internal void RefocusSearchBox() => FocusSearchBox();

    /// <summary>结束当前会话；投影订阅继续由视觉树和 DataContext 的真实所有权控制。</summary>
    internal void EndSession() => _sessionActive = false;

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _attached = true;
        AttachProjection();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _attached = false;
        _sessionActive = false;
        DetachProjection();
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_attached)
        {
            AttachProjection();
        }
    }

    private void AttachProjection()
    {
        var next = (DataContext as IMainWindowViewBindings)?.WorkbenchCommands.Palette;
        if (ReferenceEquals(next, _projection))
        {
            if (_sessionActive)
            {
                RefreshItems(preserveSelection: true);
            }
            return;
        }

        DetachProjection();
        _projection = next;
        if (_projection is not null)
        {
            _projection.Changed += OnProjectionChanged;
            if (_sessionActive)
            {
                RefreshItems(preserveSelection: false);
            }
        }
    }

    private void DetachProjection()
    {
        if (_projection is not null)
        {
            _projection.Changed -= OnProjectionChanged;
            _projection = null;
        }
        PaletteItems.ItemsSource = null;
        EmptyState.IsVisible = true;
    }

    private void OnProjectionChanged(object? sender, EventArgs args)
    {
        if (!_sessionActive)
        {
            return;
        }
        if (Dispatcher.CheckAccess())
        {
            RefreshItems(preserveSelection: true);
        }
        else
        {
            // 生产投影已经切回 UI Dispatcher；这里保留纵深保护，避免测试替身或未来实现
            // 从工作线程直接改写 ListBox 的 ItemsSource 和选择状态。
            Dispatcher.Post(() => RefreshItems(preserveSelection: true));
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs args)
    {
        if (_sessionActive)
        {
            RefreshItems(preserveSelection: false);
        }
    }

    private void RefreshItems(bool preserveSelection)
    {
        var selectedId = preserveSelection
            ? (PaletteItems.SelectedItem as WorkbenchCommandPaletteProjectionEntry)?.CommandId
            : null;
        var items = _projection?.GetItems(SearchBox.Text).ToArray() ?? [];
        PaletteItems.ItemsSource = items;
        EmptyState.IsVisible = items.Length == 0;

        var selectedIndex = selectedId is null
            ? -1
            : Array.FindIndex(items, item => item.CommandId == selectedId);
        PaletteItems.SelectedIndex = selectedIndex >= 0
            ? selectedIndex
            : items.Length > 0 ? 0 : -1;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs args)
    {
        if (!_sessionActive)
        {
            return;
        }

        switch (args.Key)
        {
            case Key.Escape:
                args.Handled = true;
                CloseRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Key.Up:
                args.Handled = true;
                MoveSelection(-1);
                break;
            case Key.Down:
                args.Handled = true;
                MoveSelection(1);
                break;
            case Key.Enter:
                args.Handled = true;
                ExecuteSelection();
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        var count = PaletteItems.ItemCount;
        if (count == 0)
        {
            return;
        }
        PaletteItems.SelectedIndex = Math.Clamp(
            PaletteItems.SelectedIndex + delta,
            0,
            count - 1);
        if (PaletteItems.SelectedItem is { } selected)
        {
            PaletteItems.ScrollIntoView(selected);
        }
    }

    private void ExecuteSelection()
    {
        if (PaletteItems.SelectedItem is not WorkbenchCommandPaletteProjectionEntry item ||
            !item.IsEnabled ||
            !item.Command.CanExecute(null))
        {
            // 展示快照可能在 Enter 前一瞬失效。留在 Palette 并重查，而不是相信旧 IsEnabled。
            RefreshItems(preserveSelection: true);
            return;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
        item.Command.Execute(null);
    }

    private void FocusSearchBox()
    {
        // 遮罩可见性和布局通常在当前输入事件之后提交；同步尝试让 Headless 路径可观察，
        // Dispatcher 回调则覆盖真实窗口首次布局的时序，两次 Focus 都是幂等的。
        _ = SearchBox.Focus();
        Dispatcher.Post(
            () =>
            {
                // 用户可能在布局提交前已经按 Escape、执行命令或关闭窗口。
                // 延迟回调必须再次确认会话仍存活，避免隐藏后的搜索框抢回焦点。
                if (_sessionActive)
                {
                    _ = SearchBox.Focus();
                }
            },
            DispatcherPriority.Input);
    }
}
