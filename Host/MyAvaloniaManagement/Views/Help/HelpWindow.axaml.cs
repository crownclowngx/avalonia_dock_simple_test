using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using MyAvaloniaManagement.Business.Help;

namespace MyAvaloniaManagement.Views.Help;

internal sealed partial class HelpWindow : Window
{
    private readonly HelpContentCatalog _catalog;
    private readonly HelpMarkdownRenderer _renderer;
    private readonly IHelpReader _reader;
    private readonly List<string> _history = [];
    private CancellationTokenSource? _load, _search;
    private int _historyIndex = -1;
    private bool _closed, _selecting, _restored;
    private string _requestId = string.Empty;
    internal HelpReadingState ReadingState { get; }
    internal Task CurrentLoad { get; private set; } = Task.CompletedTask;

    internal HelpWindow(HelpContentCatalog catalog, HelpReadingState state, IHelpReader reader)
    {
        _catalog = catalog; _renderer = new(catalog); ReadingState = state; _reader = reader;
        InitializeComponent();
        Width = state.Width; Height = state.Height;
        ReduceMotionBox.IsChecked = state.ReduceMotion;
        ChapterList.ItemsSource = HelpContentCatalog.Chapters;
        ReaderHost.Content = reader.View;
        reader.Message += ReaderMessage;
        ChapterList.SelectionChanged += ChapterSelected;
        SearchResults.SelectionChanged += SearchSelected;
        SearchBox.TextChanged += SearchChanged;
        BackButton.Click += (_, _) => TraverseHistory(-1);
        ForwardButton.Click += (_, _) => TraverseHistory(1);
        SmallerButton.Click += (_, _) => ChangeFont(-1);
        LargerButton.Click += (_, _) => ChangeFont(1);
        ReduceMotionBox.IsCheckedChanged += (_, _) =>
        {
            ReadingState.ReduceMotion = ReduceMotionBox.IsChecked == true;
            _ = _reader.PresentAsync(Presentation());
        };
        ActualThemeVariantChanged += ThemeChanged;
        PropertyChanged += WindowPropertyChanged;
        PositionChanged += (_, _) => SaveNormalBounds();
        Opened += (_, _) =>
        {
            RestoreBounds();
            Navigate(_catalog.Find(state.ArticleId)?.Id ?? "product");
        };
        Closed += WindowClosed;
        AddHandler(KeyDownEvent, PreviewKeyDown, RoutingStrategies.Tunnel);
    }

