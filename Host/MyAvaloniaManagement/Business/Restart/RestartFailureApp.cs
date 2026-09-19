using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;

namespace MyAvaloniaManagement.Business.Restart;

/// <summary>助手专用的最小错误界面，不依赖旧进程的窗口或插件；只有失败时才初始化 Avalonia。</summary>
internal sealed class RestartFailureApp(string message, string? logPath) : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var close = new Button { Content = "关闭", HorizontalAlignment = HorizontalAlignment.Right };
            var window = new Window
            {
                Title = "Host 自动重启未完成", Width = 520, Height = 300,
                Content = new StackPanel
                {
                    Margin = new Thickness(24), Spacing = 20,
                    Children =
                    {
                        new TextBlock { Text = message + "\n请确认原 Host 已退出后，再手动打开工作台。已保存的文档和设置会保留。",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        new TextBlock { Text = logPath is null ? "无法写入诊断日志。" : "诊断日志：" + logPath,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap }, close
                    }
                }
            };
            close.Click += (_, _) => window.Close();
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    internal static int Show(Exception exception)
    {
        string? logPath = null;
        // 独立失败记录只包含固定错误码与异常类型；不写会话令牌、参数或异常正文。
        try
        {
            var directory = Path.Combine(Storage.HostDataRootPolicy.ResolveDefault(), "diagnostics");
            Directory.CreateDirectory(directory);
            logPath = Path.Combine(directory, $"restart-{Guid.NewGuid():N}.log");
            File.WriteAllText(logPath,
                $"HOST_RESTART_FAILED type={exception.GetType().Name}");
        }
        catch (Exception) { logPath = null; /* 记录失败仍需给用户可见反馈。 */ }
        var message = exception is OperationCanceledException ? "等待重启交接超时，未启动新的 Host。" :
            exception is InvalidDataException ? "无法确认本次重启请求，自动重启已取消。" :
            "原 Host 未能完成正常退出，或新的 Host 无法启动。";
        AppBuilder.Configure(() => new RestartFailureApp(message, logPath)).UsePlatformDetect().WithInterFont()
            .StartWithClassicDesktopLifetime([]);
        return 1;
    }
}
