using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using MyAvaloniaManagement.Business.Diagnostics;

namespace MyAvaloniaManagement.Views;

/// <summary>
/// 在宿主组合失败时显示的最小错误窗口。
/// </summary>
/// <remarks>
/// 该窗口不得解析主工作台服务，也不得实例化任何插件 ViewModel；否则错误展示路径会再次
/// 经过已经失败的组合根。界面仅使用启动前保存的不可变诊断快照。
/// </remarks>
internal partial class StartupFailureWindow : Window
{
    private readonly HostStartupFailureContext _context;
    private readonly string _copyText;

    internal StartupFailureWindow(HostStartupFailureContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        InitializeComponent();

        SummaryText.Text = $"检测到 {context.Diagnostics.Count} 条启动诊断。为避免使用不完整的插件组合，宿主没有创建主工作台。";
        LogPathText.Text = context.LogPath is null
            ? "本次诊断日志无法写入磁盘，请使用“复制诊断”保留当前摘要。"
            : $"诊断日志：{context.LogPath}";
        OpenLogButton.IsEnabled = context.LogPath is not null;

        var lines = context.Diagnostics.Select(FormatForUser).ToArray();
        DiagnosticList.ItemsSource = lines;
        _copyText = string.Join(Environment.NewLine + Environment.NewLine, lines) +
                    Environment.NewLine +
                    (context.LogPath is null ? "日志：未写入" : $"日志：{context.LogPath}");
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        var clipboard = Clipboard;
        if (clipboard is null)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(_copyText);
        }
        catch (Exception)
        {
            // 剪贴板由桌面环境提供，失败不能关闭唯一的启动诊断窗口。
            CopyButton.Content = "复制失败";
        }
    }

    private void OnOpenLogClicked(object? sender, RoutedEventArgs e)
    {
        if (_context.LogPath is not { } logPath)
        {
            return;
        }

        var directory = Path.GetDirectoryName(logPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(directory)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            OpenLogButton.Content = "无法打开目录";
        }
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();

    private static string FormatForUser(HostDiagnosticRecord item)
    {
        var subject = item.PluginId ?? item.PluginDirectory ?? item.AssemblyName ?? item.StableId ?? "宿主";
        return $"[{item.Code}] {ToPhaseText(item.Phase)} · {subject}{Environment.NewLine}{item.UserMessage}";
    }

    private static string ToPhaseText(HostDiagnosticPhase phase) => phase switch
    {
        HostDiagnosticPhase.DiagnosticInfrastructure => "诊断设施",
        HostDiagnosticPhase.PluginRootDiscovery => "插件目录发现",
        HostDiagnosticPhase.PluginAssemblyLoad => "插件程序集加载",
        HostDiagnosticPhase.PluginTypePreflight => "插件类型预检",
        HostDiagnosticPhase.PluginModuleDiscovery => "插件模块发现",
        HostDiagnosticPhase.PluginServiceRegistration => "插件服务注册",
        HostDiagnosticPhase.HostContainerBuild => "宿主容器构建",
        HostDiagnosticPhase.ExtensionDiscovery => "扩展组合",
        HostDiagnosticPhase.PluginLifecycle => "插件生命周期",
        HostDiagnosticPhase.Layout => "布局恢复",
        HostDiagnosticPhase.HostBootstrap => "宿主启动",
        _ => phase.ToString(),
    };
}
