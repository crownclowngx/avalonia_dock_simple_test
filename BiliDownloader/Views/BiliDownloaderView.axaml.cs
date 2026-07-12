using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using BiliDownloader.ViewModels;

namespace BiliDownloader.Views;

public partial class BiliDownloaderView : UserControl
{
    private bool _hasCheckedLogin;
    private bool _hasRecoveredTasks;
    private bool _hasSetupSyncScroll;

    private bool _isSyncing;

    public BiliDownloaderView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is BiliDownloaderViewModel vm)
        {
            // 首次附加到视觉树时异步触发登录检查
            if (!_hasCheckedLogin)
            {
                _hasCheckedLogin = true;
                _ = vm.EnsureLoggedInAsync();
            }

            // 首次附加时从 SQLite 恢复未完成任务状态
            if (!_hasRecoveredTasks)
            {
                _hasRecoveredTasks = true;
                _ = vm.RecoverTasksFromStoreAsync();
            }
        }

        // 设置重命名面板同步滚动（仅一次）
        if (!_hasSetupSyncScroll)
        {
            _hasSetupSyncScroll = true;
            SetupSyncScroll();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // 点击视图时检查登录状态
        if (DataContext is BiliDownloaderViewModel { IsLoggedIn: false } vm)
        {
            _ = vm.EnsureLoggedInAsync();
        }
    }

    /// <summary>
    /// 设置左右重命名 TextBox 的同步滚动
    /// </summary>
    private void SetupSyncScroll()
    {
        var leftTextBox = this.FindControl<TextBox>("OriginalTitlesTextBox");
        var rightTextBox = this.FindControl<TextBox>("NewTitlesTextBox");
        if (leftTextBox == null || rightTextBox == null) return;

        // 用 Tunnel 策略确保鼠标滚轮事件也能被捕获
        leftTextBox.AddHandler(ScrollViewer.ScrollChangedEvent,
            (sender, e) => SyncScroll(leftTextBox, rightTextBox),
            RoutingStrategies.Tunnel);

        rightTextBox.AddHandler(ScrollViewer.ScrollChangedEvent,
            (sender, e) => SyncScroll(rightTextBox, leftTextBox),
            RoutingStrategies.Tunnel);
    }

    private void SyncScroll(TextBox source, TextBox target)
    {
        if (_isSyncing) return;
        var sourceScroll = FindScrollViewer(source);
        var targetScroll = FindScrollViewer(target);
        if (sourceScroll == null || targetScroll == null) return;

        _isSyncing = true;
        targetScroll.Offset = sourceScroll.Offset;
        _isSyncing = false;
    }

    private static ScrollViewer? FindScrollViewer(TextBox textBox)
    {
        foreach (var descendant in textBox.GetVisualDescendants())
        {
            if (descendant is ScrollViewer sv)
                return sv;
        }
        return null;
    }
}
