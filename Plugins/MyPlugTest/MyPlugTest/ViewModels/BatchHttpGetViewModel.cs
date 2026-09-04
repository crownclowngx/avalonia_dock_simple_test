using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.PluginSdk;
using MyPlugTest.Services;

namespace MyPlugTest.ViewModels;

/// <summary>
/// 按文本行顺序执行 HTTP GET 请求的普通插件 Document 模型。
/// </summary>
/// <remarks>
/// 模型只拥有请求输入、执行状态和协作取消，不继承或操作 Dock。Host 在独立 Scope 中创建实例并通过
/// <see cref="IPluginDocument"/> 读取展示状态；关闭令牌与本地释放令牌共同阻止迟到网络结果更新界面。
/// </remarks>
public sealed class BatchHttpGetViewModel : ObservableObject, IPluginDocument, IDisposable
{
    private readonly IUrlContentService _urlContentService;
    private readonly IBatchHttpFileService _fileService;
    private readonly IDocumentLifetime _documentLifetime;
    // 本地 CTS 补足直接构造时的 Dispose 取消能力，宿主 ClosingToken 则覆盖真实 Dock 生命周期。
    // 批处理循环使用二者的联合令牌，因此关闭既能中止当前 HTTP，也能阻止进入下一条 URL；
    // 已完成请求的统计不会在关闭后继续刷新，以保持关闭瞬间的状态快照不再变化。
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;
    private string _requestLines = "http://example.com";
    private string _responseContent = string.Empty;
    private string _statusText = "等待执行";
    private bool _isRunning;
    private bool _isFileBatchMode;
    private string _inputFilePath = string.Empty;
    private string _outputFilePath = string.Empty;
    private int _totalCount;
    private int _processedCount;
    private int _succeededCount;
    private int _failedCount;
    private int _writtenCount;
    private string _title = "逐行 HTTP GET";

    public BatchHttpGetViewModel(
        IUrlContentService urlContentService,
        IBatchHttpFileService fileService,
        IDocumentLifetime documentLifetime)
    {
        _urlContentService = urlContentService ?? throw new ArgumentNullException(nameof(urlContentService));
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _documentLifetime = documentLifetime ?? throw new ArgumentNullException(nameof(documentLifetime));
        ExecuteRequestsCommand = new AsyncRelayCommand(ExecuteRequestsAsync);
        SelectInputFileCommand = new AsyncRelayCommand(SelectInputFileAsync);
    }

    /// <inheritdoc />
    public DocumentPresentationState Presentation => new(_title);

    /// <inheritdoc />
    public event EventHandler? PresentationChanged;

    /// <inheritdoc />
    public ValueTask InitializeAsync(
        DocumentActivation activation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation);
        cancellationToken.ThrowIfCancellationRequested();
        if (activation is not NewDocumentActivation)
        {
            // 批量请求结果不属于 Document envelope；恢复输入不能被静默解释为空白任务。
            throw new NotSupportedException("逐行 HTTP GET 只支持新建激活。");
        }

        var title = string.IsNullOrWhiteSpace(activation.Title) ? "逐行 HTTP GET" : activation.Title;
        if (!string.Equals(_title, title, StringComparison.Ordinal))
        {
            _title = title;
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
        return ValueTask.CompletedTask;
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
        private set
        {
            if (!SetProperty(ref _isRunning, value)) return;
            OnPropertyChanged(nameof(CanInteract));
        }
    }

    /// <summary>启用只读写文件、不向响应框提交正文的高吞吐批处理模式。</summary>
    public bool IsFileBatchMode
    {
        get => _isFileBatchMode;
        set
        {
            if (!SetProperty(ref _isFileBatchMode, value)) return;
            OnPropertyChanged(nameof(IsManualMode));
            OnPropertyChanged(nameof(ExecuteButtonText));
            if (value) ResponseContent = string.Empty;
        }
    }

    public bool IsManualMode => !IsFileBatchMode;

    public bool CanInteract => !IsRunning;

