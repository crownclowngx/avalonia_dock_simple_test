using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MyAvaloniaManagementCommon.Presentation;
using MySmallTools.Business.SecretVideoPlayer.Playback;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.Views.SecretVideoPlayer.Playback;

/// <summary>
/// 在普通占位区和宿主全屏覆盖层之间迁移唯一播放器视觉树。
/// </summary>
/// <remarks>
/// 本类型只处理 Avalonia/TopLevel 呈现，不拥有播放状态。迁移被串行化，并等待
/// NativeControlHost 完成旧 HWND 销毁后再连接新表面，避免一个 MediaPlayer 同时绑定两个句柄。
/// </remarks>
public sealed class FullscreenPlaybackPresenter
{
    private readonly VideoPlayerControl _owner;
    private readonly ContentControl _normalPlaceholder;
    private readonly Control _playerShell;
    private readonly PlaybackViewportView _viewport;
    private readonly Func<VideoPlayerControlViewModel?> _viewModel;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private IWindowContentFullscreenHost? _fullscreenHost;
    private TopLevel? _fullscreenTopLevel;
    private bool _forcingVisualReset;

    public FullscreenPlaybackPresenter(
        VideoPlayerControl owner,
        ContentControl normalPlaceholder,
        Control playerShell,
        PlaybackViewportView viewport,
        Func<VideoPlayerControlViewModel?> viewModel)
    {
        _owner = owner;
        _normalPlaceholder = normalPlaceholder;
        _playerShell = playerShell;
        _viewport = viewport;
        _viewModel = viewModel;
    }

    public async Task<PlaybackFailure?> ApplyAsync(bool enterFullscreen)
    {
        await _transitionGate.WaitAsync();
        try
        {
            return enterFullscreen
                ? await EnterAsync()
                : await ExitAsync();
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task<PlaybackFailure?> EnterAsync()
    {
        if (_fullscreenHost is not null)
            return null;

        var topLevel = TopLevel.GetTopLevel(_owner);
        if (topLevel is not IWindowContentFullscreenHost fullscreenHost)
        {
            return new PlaybackFailure(
                PlaybackFailureCode.ControlUnavailable,
                "当前宿主窗口不支持内容区全屏。");
        }

        var previousGeneration = _viewport.Surface.CurrentSurfaceToken?.Generation ?? 0;
        var attachment = WaitForNewSurfaceAttachmentAsync(previousGeneration);
        _normalPlaceholder.Content = null;
        await WaitForNativeSurfaceReleaseAsync();

        if (!fullscreenHost.TryPresent(_playerShell, _owner))
        {
            _normalPlaceholder.Content = _playerShell;
            var restoreFailure = await attachment;
            return restoreFailure ?? new PlaybackFailure(
                PlaybackFailureCode.ControlUnavailable,
                "当前窗口已有播放器处于全屏状态。");
        }

        _fullscreenHost = fullscreenHost;
        _fullscreenTopLevel = topLevel;
        _fullscreenTopLevel.AddHandler(
            InputElement.KeyDownEvent,
            OnFullscreenTopLevelKeyDown,
            RoutingStrategies.Tunnel);

        var failure = await attachment;
        if (failure is not null)
        {
            await RollBackFailedEntryAsync(fullscreenHost);
            return failure;
        }

        _owner.Focus();
        return null;
    }

    private async Task<PlaybackFailure?> ExitAsync()
    {
        if (_fullscreenHost is null)
            return null;

        var previousGeneration = _viewport.Surface.CurrentSurfaceToken?.Generation ?? 0;
        var attachment = WaitForNewSurfaceAttachmentAsync(previousGeneration);
        var host = _fullscreenHost;
        if (!host.TryRestore(_owner))
        {
            return new PlaybackFailure(
                PlaybackFailureCode.ControlUnavailable,
                "宿主窗口拒绝归还全屏播放器。");
        }

        _fullscreenHost = null;
        await WaitForNativeSurfaceReleaseAsync();
        _normalPlaceholder.Content = _playerShell;
        RemoveTopLevelHandler();
        var failure = await attachment;
        _owner.Focus();
        return failure;
    }

    private async Task RollBackFailedEntryAsync(IWindowContentFullscreenHost fullscreenHost)
    {
        var previousGeneration = _viewport.Surface.CurrentSurfaceToken?.Generation ?? 0;
        if (!fullscreenHost.TryRestore(_owner))
        {
            ForceReset();
            return;
        }

        var attachment = WaitForNewSurfaceAttachmentAsync(previousGeneration);
        _fullscreenHost = null;
        RemoveTopLevelHandler();
        await WaitForNativeSurfaceReleaseAsync();
        _normalPlaceholder.Content = _playerShell;
        _ = await attachment;
    }

    private static async Task WaitForNativeSurfaceReleaseAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(
            static () => { },
            DispatcherPriority.Background);
    }

    private async Task<PlaybackFailure?> WaitForNewSurfaceAttachmentAsync(long previousGeneration)
    {
        var viewModel = _viewModel();
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
                return;
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

    public void ForceReset()
    {
        if (_forcingVisualReset)
            return;

        _forcingVisualReset = true;
        try
        {
            RemoveTopLevelHandler();
            if (_fullscreenHost is not null)
            {
                _fullscreenHost.TryRestore(_owner);
                _fullscreenHost = null;
            }

            if (_normalPlaceholder.Content is null && _playerShell.Parent is null)
                _normalPlaceholder.Content = _playerShell;
        }
        finally
        {
            _forcingVisualReset = false;
        }
    }

    private void RemoveTopLevelHandler()
    {
        _fullscreenTopLevel?.RemoveHandler(
            InputElement.KeyDownEvent,
            OnFullscreenTopLevelKeyDown);
        _fullscreenTopLevel = null;
    }

    private void OnFullscreenTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        var viewModel = _viewModel();
        if (e.Key != Key.Escape || viewModel?.IsFullscreen != true)
            return;
        if (viewModel.ToggleFullscreenCommand.CanExecute(null))
            viewModel.ToggleFullscreenCommand.Execute(null);
        e.Handled = true;
    }
}
