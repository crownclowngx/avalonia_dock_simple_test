using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MySmallTools.ViewModels.SecretVideoPlayer;
using MySmallTools.Views.SecretVideoPlayer.Playback;

namespace MySmallTools.Views.SecretVideoPlayer;

/// <summary>
/// 视频播放器控件
/// 独立的播放器组件，可以在不同的视图中复用
/// </summary>
public partial class VideoPlayerControl : UserControl
{
    public static readonly StyledProperty<IPlaybackNavigationContext?> NavigationContextProperty =
        AvaloniaProperty.Register<VideoPlayerControl, IPlaybackNavigationContext?>(
            nameof(NavigationContext));

    private VideoPlayerControlViewModel? _boundViewModel;
    private readonly FullscreenPlaybackPresenter _fullscreenPresenter;
    private bool _hasNavigationContext;

    public IPlaybackNavigationContext? NavigationContext
    {
        get => GetValue(NavigationContextProperty);
        set => SetValue(NavigationContextProperty, value);
    }

    public bool HasNavigationContext
    {
        get => _hasNavigationContext;
        private set => SetAndRaise(
            HasNavigationContextProperty,
            ref _hasNavigationContext,
            value);
    }

    /// <summary>
    /// 播放器控件的 ViewModel
    /// </summary>
    public VideoPlayerControlViewModel? ViewModel => DataContext as VideoPlayerControlViewModel;

    public VideoPlayerControl()
    {
        InitializeComponent();
        _fullscreenPresenter = new FullscreenPlaybackPresenter(
            this,
            NormalPlaceholder,
            PlayerShell,
            Viewport,
            () => _boundViewModel);
        DataContextChanged += OnDataContextChanged;
        Viewport.Surface.SurfaceReadyChanged += OnSurfaceReadyChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        AddHandler(KeyDownEvent, OnPlayerKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPlayerPointerPressed, RoutingStrategies.Tunnel);
        this.GetObservable(NavigationContextProperty).Subscribe(value =>
            HasNavigationContext = value is not null);
    }

    // HasNavigationContext 只用于 XAML 显隐；DirectProperty 让 NavigationContext 改变时
    // 无需把呈现端口塞进 VideoPlayerControlViewModel。
    public static readonly DirectProperty<VideoPlayerControl, bool> HasNavigationContextProperty =
        AvaloniaProperty.RegisterDirect<VideoPlayerControl, bool>(
            nameof(HasNavigationContext),
            control => control.HasNavigationContext);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_boundViewModel, DataContext))
        {
            return;
        }

        // 控件回收给另一个文档时，先通知旧 ViewModel 表面已经不可用，
        // 再把当前 HWND 状态交给新的 ViewModel，避免两个播放器同时认为自己拥有同一个输出窗口。
        if (_boundViewModel is not null)
        {
            _fullscreenPresenter.ForceReset();
            _boundViewModel.ResetFullscreenPresentation();
            _boundViewModel.SetVideoSurface(null);
            _boundViewModel.NativeOutputChanged -= OnNativeOutputChanged;
            _boundViewModel.FullscreenPresentationRequested -= OnFullscreenPresentationRequested;
        }

        // MediaPlayer 不使用 XAML 继承 DataContext 绑定，而是在这里按严格顺序切换：
        // 先暂停旧播放器，再让 VideoView 清除旧 Hwnd，最后绑定新播放器。
        // 这可避免 DataContext 向子控件传播得更早时，旧播放器在仍播放的状态下突然失去输出句柄。
        _boundViewModel = DataContext as VideoPlayerControlViewModel;
        if (_boundViewModel is not null)
        {
            _boundViewModel.NativeOutputChanged += OnNativeOutputChanged;
            _boundViewModel.FullscreenPresentationRequested += OnFullscreenPresentationRequested;
        }
        Viewport.Surface.MediaPlayer = _boundViewModel?.MediaPlayer;
        _boundViewModel?.SetVideoSurface(Viewport.Surface.CurrentSurfaceToken);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Dock 重新展示 View 时，正常占位容器必须重新拥有唯一 PlayerShell。
        if (NormalPlaceholder.Content is null && PlayerShell.Parent is null)
        {
            NormalPlaceholder.Content = PlayerShell;
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Dock 切出或文档关闭时不能把 Shell 留在 TopLevel 覆盖层，否则不可见 Document
        // 仍会截获输入并持有 HWND。这里执行纯视觉幂等回收，表面事件负责同步停止 vout。
        _fullscreenPresenter.ForceReset();
        _boundViewModel?.ResetFullscreenPresentation();
    }

    private void OnSurfaceReadyChanged(object? sender, VideoSurfaceReadyChangedEventArgs e)
    {
        // 事件由 DestroyNativeControlCore 在清除 HWND 前同步触发，
        // 所以此处不可异步排队，必须立即让 ViewModel 暂停正在播放的媒体。
        _boundViewModel?.SetVideoSurface(e.IsReady ? e.Surface : null);
    }

    private void OnNativeOutputChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _boundViewModel))
        {
            Viewport.Surface.MediaPlayer = _boundViewModel?.MediaPlayer;
        }
    }

    private async void OnFullscreenPresentationRequested(
        object? sender,
        FullscreenPresentationRequestedEventArgs e)
    {
        if (!ReferenceEquals(sender, _boundViewModel))
        {
            return;
        }

        var failure = await _fullscreenPresenter.ApplyAsync(e.EnterFullscreen);
        var viewModel = _boundViewModel;
        if (viewModel is not null && ReferenceEquals(sender, viewModel))
            viewModel.CompleteFullscreenPresentation(
                e.Revision,
                e.EnterFullscreen && failure is null,
                failure);
    }

    private void OnPlayerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is EmbeddedVideoSurface or Border)
        {
            Focus();
        }
    }

    private void OnPlayerKeyDown(object? sender, KeyEventArgs e)
    {
        var viewModel = _boundViewModel;
        if (viewModel is null)
            return;
        if (PlaybackShortcutRouter.TryHandle(e, viewModel))
            e.Handled = true;
    }

    /// <summary>
    /// 加载媒体文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="password">解密密码</param>
    /// <returns>是否加载成功</returns>
    public async Task<bool> LoadMediaAsync(string filePath, string password)
    {
        if (ViewModel != null)
        {
            return await ViewModel.LoadMediaAsync(filePath, password);
        }
        return false;
    }

    /// <summary>
    /// 清理当前媒体
    /// </summary>
    public Task CleanupMediaAsync(CancellationToken cancellationToken = default)
    {
        return ViewModel?.CleanupMediaAsync(cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>
    /// 获取当前播放状态
    /// </summary>
    public bool IsPlaying => ViewModel?.IsPlaying ?? false;

    /// <summary>
    /// 获取当前暂停状态
    /// </summary>
    public bool IsPaused => ViewModel?.IsPaused ?? false;
}
