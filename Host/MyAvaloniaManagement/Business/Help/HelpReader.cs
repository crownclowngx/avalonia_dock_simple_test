using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using MyAvaloniaManagement.Business.Storage;

namespace MyAvaloniaManagement.Business.Help;

internal sealed record HelpReaderPresentation(bool Dark, int FontSize, bool ReduceMotion);
internal sealed record HelpReaderMessage(string Type, string? RequestId = null, string? Value = null,
    string? Source = null, double ScrollTop = 0, string? Anchor = null, bool FullTextExpanded = false);

/// <summary>真实 WebView 与 Headless 测试之间的渲染边界，不暴露给插件。</summary>
internal interface IHelpReader : IDisposable
{
    Control View { get; }
    event EventHandler<HelpReaderMessage>? Message;
    Task DisplayAsync(HelpPage page, HelpPagePosition position, string query, string anchor,
        HelpReaderPresentation presentation, CancellationToken cancellationToken);
    Task PresentAsync(HelpReaderPresentation presentation);
    Task SetSuspendedAsync(bool suspended);
}

internal sealed class HelpWebReader : IHelpReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Grid _root = new();
    private readonly TextBox _plain = new() { IsReadOnly = true, AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Margin = new Thickness(20) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 14) };
    private readonly Button _retry = new() { Content = "重试富文本阅读", Margin = new Thickness(20, 0, 20, 12) };
    private readonly Grid _fallback = new() { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Uri _pageUri = new(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "HelpWeb", "index.html")));
    private NativeWebView? _browser;
    private TaskCompletionSource _ready = NewCompletion();
    private TaskCompletionSource? _rendered;
    private HelpPage? _page;
    private HelpPagePosition _position = new();
    private HelpReaderPresentation _presentation = new(false, 17, false);
    private string _query = "", _anchor = "";
    private bool _disposed;

    public HelpWebReader()
    {
        _fallback.Children.Add(_status);
        Grid.SetRow(_retry, 1); _fallback.Children.Add(_retry);
        Grid.SetRow(_plain, 2); _fallback.Children.Add(_plain);
        _root.Children.Add(_fallback);
        _retry.Click += RetryClicked;
    }

    public Control View => _root;
    public event EventHandler<HelpReaderMessage>? Message;
    internal NativeWebView? Browser => _browser;
    internal bool IsFallbackVisible => _fallback.IsVisible;

    public async Task DisplayAsync(HelpPage page, HelpPagePosition position, string query, string anchor,
        HelpReaderPresentation presentation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = page; _position = position; _query = query; _anchor = anchor; _presentation = presentation;
        _plain.Text = page.PlainText;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        try
        {
            EnsureBrowser();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            token.ThrowIfCancellationRequested();
            if (_page.RequestId != page.RequestId) return;
            var completed = _rendered = NewCompletion();
            var payload = JsonSerializer.Serialize(new { page, position, query, anchor, presentation }, Json);
            await _browser!.InvokeScript("window.hostHelp.openPage(" + payload + "); 'accepted'");
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(40), token);
            if (_page.RequestId != page.RequestId || _disposed) return;
            _fallback.IsVisible = false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (_disposed || token.IsCancellationRequested || _page.RequestId != page.RequestId) return;
            Console.Error.WriteLine("HelpReader errorCode=HELP_RENDER_FAILED type=" + exception.GetType().Name);
            _status.Text = "富文本阅读器暂时不可用。下面保留完整文字，可选择并复制。请检查本地帮助资源与 Microsoft Edge WebView2 Runtime 后重试。";
            _fallback.IsVisible = true;
            DestroyBrowser();
        }
    }

    private void EnsureBrowser()
    {
        if (_browser is not null) return;
        if (!File.Exists(_pageUri.LocalPath)) throw new FileNotFoundException("Offline help page is missing.");
        _status.Text = "正在准备离线公式与图示…";
        _fallback.IsVisible = true;
        _ready = NewCompletion();
        var browser = _browser = new NativeWebView();
        browser.EnvironmentRequested += ConfigureEnvironment;
        browser.WebMessageReceived += ReceiveMessage;
        browser.NavigationStarted += NavigationStarted;
        browser.NewWindowRequested += NewWindowRequested;
        _root.Children.Insert(0, browser);
        browser.Source = _pageUri;
    }

    private void ConfigureEnvironment(object? sender, WebViewEnvironmentRequestedEventArgs args)
    {
        args.EnableDevTools = false;
        if (args is WindowsWebView2EnvironmentRequestedEventArgs windows)
        {
            windows.UserDataFolder = Path.Combine(HostDataRootPolicy.ResolveDefault(), "HelpWebView");
            windows.ProfileName = "HostHelp";
            windows.IsInPrivateModeEnabled = true;
        }
    }

    private void ReceiveMessage(object? sender, WebMessageReceivedEventArgs args)
    {
        if (_disposed || !ReferenceEquals(sender, _browser)) return;
        HelpReaderMessage? message;
        if (string.IsNullOrEmpty(args.Body) || args.Body.Length > 128 * 1024) return;
        try { message = JsonSerializer.Deserialize<HelpReaderMessage>(args.Body, Json); }
        catch (JsonException) { return; }
        if (message is null) return;
        if (message.Type == "ready") { _ready.TrySetResult(); return; }
        if (message.RequestId != _page?.RequestId) return;
        if (message.Type == "rendered") { _rendered?.TrySetResult(); return; }
        if (message.Type == "render-error") { _rendered?.TrySetException(new InvalidOperationException("Help page render failed.")); return; }
        Message?.Invoke(this, message);
    }

    private void NavigationStarted(object? sender, WebViewNavigationStartingEventArgs args)
    {
        if (args.Request == _pageUri || args.Request?.ToString() == "about:blank") return;
        args.Cancel = true;
    }

    private static void NewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs args) => args.Handled = true;

    private async void RetryClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_disposed || _page is null) return;
        _retry.IsEnabled = false;
        DestroyBrowser();
        try { await DisplayAsync(_page, _position, _query, _anchor, _presentation, _lifetime.Token); }
        finally { if (!_disposed) _retry.IsEnabled = true; }
    }

    public Task PresentAsync(HelpReaderPresentation presentation)
    {
        _presentation = presentation;
        _plain.FontSize = presentation.FontSize;
        return ScriptAsync("window.hostHelp?.present(" + JsonSerializer.Serialize(presentation, Json) + ")");
    }

    public Task SetSuspendedAsync(bool suspended) => ScriptAsync("window.hostHelp?.suspend(" + (suspended ? "true" : "false") + ")");

    private async Task ScriptAsync(string script)
    {
        if (_disposed || _browser is null || !_ready.Task.IsCompletedSuccessfully) return;
        try { await _browser.InvokeScript(script); }
        catch (Exception exception) { Console.Error.WriteLine("HelpReader errorCode=HELP_SCRIPT_FAILED type=" + exception.GetType().Name); }
    }

    private void DestroyBrowser()
    {
        if (_browser is null) return;
        _browser.EnvironmentRequested -= ConfigureEnvironment;
        _browser.WebMessageReceived -= ReceiveMessage;
        _browser.NavigationStarted -= NavigationStarted;
        _browser.NewWindowRequested -= NewWindowRequested;
        _root.Children.Remove(_browser); // Detaching destroys the package-owned native adapter.
        _browser = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _ready.TrySetCanceled(); _rendered?.TrySetCanceled();
        _retry.Click -= RetryClicked;
        DestroyBrowser();
        _root.Children.Clear(); _plain.Text = null; _page = null; Message = null;
        _lifetime.Dispose();
    }

    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
