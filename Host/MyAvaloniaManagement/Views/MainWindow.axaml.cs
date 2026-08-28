using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels.Bindings;

namespace MyAvaloniaManagement.Views;

internal sealed partial class MainWindow : Window, IWindowContentFullscreenHost
{
    private readonly WindowContentFullscreenSession _fullscreenSession;
    private readonly List<KeyBinding> _generatedKeyBindings = [];
    private IWorkbenchKeyBindingProjection? _keyBindingProjection;
    private bool _closed;
    private bool _windowCloseApproved;
    private bool _windowClosePending;

    public MainWindow()
    {
        InitializeComponent();
        _fullscreenSession = new WindowContentFullscreenSession(
            ContentFullscreenLayer,
            ContentFullscreenHost);
        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // Closed 是本 Window 生成对象的永久所有权边界。之后即使 Dispatcher 中仍有迟到刷新，
        // 或测试/框架又改变 DataContext，也不得重新向已关闭窗口安装 KeyBinding。
        _closed = true;
        DetachKeyBindingProjection();
        // Closing 可能被 Document 保存确认取消，只有真正 Closed 才使全屏宿主永久失效。
        // ContentHost 的视觉树卸载也会触发同一幂等入口，两条框架时序不形成双重释放。
        _fullscreenSession.Dispose();
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        AttachKeyBindingProjection();
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.ApplyPendingLayout();
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs args) =>
        AttachKeyBindingProjection();

    private void AttachKeyBindingProjection()
    {
        if (_closed)
        {
            DetachKeyBindingProjection();
            return;
        }

        var next = (DataContext as IMainWindowViewBindings)?.WorkbenchCommands.KeyBindings;
        if (ReferenceEquals(next, _keyBindingProjection))
        {
            RebuildKeyBindings();
            return;
        }

        DetachKeyBindingProjection();
        _keyBindingProjection = next;
        if (_keyBindingProjection is not null)
        {
            _keyBindingProjection.Changed += OnKeyBindingProjectionChanged;
            RebuildKeyBindings();
        }
    }

    private void DetachKeyBindingProjection()
    {
        if (_keyBindingProjection is not null)
        {
            _keyBindingProjection.Changed -= OnKeyBindingProjectionChanged;
            _keyBindingProjection = null;
        }
        RemoveGeneratedKeyBindings();
    }

    private void OnKeyBindingProjectionChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RebuildKeyBindings();
        }
        else
        {
            Dispatcher.UIThread.Post(RebuildKeyBindings);
        }
    }

    private void RebuildKeyBindings()
    {
        RemoveGeneratedKeyBindings();
        if (_closed || _keyBindingProjection is null)
        {
            return;
        }
        foreach (var entry in _keyBindingProjection.Items)
        {
            var binding = new KeyBinding
            {
                Gesture = new KeyGesture(entry.Key, entry.Modifiers),
                Command = entry.Command,
            };
            KeyBindings.Add(binding);
            _generatedKeyBindings.Add(binding);
        }
    }

    private void RemoveGeneratedKeyBindings()
    {
        foreach (var binding in _generatedKeyBindings)
        {
            _ = KeyBindings.Remove(binding);
            binding.Command = null!;
        }
        _generatedKeyBindings.Clear();
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_windowCloseApproved)
        {
            if (DataContext is ViewModels.MainWindowViewModel approvedViewModel)
            {
                approvedViewModel.SaveLayout();
            }
            return;
        }

        if (DataContext is ViewModels.MainWindowViewModel cleanViewModel &&
            !cleanViewModel.HasDirtyDocuments())
        {
            cleanViewModel.SaveLayout();
            return;
        }

        // Avalonia Closing 是同步可取消事件。首次请求必须立即取消，再异步汇总保存；只有
        // 用户完成决策后才重新 Close。这样窗口不会在文件选择器显示期间提前释放 Scope。
        e.Cancel = true;
        if (_windowClosePending ||
            DataContext is not ViewModels.MainWindowViewModel viewModel)
        {
            return;
        }

        _windowClosePending = true;
        try
        {
            if (!await viewModel.ConfirmWindowCloseAsync())
            {
                return;
            }

            _windowCloseApproved = true;
            Dispatcher.UIThread.Post(Close, DispatcherPriority.Background);
        }
        finally
        {
            _windowClosePending = false;
        }
    }

    IDisposable? IWindowContentFullscreenHost.TryPresent(Control content) =>
        _fullscreenSession.TryPresent(content);
}
