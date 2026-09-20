using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MyAvaloniaManagement.ViewModels.Startup;

namespace MyAvaloniaManagement.Views;

/// <summary>轻量启动窗口。窗口只拥有动画时钟和关闭意图；是否取消、何时释放资源由启动协调器决定。</summary>
internal sealed partial class SplashWindow : Window
{
    private readonly DispatcherTimer _animation = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private readonly Stopwatch _clock = new();
    private readonly TranslateTransform _translate = new();
    private readonly RotateTransform _rotate = new();
    private bool _allowClose;
    internal event Action? CancellationRequested;
    internal bool AnimationRunning => _animation.IsEnabled;

    internal SplashWindow(SplashViewModel model, bool? animate = null)
    {
        InitializeComponent();
        DataContext = model;
        Feather.RenderTransform = new TransformGroup { Children = { _rotate, _translate } };
        _animation.Tick += (_, _) =>
        {
            var wave = Math.Sin(_clock.Elapsed.TotalSeconds * Math.PI * 2 / 2.4);
            _translate.Y = wave * 3;
            _rotate.Angle = wave * 2;
        };
        Opened += (_, _) => { if (animate ?? MotionEnabled()) { _clock.Start(); _animation.Start(); } };
        Closed += (_, _) => StopAnimation();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_allowClose) return;
        e.Cancel = true;
        CancellationRequested?.Invoke();
    }

    internal void StopAnimation()
    {
        _animation.Stop();
        _clock.Stop();
        _translate.Y = 0;
        _rotate.Angle = 0;
    }

    internal void CompleteAndClose() { _allowClose = true; StopAnimation(); Close(); }

    /// <summary>Windows 遵守系统客户区动画偏好；未知平台保守展示。环境开关用于桌面辅助需求和自动化。</summary>
    private static bool MotionEnabled()
    {
        if (Environment.GetEnvironmentVariable("MYAVALONIA_REDUCED_MOTION") == "1") return false;
        if (!OperatingSystem.IsWindows()) return true;
        return !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
