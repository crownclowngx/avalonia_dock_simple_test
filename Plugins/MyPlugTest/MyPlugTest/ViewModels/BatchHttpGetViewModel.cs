using System.Text;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyPlugTest.Services;

namespace MyPlugTest.ViewModels;

/// <summary>
/// 按文本行顺序执行 HTTP GET 请求的临时文档。
/// </summary>
public sealed class BatchHttpGetViewModel : Document, IDisposable
{
    private readonly IUrlContentService _urlContentService;
    private readonly IDocumentLifetime? _documentLifetime;
    // 本地 CTS 补足直接构造时的 Dispose 取消能力，宿主 ClosingToken 则覆盖真实 Dock 生命周期。
    // 批处理循环使用二者的联合令牌，因此关闭既能中止当前 HTTP，也能阻止进入下一条 URL；
    // 已完成请求的统计不会在关闭后继续刷新，以保持关闭瞬间的状态快照不再变化。
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;
    private string _requestLines = "http://example.com";
    private string _responseContent = string.Empty;
    private string _statusText = "等待执行";
    private bool _isRunning;

    public BatchHttpGetViewModel(
        IUrlContentService urlContentService,
        IDocumentLifetime? documentLifetime = null)
    {
        _urlContentService = urlContentService;
        _documentLifetime = documentLifetime;
        ExecuteRequestsCommand = new AsyncRelayCommand(ExecuteRequestsAsync);
    }

    /// <summary>每个非空行表示一个 GET 请求地址。</summary>
    public string RequestLines
    {
        get => _requestLines;
        set => SetProperty(ref _requestLines, value);
    }

    /// <summary>按执行顺序汇总的响应正文或错误信息。</summary>
    public string ResponseContent
    {
        get => _responseContent;
        private set => SetProperty(ref _responseContent, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public IAsyncRelayCommand ExecuteRequestsCommand { get; }

    private async Task ExecuteRequestsAsync(CancellationToken commandToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            _disposeCts.Token,
            _documentLifetime?.ClosingToken ?? CancellationToken.None);
        var cancellationToken = linked.Token;

        var requests = RequestLines
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select((url, index) => new RequestLine(index + 1, url.Trim()))
            .Where(request => !string.IsNullOrWhiteSpace(request.Value))
            .ToArray();

        if (requests.Length == 0)
        {
            ResponseContent = "没有可执行的网址。";
            StatusText = "请输入至少一个网址";
            return;
        }

        var output = new StringBuilder();
        var succeeded = 0;
        IsRunning = true;
        ResponseContent = string.Empty;

        try
        {
            for (var index = 0; index < requests.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = requests[index];
                StatusText = $"正在执行 {index + 1}/{requests.Length}：{request.Value}";
                output.AppendLine($"===== 第 {request.LineNumber} 行：{request.Value} =====");

                if (!TryNormalizeHttpUrl(request.Value, out var url))
                {
                    output.AppendLine("请求失败：网址必须使用 http 或 https。");
                    output.AppendLine();
                    ResponseContent = output.ToString();
                    continue;
                }

                try
                {
                    var content = await _urlContentService.GetStringAsync(url, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    output.AppendLine(content);
                    succeeded++;
                }
                catch (UrlContentRequestException ex)
                {
                    var statusCode = ex.StatusCode?.ToString() ?? "无";
                    output.AppendLine($"请求失败：状态码 {statusCode}");
                    if (!string.IsNullOrWhiteSpace(ex.ResponseContent))
                    {
                        output.AppendLine(ex.ResponseContent);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    output.AppendLine($"请求异常：{ex.Message}");
                }

                output.AppendLine();
                if (!IsClosing) ResponseContent = output.ToString();
            }

            StatusText = $"执行完成：成功 {succeeded}，失败 {requests.Length - succeeded}，共 {requests.Length} 个请求";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 命令主动取消和 Document 关闭共用同一协作取消路径。取消不会计入失败数量，
            // 也不会把 OperationCanceledException 文本追加到响应区；关闭后的状态由 Scope
            // 释放，不需要为了显示“已取消”而再次触碰已经失效的 ViewModel。
        }
        finally
        {
            if (!IsClosing) IsRunning = false;
        }
    }

    private bool IsClosing => Volatile.Read(ref _disposed) != 0 || _documentLifetime?.IsClosing == true;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        ExecuteRequestsCommand.Cancel();
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    private static bool TryNormalizeHttpUrl(string value, out string url)
    {
        var candidate = value.Contains("://", StringComparison.Ordinal)
            ? value
            : $"http://{value}";

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            url = uri.AbsoluteUri;
            return true;
        }

        url = string.Empty;
        return false;
    }

    private sealed record RequestLine(int LineNumber, string Value);
}