    public string ExecuteButtonText => IsFileBatchMode ? "执行文件批处理 GET" : "按行执行 GET";

    public string InputFilePath
    {
        get => _inputFilePath;
        private set => SetProperty(ref _inputFilePath, value);
    }

    public string OutputFilePath
    {
        get => _outputFilePath;
        private set => SetProperty(ref _outputFilePath, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        private set => SetProperty(ref _totalCount, value);
    }

    public int ProcessedCount
    {
        get => _processedCount;
        private set => SetProperty(ref _processedCount, value);
    }

    public int SucceededCount
    {
        get => _succeededCount;
        private set => SetProperty(ref _succeededCount, value);
    }

    public int FailedCount
    {
        get => _failedCount;
        private set => SetProperty(ref _failedCount, value);
    }

    public int WrittenCount
    {
        get => _writtenCount;
        private set => SetProperty(ref _writtenCount, value);
    }

    public IAsyncRelayCommand ExecuteRequestsCommand { get; }
    public IAsyncRelayCommand SelectInputFileCommand { get; }

    private async Task ExecuteRequestsAsync(CancellationToken commandToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            _disposeCts.Token,
            _documentLifetime.ClosingToken);
        var cancellationToken = linked.Token;

        // 命令可能在关闭动画尚未解绑控件时被再次触发。已进入关闭态时直接返回，
        // 不再连“请输入网址”这类本地校验结果也回写到即将释放的模型。
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (IsFileBatchMode)
        {
            await ExecuteFileBatchAsync(cancellationToken);
            return;
        }

        var requests = ParseRequests(RequestLines.ReplaceLineEndings("\n").Split('\n'));

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
        ResetStatistics(requests.Length);

        try
        {
            for (var index = 0; index < requests.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = requests[index];
                StatusText = $"正在执行 {index + 1}/{requests.Length}：{request.Value}";
                var result = await RequestOneAsync(request, cancellationToken);
                output.Append(result.Text);
                if (result.Succeeded) succeeded++;
                UpdateStatistics(index + 1, succeeded);
                if (!IsClosing) ResponseContent = output.ToString();
            }

            if (!IsClosing)
            {
                StatusText = $"执行完成：成功 {succeeded}，失败 {requests.Length - succeeded}，共 {requests.Length} 个请求";
            }
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

    private async Task SelectInputFileAsync(CancellationToken commandToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            _disposeCts.Token,
            _documentLifetime.ClosingToken);
        var cancellationToken = linked.Token;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = await _fileService.PickInputFileAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(path) || IsClosing) return;

            InputFilePath = path;
            OutputFilePath = string.Empty;
            ResetStatistics(0);
            StatusText = "已选择地址文件，等待执行";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsClosing) StatusText = $"选择文件失败：{exception.Message}";
        }
    }

    private async Task ExecuteFileBatchAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(InputFilePath))
        {
            StatusText = "请先选择包含 GET 地址的文本文件";
            return;
        }

        var suggestedFileName = $"{Path.GetFileNameWithoutExtension(InputFilePath)}_responses.txt";
        string? outputPath;
        try
        {
            outputPath = await _fileService.PickOutputFileAsync(
                suggestedFileName,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            if (!IsClosing) StatusText = $"选择输出文件失败：{exception.Message}";
            return;
        }

        if (string.IsNullOrWhiteSpace(outputPath) || IsClosing)
        {
            StatusText = "已取消文件批处理";
            return;
        }

        IsRunning = true;
        ResponseContent = string.Empty;
        OutputFilePath = outputPath;
        ResetStatistics(0);

        try
        {
            StatusText = "正在从文件读取请求地址…";
            var lines = await _fileService.ReadAllLinesAsync(InputFilePath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var requests = ParseRequests(lines);
            ResetStatistics(requests.Length);
            if (requests.Length == 0)
            {
                StatusText = "输入文件中没有可执行的网址";
                return;
            }

            var progress = new Progress<BatchProgress>(ReportFileProgress);
            var result = await Task.Run(
                () => ExecuteRequestsToMemoryAsync(requests, progress, cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            UpdateStatistics(requests.Length, result.Succeeded);
            StatusText = $"请求完成，正在一次性写入 {requests.Length} 条结果…";
            await _fileService.WriteAllTextAtomicallyAsync(
                outputPath,
                result.Content,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            WrittenCount = requests.Length;
            StatusText = $"文件批处理完成：成功 {result.Succeeded}，失败 {result.Failed}，已写入 {WrittenCount} 条结果";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!IsClosing)
                StatusText = $"已取消：已处理 {ProcessedCount}/{TotalCount}，未写入输出文件";
        }
        catch (Exception exception)
        {
            if (!IsClosing) StatusText = $"文件批处理失败，未写入输出文件：{exception.Message}";
        }
        finally
        {
            if (!IsClosing) IsRunning = false;
        }
    }

    private async Task<BatchExecutionResult> ExecuteRequestsToMemoryAsync(
        IReadOnlyList<RequestLine> requests,
        IProgress<BatchProgress> progress,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var succeeded = 0;
        var reportTimer = Stopwatch.StartNew();

        for (var index = 0; index < requests.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RequestOneAsync(requests[index], cancellationToken)
                .ConfigureAwait(false);
            output.Append(result.Text);
            if (result.Succeeded) succeeded++;

            var processed = index + 1;
            if (processed == requests.Count || reportTimer.ElapsedMilliseconds >= 150)
            {
                progress.Report(new BatchProgress(processed, succeeded, requests.Count));
                reportTimer.Restart();
            }
        }

        return new BatchExecutionResult(
            output.ToString(),
            succeeded,
            requests.Count - succeeded);
    }

    private async Task<RequestResult> RequestOneAsync(
        RequestLine request,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        output.AppendLine($"===== 第 {request.LineNumber} 行：{request.Value} =====");

        if (!TryNormalizeHttpUrl(request.Value, out var url))
        {
            output.AppendLine("请求失败：网址必须使用 http 或 https。");
            output.AppendLine();
            return new RequestResult(output.ToString(), false);
        }

        var succeeded = false;
        try
        {
            var content = await _urlContentService.GetStringAsync(url, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            output.AppendLine(content);
            succeeded = true;
        }
        catch (UrlContentRequestException ex)
        {
            var statusCode = ex.StatusCode?.ToString() ?? "无";
            output.AppendLine($"请求失败：状态码 {statusCode}");
            if (!string.IsNullOrWhiteSpace(ex.ResponseContent))
                output.AppendLine(ex.ResponseContent);
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
        return new RequestResult(output.ToString(), succeeded);
    }

    private void ReportFileProgress(BatchProgress progress)
    {
        if (IsClosing || !IsRunning) return;
        UpdateStatistics(progress.Processed, progress.Succeeded);
        StatusText = $"正在后台请求：已处理 {progress.Processed}/{progress.Total}，成功 {progress.Succeeded}，失败 {progress.Processed - progress.Succeeded}";
    }

    private void ResetStatistics(int total)
    {
        TotalCount = total;
        ProcessedCount = 0;
        SucceededCount = 0;
        FailedCount = 0;
        WrittenCount = 0;
    }

    private void UpdateStatistics(int processed, int succeeded)
    {
        ProcessedCount = processed;
        SucceededCount = succeeded;
        FailedCount = processed - succeeded;
    }

    private static RequestLine[] ParseRequests(IEnumerable<string> lines) =>
        lines.Select((url, index) => new RequestLine(index + 1, url.Trim()))
            .Where(request => !string.IsNullOrWhiteSpace(request.Value))
            .ToArray();

    private bool IsClosing => Volatile.Read(ref _disposed) != 0 || _documentLifetime.IsClosing;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        ExecuteRequestsCommand.Cancel();
        SelectInputFileCommand.Cancel();
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
    private sealed record RequestResult(string Text, bool Succeeded);
    private sealed record BatchProgress(int Processed, int Succeeded, int Total);
    private sealed record BatchExecutionResult(string Content, int Succeeded, int Failed);
}