    internal void Navigate(string id, string query = "", string anchor = "", bool recordHistory = true)
    {
        if (_closed) return;
        var article = _catalog.Find(id);
        if (article is null) { ReadingStatus.Text = "此参考文档未随当前版本收录。"; return; }
        if (recordHistory && (_historyIndex < 0 || _history[_historyIndex] != id))
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            _history.Add(id); _historyIndex = _history.Count - 1;
        }
        BackButton.IsEnabled = _historyIndex > 0;
        ForwardButton.IsEnabled = _historyIndex < _history.Count - 1;
        ReadingState.ArticleId = id;
        _selecting = true;
        ChapterList.SelectedItem = HelpContentCatalog.Chapters.FirstOrDefault(a => a.Id == id);
        _selecting = false;
        Breadcrumb.Text = "帮助 / " + article.Title;
        ReadingStatus.Text = "正在载入…";
        _load?.Cancel(); _load?.Dispose(); _load = new CancellationTokenSource();
        _requestId = Guid.NewGuid().ToString("N");
        CurrentLoad = LoadAsync(article, _requestId, query, anchor, _load.Token);
    }

    private async Task LoadAsync(HelpArticle article, string requestId, string query, string anchor, CancellationToken token)
    {
        try
        {
            var page = await Task.Run(() => _renderer.Render(article, requestId, token), token);
            if (_closed || token.IsCancellationRequested || requestId != _requestId) return;
            await _reader.DisplayAsync(page, ReadingState.PositionFor(article.Id), query, anchor, Presentation(), token);
            if (_closed || token.IsCancellationRequested || requestId != _requestId) return;
            ReadingStatus.Text = query.Length > 0 ? "搜索定位：“" + query + "”" : article.Description;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (_closed || token.IsCancellationRequested) return;
            ReadingStatus.Text = "文章暂时无法载入，请重新选择章节。";
            Console.Error.WriteLine("Help errorCode=HELP_CONTENT_FAILED type=" + exception.GetType().Name);
        }
    }

    private void ChapterSelected(object? sender, SelectionChangedEventArgs args)
    {
        if (!_selecting && ChapterList.SelectedItem is HelpArticle article) Navigate(article.Id);
    }

    private void SearchSelected(object? sender, SelectionChangedEventArgs args)
    {
        if (SearchResults.SelectedItem is HelpSearchResult result) Navigate(result.ArticleId, result.Query);
    }

    private async void SearchChanged(object? sender, TextChangedEventArgs args)
    {
        _search?.Cancel(); _search?.Dispose(); _search = new CancellationTokenSource();
        var token = _search.Token;
        var query = SearchBox.Text?.Trim() ?? "";
        ChapterList.IsVisible = query.Length == 0;
        SearchResults.IsVisible = query.Length > 0;
        if (query.Length == 0) { SidebarStatus.Text = "本地阅读 · 与主窗口并行使用"; return; }
        try
        {
            await Task.Delay(180, token);
            var results = await Task.Run(() => _catalog.Search(query, token), token);
            if (_closed || token.IsCancellationRequested) return;
            SearchResults.ItemsSource = results;
            SidebarStatus.Text = results.Count == 0 ? "没有找到匹配内容" : $"找到 {results.Count} 篇相关文章";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async void ReaderMessage(object? sender, HelpReaderMessage message)
    {
        if (_closed || message.RequestId != _requestId) return;
        switch (message.Type)
        {
            case "position":
                var position = ReadingState.PositionFor(ReadingState.ArticleId);
                position.ScrollTop = double.IsFinite(message.ScrollTop) ? Math.Max(0, message.ScrollTop) : 0;
                position.Anchor = message.Anchor ?? "";
                position.FullTextExpanded = message.FullTextExpanded;
                break;
            case "search": SearchBox.Focus(); SearchBox.SelectAll(); break;
            case "copy":
                if (Clipboard is not null && message.Value is { Length: <= 65536 })
                {
                    try { await Clipboard.SetTextAsync(message.Value); }
                    catch (Exception exception) { ReadingStatus.Text = "复制未完成：" + exception.GetType().Name; }
                }
                break;
            case "link" when message.Value is not null:
                if (Uri.TryCreate(message.Value, UriKind.Absolute, out var external) && external.Scheme is "https" or "http")
                {
                    try { Process.Start(new ProcessStartInfo(external.AbsoluteUri) { UseShellExecute = true }); }
                    catch (Exception exception) { ReadingStatus.Text = "无法打开外部链接：" + exception.GetType().Name; }
                    break;
                }
                var target = _catalog.ResolveLink(message.Source, message.Value);
                if (target is null) { ReadingStatus.Text = "此链接指向未随 Host 收录的资料：" + message.Value; break; }
                var separator = target.IndexOf('#');
                Navigate(separator < 0 ? target : separator == 0 ? ReadingState.ArticleId : target[..separator],
                    anchor: separator < 0 ? "" : Uri.UnescapeDataString(target[(separator + 1)..]));
                break;
        }
    }

    private void TraverseHistory(int delta)
    {
        var next = _historyIndex + delta;
        if (next < 0 || next >= _history.Count) return;
        _historyIndex = next;
        Navigate(_history[next], recordHistory: false);
    }

    private void ChangeFont(int delta)
    {
        ReadingState.FontSize = Math.Clamp(ReadingState.FontSize + delta, 14, 26);
        _ = _reader.PresentAsync(Presentation());
    }

    private HelpReaderPresentation Presentation() => new(ActualThemeVariant == ThemeVariant.Dark,
        ReadingState.FontSize, ReadingState.ReduceMotion);
    private void ThemeChanged(object? sender, EventArgs args) => _ = _reader.PresentAsync(Presentation());

    private void PreviewKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.F && args.KeyModifiers.HasFlag(KeyModifiers.Control))
        { SearchBox.Focus(); SearchBox.SelectAll(); args.Handled = true; }
        else if (args.Key == Key.F1) args.Handled = true;
        else if (args.Key == Key.Escape && SearchBox.IsFocused)
        { SearchBox.Text = ""; ChapterList.Focus(); args.Handled = true; }
    }

    private void WindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (_closed) return;
        if (args.Property == WindowStateProperty)
        {
            if (WindowState != WindowState.Minimized) ReadingState.Maximized = WindowState == WindowState.Maximized;
            _ = _reader.SetSuspendedAsync(WindowState == WindowState.Minimized);
        }
        if (args.Property == BoundsProperty) SaveNormalBounds();
    }

    private void SaveNormalBounds()
    {
        if (_closed || !_restored || WindowState != WindowState.Normal || !IsVisible) return;
        ReadingState.Width = Width; ReadingState.Height = Height;
        ReadingState.X = Position.X; ReadingState.Y = Position.Y;
    }

    private void RestoreBounds()
    {
        var location = ReadingState.X is int x && ReadingState.Y is int y ? new PixelPoint(x, y) : Position;
        var screen = Screens.ScreenFromPoint(location) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            var area = screen.WorkingArea;
            var scale = screen.Scaling;
            Width = Math.Min(ReadingState.Width, area.Width / scale);
            Height = Math.Min(ReadingState.Height, area.Height / scale);
            MinWidth = Math.Min(640, area.Width / scale); MinHeight = Math.Min(480, area.Height / scale);
            Position = new PixelPoint(Math.Clamp(location.X, area.X, Math.Max(area.X, area.Right - (int)(Width * scale))),
                Math.Clamp(location.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)(Height * scale))));
        }
        if (ReadingState.Maximized) WindowState = WindowState.Maximized;
        _restored = true;
    }

    private void WindowClosed(object? sender, EventArgs args)
    {
        if (_closed) return;
        _closed = true;
        _load?.Cancel(); _load?.Dispose(); _search?.Cancel(); _search?.Dispose();
        ActualThemeVariantChanged -= ThemeChanged;
        PropertyChanged -= WindowPropertyChanged;
        _reader.Message -= ReaderMessage;
        _reader.Dispose(); ReaderHost.Content = null;
    }
}
