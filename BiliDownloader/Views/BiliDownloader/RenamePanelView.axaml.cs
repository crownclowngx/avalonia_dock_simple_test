using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace BiliDownloader.Views.BiliDownloader;

public partial class RenamePanelView : UserControl
{
    private bool _hasSetupSyncScroll;
    private bool _isSyncing;

    public RenamePanelView()
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

        // 设置重命名面板同步滚动（仅一次）
        if (!_hasSetupSyncScroll)
        {
            _hasSetupSyncScroll = true;
            SetupSyncScroll();
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
