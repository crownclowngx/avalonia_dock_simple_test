using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MyAvaloniaManagementCommon.Presentation;
using MySmallTools.Business.SecretVideoPlayer.Playback;
using MySmallTools.ViewModels.SecretVideoPlayer;

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
    private readonly SemaphoreSlim _fullscreenTransitionGate = new(1, 1);
    private IWindowContentFullscreenHost? _fullscreenHost;
    private TopLevel? _fullscreenTopLevel;
    private bool _hasNavigationContext;
    private bool _forcingVisualReset;

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
        DataContextChanged += OnDataContextChanged;
        VideoSurface.SurfaceReadyChanged += OnSurfaceReadyChanged;
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
            ForceExitFullscreenVisual();
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
        VideoSurface.MediaPlayer = _boundViewModel?.MediaPlayer;
        _boundViewModel?.SetVideoSurface(VideoSurface.CurrentSurfaceToken);
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
        ForceExitFullscreenVisual();
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
            VideoSurface.MediaPlayer = _boundViewModel?.MediaPlayer;
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

        await _fullscreenTransitionGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(sender, _boundViewModel))
            {
                return;
            }

            var failure = e.EnterFullscreen
                ? await EnterFullscreenAsync()
                : await ExitFullscreenAsync();
            _boundViewModel?.CompleteFullscreenPresentation(
                e.Revision,
                e.EnterFullscreen && failure is null,
                failure);
        }
        finally
        {
            _fullscreenTransitionGate.Release();
        }
    }

    private async Task<PlaybackFailure?> EnterFullscreenAsync()
    {
        if (_fullscreenHost is not null)
        {
            return null;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not IWindowContentFullscreenHost fullscreenHost)
        {
            return new PlaybackFailure(
                PlaybackFailureCode.ControlUnavailable,
                "当前宿主窗口不支持内容区全屏。");
        }

        var previousGeneration = VideoSurface.CurrentSurfaceToken?.Generation ?? 0;
        var attachment = WaitForNewSurfaceAttachmentAsync(previousGeneration);

        // 先从旧父容器移除，并把一次后台调度机会让给 Avalonia。若在同一个
        // UI 调度片段中“移除后立即加入”，框架会把两次变更合并成普通重设父级，
        // NativeControlHost 就不会销毁 HWND，后续等待新表面代次必然超时。
        // 分成两个视觉树事务后，DestroyNativeControlCore 会先保存恢复快照，
        // 覆盖层再创建新的 HWND。整个过程仍只复用这一份 PlayerShell/VideoView。
        NormalPlaceholder.Content = null;
        await WaitForNativeSurfaceReleaseAsync();

        if (!fullscreenHost.TryPresent(PlayerShell, this))
        {
            NormalPlaceholder.Content = PlayerShell;
            var restoreFailure = await attachment;
            return restoreFailure ?? new PlaybackFailure(
                PlaybackFailureCode.ControlUnavailable,
                "当前窗口已有播放器处于全屏状态。");
        }

        _fullscreenHost = fullscreenHost;
        _fullscreenTopLevel = topLevel;
        _fullscreenTopLevel?.AddHandler(
            KeyDownEvent,
            OnFullscreenTopLevelKeyDown,
            RoutingStrategies.Tunnel);

        var failure = await attachment;
        if (failure is not null)
        {
            await RollBackFailedFullscreenEntryAsync(fullscreenHost);
            return failure;
        }

        Focus();
        return null;
    }

    private async Task<PlaybackFailure?> ExitFullscreenAsync()
    {
        if (_fullscreenHost is null)
        {
            return null;
        }

        var previousGeneration = VideoSurface.CurrentSurfaceToken?.Generation ?? 0;
        var attachment = WaitForNewSurfaceAttachmentAsync(previousGeneration);
        var host = _fullscreenHost;
        if (!host.TryRestore(this))
        {
            return new PlaybackFailure(
                PlaybackFailureCode.ControlUnavailable,
                "宿主窗口拒绝归还全屏播放器。");
        }

        _fullscreenHost = null;
        await WaitForNativeSurfaceReleaseAsync();
        NormalPlaceholder.Content = PlayerShell;
        RemoveFullscreenTopLevelHandler();

        var failure = await attachment;
        Focus();
        return failure;
    }

    private async Task RollBackFailedFullscreenEntryAsync(
        IWindowContentFullscreenHost fullscreenHost)
    {
        var previousGeneration = VideoSurface.CurrentSurfaceToken?.Generation ?? 0;
        if (!fullscreenHost.TryRestore(this))
        {
            ForceExitFullscreenVisual();
            return;
        }

        var attachment = WaitForNewSurfaceAttachmentAsync(previousGeneration);
        _fullscreenHost = null;
        RemoveFullscreenTopLevelHandler();
        await WaitForNativeSurfaceReleaseAsync();
        NormalPlaceholder.Content = PlayerShell;

        // The original surface failure remains the user-facing error, but wait
        // for the normal presentation to settle before completing the command.
        _ = await attachment;
    }

    /// <summary>
    /// 给 NativeControlHost 一个独立的后台调度周期来释放旧 HWND。
    /// </summary>
    /// <remarks>
    /// 这里不主动调用任何 Avalonia 私有 API，也不自行销毁 Win32 句柄；句柄的所有权
    /// 始终属于 EmbeddedVideoSurface。Avalonia 11.3 的 NativeControlHost 会把销毁
    /// 延迟到 Background 优先级，以便跨 TopLevel 重设父级时复用句柄。因此这里必须
    /// 等待同优先级队列排到我们这个空操作，不能只等待优先级更高的 Render 队列；
    /// 到达该点就能确定框架先前排入的 CheckDestruction 已经有机会执行。
    /// </remarks>
    private static async Task WaitForNativeSurfaceReleaseAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(
            static () => { },
            DispatcherPriority.Background);
    }

    private async Task<PlaybackFailure?> WaitForNewSurfaceAttachmentAsync(long previousGeneration)
    {
        var viewModel = _boundViewModel;
        if (viewModel is null)
        {
            return new PlaybackFailure(
                PlaybackFailureCode.ControlUnavailable,
                "播放器视图已不可用。");
        }

        var completion = new TaskCompletionSource<PlaybackFailure?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<VideoSurfaceAttachmentCompletedEventArgs>? handler = null;
        handler = (_, args) =>
        {
            if (args.Surface.Generation <= previousGeneration)
            {
                return;
            }

            completion.TrySetResult(args.Result.Success
                ? null
                : args.Result.Failure ?? new PlaybackFailure(
                    PlaybackFailureCode.SurfaceRestoreFailed,
                    "视频表面恢复失败。"));
        };

        viewModel.SurfaceAttachmentCompleted += handler;
        try
        {
            var completed = await Task.WhenAny(
                completion.Task,
                Task.Delay(TimeSpan.FromSeconds(5)));
            return completed == completion.Task
                ? await completion.Task
                : new PlaybackFailure(
                PlaybackFailureCode.SurfaceRestoreFailed,
                "视频表面未能在允许时间内完成恢复。");
        }
        finally
        {
            viewModel.SurfaceAttachmentCompleted -= handler;
        }
    }

    private void ForceExitFullscreenVisual()
    {
        if (_forcingVisualReset)
        {
            return;
        }

        _forcingVisualReset = true;
        try
        {
            RemoveFullscreenTopLevelHandler();
            if (_fullscreenHost is not null)
            {
                _fullscreenHost.TryRestore(this);
                _fullscreenHost = null;
            }

            if (NormalPlaceholder.Content is null && PlayerShell.Parent is null)
            {
                NormalPlaceholder.Content = PlayerShell;
            }
        }
        finally
        {
            _forcingVisualReset = false;
        }
    }

    private void RemoveFullscreenTopLevelHandler()
    {
        _fullscreenTopLevel?.RemoveHandler(
            KeyDownEvent,
            OnFullscreenTopLevelKeyDown);
        _fullscreenTopLevel = null;
    }

    private void OnFullscreenTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _boundViewModel?.IsFullscreen == true)
        {
            if (_boundViewModel.ToggleFullscreenCommand.CanExecute(null))
            {
                _boundViewModel.ToggleFullscreenCommand.Execute(null);
            }
            e.Handled = true;
        }
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
        if (viewModel is null || IsEditingOrSelectingControl(e.Source))
        {
            return;
        }

        var action = PlaybackShortcutPolicy.Map(
            e.Key,
            e.KeyModifiers,
            viewModel.IsFullscreen);
        var command = action switch
        {
            PlaybackShortcutAction.TogglePlayPause => viewModel.TogglePlayPauseCommand,
            PlaybackShortcutAction.SeekBackward => viewModel.SeekBackwardCommand,
            PlaybackShortcutAction.SeekForward => viewModel.SeekForwardCommand,
            PlaybackShortcutAction.IncreaseVolume => viewModel.IncreaseVolumeCommand,
            PlaybackShortcutAction.DecreaseVolume => viewModel.DecreaseVolumeCommand,
            PlaybackShortcutAction.ExitFullscreen => viewModel.ToggleFullscreenCommand,
            _ => null
        };

        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    private static bool IsEditingOrSelectingControl(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        return visual.GetSelfAndVisualAncestors().Any(ancestor =>
            ancestor is TextBox or ComboBox or Slider or ListBox or Button);
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
