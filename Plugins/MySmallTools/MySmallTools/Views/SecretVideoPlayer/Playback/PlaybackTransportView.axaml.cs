using Avalonia;
using Avalonia.Controls;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.Views.SecretVideoPlayer.Playback;

/// <summary>播放器日常控制视图；导航上下文由媒体库按需注入。</summary>
public partial class PlaybackTransportView : UserControl
{
    public static readonly StyledProperty<IPlaybackNavigationContext?> NavigationContextProperty =
        AvaloniaProperty.Register<PlaybackTransportView, IPlaybackNavigationContext?>(
            nameof(NavigationContext));

    public static readonly StyledProperty<bool> HasNavigationContextProperty =
        AvaloniaProperty.Register<PlaybackTransportView, bool>(nameof(HasNavigationContext));

    public PlaybackTransportView()
    {
        InitializeComponent();
    }

    public IPlaybackNavigationContext? NavigationContext
    {
        get => GetValue(NavigationContextProperty);
        set => SetValue(NavigationContextProperty, value);
    }

    public bool HasNavigationContext
    {
        get => GetValue(HasNavigationContextProperty);
        set => SetValue(HasNavigationContextProperty, value);
    }
}
