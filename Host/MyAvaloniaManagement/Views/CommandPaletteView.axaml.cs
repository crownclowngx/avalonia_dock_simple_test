using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.ViewModels.Bindings;

namespace MyAvaloniaManagement.Views;

/// <summary>负责工作区四类搜索结果的查询、选择和键盘会话。</summary>
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
    private int _sessionVersion;
    private DocumentCreationTarget? _creationTarget;
    private WorkbenchWindowContext? _windows;
    internal bool IsBusy { get; private set; }
    internal Task CurrentExecution { get; private set; } = Task.CompletedTask;

    public CommandPaletteView()
    {
        InitializeComponent();
        SearchBox.TextChanged += OnSearchTextChanged;
        PaletteItems.SelectionChanged += (_, _) => UpdateSelectionHint();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>当窗口应关闭 Palette 并恢复先前焦点时发生。</summary>
    internal event EventHandler<PaletteCloseRequestedEventArgs>? CloseRequested;

    /// <summary>开始一个全新会话，清空查询并选择当前第一个结果。</summary>
    internal void BeginSession(DocumentCreationTarget? target = null, WorkbenchWindowContext? windows = null)
    {
        // 此时搜索框尚未获取焦点。目标归属于本窗口面板会话，执行时复制给异步调用，
        // 不写入共享投影命令，也不在插件初始化完成后重新读取当前活动窗口。
        _creationTarget = target;
        _windows = windows;
        _sessionActive = true;
        _sessionVersion++;
        SetBusy(false);
        SetStatus(string.Empty);
        SearchBox.Text = string.Empty;
        RefreshItems(preserveSelection: false);
        FocusSearchBox();
    }

    /// <summary>在快速重复打开时保留查询和选择，仅把输入焦点带回搜索框。</summary>
    internal void RefocusSearchBox() => FocusSearchBox();

    /// <summary>结束当前会话；投影订阅继续由视觉树和 DataContext 的真实所有权控制。</summary>
    internal void EndSession()
    {
        _creationTarget = null;
        _windows = null;
        _sessionActive = false;
        _sessionVersion++;
        SetBusy(false);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _attached = true;
        AttachProjection();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _attached = false;
        EndSession();
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
        var next = (DataContext as IWorkbenchCommandPresentationBindings)?.Palette
            ?? (DataContext as IMainWindowViewBindings)?.WorkbenchCommands.Palette;
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
        UpdateSelectionHint();
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
            SetStatus(string.Empty);
            RefreshItems(preserveSelection: false);
        }
    }

    private void RefreshItems(bool preserveSelection)
    {
        if (!_sessionActive || IsBusy) return;
        var selectedId = preserveSelection
            ? (PaletteItems.SelectedItem as WorkbenchCommandPaletteProjectionEntry)?.StableKey
            : null;
        var items = _projection?.GetItems(SearchBox.Text).ToArray() ?? [];
        var scroll = PaletteItems.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var offset = scroll?.Offset;
        if (preserveSelection)
            items = WorkbenchPaletteOrdering.PreserveOrder(PaletteItems.Items.OfType<WorkbenchCommandPaletteProjectionEntry>().ToArray(), items);
        PaletteItems.ItemsSource = items;
        EmptyState.IsVisible = items.Length == 0;

        var selectedIndex = selectedId is null
            ? -1
            : Array.FindIndex(items, item => item.StableKey == selectedId);
        var firstEnabled = Array.FindIndex(items, item => item.IsEnabled);
        PaletteItems.SelectedIndex = selectedIndex >= 0 ? selectedIndex :
            firstEnabled >= 0 ? firstEnabled : items.Length > 0 ? 0 : -1;
        if (selectedId is not null && selectedIndex < 0)
            SetStatus("原目标已不可用，请确认新的选择后再执行。");
        if (selectedIndex >= 0 && scroll is not null && offset is { } previousOffset)
        {
            PaletteItems.UpdateLayout();
            scroll.Offset = previousOffset;
        }
        UpdateSelectionHint();
    }

    /// <summary>底部只预告选中项的动作；失败信息保留在独立状态行，不被普通选择通知覆盖。</summary>
    private void UpdateSelectionHint() => SelectionHint.Text =
        (PaletteItems.SelectedItem as WorkbenchCommandPaletteProjectionEntry)?.EnterHint ?? string.Empty;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs args)
    {
        if (!_sessionActive)
        {
            return;
        }

        // Avalonia TextBox 同样以 PreeditText 判断组合输入。候选字确认、方向键和 Esc 应先留给输入法，
        // 否则窗口级 Tunnel 处理器会在 TextBox 有机会检查前误执行命令。这里只读取控件公开展示状态。
        if (SearchBox.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText)))
            return;

        switch (args.Key)
        {
            case Key.Escape:
                args.Handled = true;
                if (!IsBusy) CloseRequested?.Invoke(this, new(true));
                break;
            case Key.Up:
                args.Handled = true;
                if (!IsBusy) MoveSelection(-1);
                break;
            case Key.Down:
                args.Handled = true;
                if (!IsBusy) MoveSelection(1);
                break;
            case Key.Enter:
                args.Handled = true;
                if (!IsBusy) CurrentExecution = ExecuteSelectionAsync();
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

    // 双击仅绑定结果正文；组标题和列表空白不能执行先前的选择。
    private void OnResultDoubleTapped(object? sender, TappedEventArgs args)
    {
        if (!IsBusy) CurrentExecution = ExecuteSelectionAsync();
        args.Handled = true;
    }

    /// <summary>一次搜索会话拥有一次在途动作；失败保留现场，成功后才移除遮罩。</summary>
    /// <remarks>
    /// 普通命令仍先关闭再执行，保持文件选择器与当前 Target 的既有时序。
    /// 新建／定位／工具动作有可等待结果，关闭窗口不能冒充取消已经开始的插件初始化。
    /// 会话代号阻止旧异步完成回写已关闭或重新打开的搜索框。
    /// </remarks>
    internal async Task ExecuteSelectionAsync()
    {
        if (!_sessionActive || IsBusy) return;
        var item = PaletteItems.SelectedItem as WorkbenchCommandPaletteProjectionEntry;
        var current = item is null ? null : _projection?.GetItems(SearchBox.Text).FirstOrDefault(next => next.StableKey == item.StableKey);
        // 捕获原选择后只验证同一身份；更新列表时自动补选的结果不能消费本次 Enter。
        if (item is null || current is null || !item.IsEnabled || !current.IsEnabled ||
            item.ExpectedTarget != current.ExpectedTarget || !item.Command.CanExecute(null))
        {
            SetStatus(item is { IsEnabled: false } ? item.DisabledText : "目标或状态已变化，请确认后再执行。");
            RefreshItems(preserveSelection: true);
            return;
        }
        if (item.Identity is CommandPaletteIdentity)
        {
            if (item.Command is WorkbenchPresentationCommand guarded && !guarded.CanExecuteForTarget(item.ExpectedTarget))
            {
                SetStatus("命令目标已变化，请确认后再执行。");
                RefreshItems(preserveSelection: true);
                return;
            }
            CloseRequested?.Invoke(this, new(true));
            if (item.Command is WorkbenchPresentationCommand command)
                await command.ExecuteObservedAsync(item.ExpectedTarget);
            else
                item.Command.Execute(null);
            return;
        }
        var version = _sessionVersion;
        var target = _creationTarget;
        var windows = _windows;
        var activationVersion = windows?.ActivationVersion;
        SetBusy(true);
        SetStatus(item.Identity is FunctionPaletteIdentity ? "正在打开功能…" : "正在处理…");
        WorkspacePaletteResult result;
        try
        {
            result = item.Command is WorkspacePaletteCommand action
                ? await action.ExecuteWithTargetAsync(target) : new("该结果暂时无法执行，请重新选择。");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Palette errorCode=PALETTE_ACTION_FAILED type={exception.GetType().Name}");
            result = new("操作未完成，请重试。");
        }
        if (!_sessionActive || version != _sessionVersion) return;
        SetBusy(false);
        SetStatus(result.Error);
        if (result.Error.Length == 0)
        {
            // 页面定位/工具定位主动切换窗口属于动作本身；只有新建要防止等待期间的窗口切换
            // 被迟到焦点恢复撤销。关闭会使会话号加一，再次打开或离树则使旧恢复自动失效。
            CloseRequested?.Invoke(this, new(false));
            // 新标签的内容可能尚未挂到视觉树，先提交窗口布局，再将焦点交给其输入控件。
            TopLevel.GetTopLevel(this)?.UpdateLayout();
            (item.Command as WorkspacePaletteCommand)?.FocusResult(result.PageId,
                () => !_sessionActive && _sessionVersion == version + 1 &&
                    (item.Identity is not FunctionPaletteIdentity || windows?.ActivationVersion == activationVersion));
        }
        else
        {
            RefreshItems(preserveSelection: true);
            FocusSearchBox();
        }
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        SearchBox.IsEnabled = !busy;
        PaletteItems.IsEnabled = !busy;
    }

    private void SetStatus(string text)
    {
        OperationStatus.Text = text;
        OperationStatus.IsVisible = text.Length > 0;
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

/// <summary>Esc 恢复旧焦点；工作区动作成功则由动作本身把焦点交给新目标。</summary>
internal sealed class PaletteCloseRequestedEventArgs(bool restorePreviousFocus) : EventArgs
{
    internal bool RestorePreviousFocus { get; } = restorePreviousFocus;
}
