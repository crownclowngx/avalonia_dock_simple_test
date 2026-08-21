using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Presentation;

/// <summary>将最终 UI SDK 的窄窗口端口映射到当前 Avalonia 桌面主窗口。</summary>
/// <remarks>
/// 服务作为 Host singleton 存在，但构造时不读取 <see cref="Application.Current"/>。插件 Provider
/// 在主窗口创建前即可安全取得同一端口实例，真正调用时再定位当前 Runtime 的主窗口。这样既不把
/// Host 实现或窗口对象交给插件，也不会把测试或设计器环境中“暂无窗口”误判为启动失败。
/// </remarks>
internal sealed class AvaloniaPluginWindowInteraction : IPluginWindowInteraction
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PickOpenFilesAsync(
        FilePickerOpenOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureUiThread();
        cancellationToken.ThrowIfCancellationRequested();

        var window = GetMainWindow();
        if (window is null)
        {
            return [];
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(options);
        // 系统选择器无法可靠强制关闭；返回后再次观察令牌，阻止关闭期间的路径迟到提交。
        cancellationToken.ThrowIfCancellationRequested();
        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<string?> PickSaveFileAsync(
        FilePickerSaveOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureUiThread();
        cancellationToken.ThrowIfCancellationRequested();

        var window = GetMainWindow();
        if (window is null)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(options);
        cancellationToken.ThrowIfCancellationRequested();
        return file?.TryGetLocalPath();
    }

    /// <inheritdoc />
    public async Task<bool> TrySetClipboardTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        EnsureUiThread();
        cancellationToken.ThrowIfCancellationRequested();

        var clipboard = GetMainWindow()?.Clipboard;
        if (clipboard is null)
        {
            return false;
        }

        await clipboard.SetTextAsync(text);
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    private static Avalonia.Controls.Window? GetMainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
        ?.MainWindow;

    private static void EnsureUiThread()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException("插件窗口交互必须在 Avalonia UI 线程调用。");
        }
    }
}
