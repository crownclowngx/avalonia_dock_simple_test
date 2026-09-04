using MyPlugTest.Services;
using MyPlugTest.ViewModels;

namespace MyAvaloniaManagement.PluginTests;

public sealed class BatchHttpGetDocumentTests
{
    [Fact]
    public async Task 请求严格按非空行顺序执行且单行失败不阻断后续行()
    {
        var service = new RecordingUrlContentService();
        using var lifetime = new TestPluginDocumentLifetime();
        using var viewModel = new BatchHttpGetViewModel(service, new StubBatchHttpFileService(), lifetime)
        {
            RequestLines = "one.test/path\n\nhttps://two.test/fail\r\nhttp://three.test/ok",
        };

        await viewModel.ExecuteRequestsCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                "http://one.test/path",
                "https://two.test/fail",
                "http://three.test/ok",
            ],
            service.RequestedUrls);
        Assert.Contains("第 1 行：one.test/path", viewModel.ResponseContent);
        Assert.Contains("第 3 行：https://two.test/fail", viewModel.ResponseContent);
        Assert.Contains("第 4 行：http://three.test/ok", viewModel.ResponseContent);
        Assert.Contains("状态码 503", viewModel.ResponseContent);
        Assert.Equal("执行完成：成功 2，失败 1，共 3 个请求", viewModel.StatusText);
        Assert.False(viewModel.IsRunning);
    }

    [Fact]
    public async Task 非Http协议只记录失败而不调用请求服务()
    {
        var service = new RecordingUrlContentService();
        using var lifetime = new TestPluginDocumentLifetime();
        using var viewModel = new BatchHttpGetViewModel(service, new StubBatchHttpFileService(), lifetime)
        {
            RequestLines = "ftp://example.com/file",
        };

        await viewModel.ExecuteRequestsCommand.ExecuteAsync(null);

        Assert.Empty(service.RequestedUrls);
        Assert.Contains("网址必须使用 http 或 https", viewModel.ResponseContent);
        Assert.Equal("执行完成：成功 0，失败 1，共 1 个请求", viewModel.StatusText);
    }

    [Fact]
    public async Task DisposingDocumentCancelsInFlightRequestWithoutRenderingAnError()
    {
        var service = new BlockingUrlContentService();
        using var lifetime = new TestPluginDocumentLifetime();
        using var viewModel = new BatchHttpGetViewModel(service, new StubBatchHttpFileService(), lifetime)
        {
            RequestLines = "https://slow.test/request\nhttps://never.test/request",
        };

        var execution = viewModel.ExecuteRequestsCommand.ExecuteAsync(null);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var contentBeforeClose = viewModel.ResponseContent;

        viewModel.Dispose();
        await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(service.CancellationObserved);
        Assert.Equal(1, service.CallCount);
        Assert.Equal(contentBeforeClose, viewModel.ResponseContent);
    }

    [Fact]
    public async Task 文件模式只在内存汇总响应并于完成后一次写入且不更新响应框()
    {
        var requestService = new RecordingUrlContentService();
        var fileService = new StubBatchHttpFileService
        {
            InputPath = "requests.txt",
            OutputPath = "responses.txt",
            InputLines =
            [
                "one.test/path",
                "",
                "https://two.test/fail",
                "ftp://invalid.test/file",
                "http://three.test/ok",
            ],
        };
        using var lifetime = new TestPluginDocumentLifetime();
        using var viewModel = new BatchHttpGetViewModel(requestService, fileService, lifetime)
        {
            IsFileBatchMode = true,
        };

        await viewModel.SelectInputFileCommand.ExecuteAsync(null);
        await viewModel.ExecuteRequestsCommand.ExecuteAsync(null);

        Assert.Equal("requests.txt", viewModel.InputFilePath);
        Assert.Equal("responses.txt", viewModel.OutputFilePath);
        Assert.Equal(4, viewModel.TotalCount);
        Assert.Equal(4, viewModel.ProcessedCount);
        Assert.Equal(2, viewModel.SucceededCount);
        Assert.Equal(2, viewModel.FailedCount);
        Assert.Equal(4, viewModel.WrittenCount);
        Assert.Equal(1, fileService.WriteCount);
        Assert.Empty(viewModel.ResponseContent);
        Assert.Contains("第 1 行：one.test/path", fileService.WrittenContent);
        Assert.Contains("第 3 行：https://two.test/fail", fileService.WrittenContent);
        Assert.Contains("第 4 行：ftp://invalid.test/file", fileService.WrittenContent);
        Assert.Contains("第 5 行：http://three.test/ok", fileService.WrittenContent);
        Assert.Equal(
            "文件批处理完成：成功 2，失败 2，已写入 4 条结果",
            viewModel.StatusText);
    }

    [Fact]
    public async Task 文件模式关闭时取消进行中的请求且不写入半成品()
    {
        var requestService = new BlockingUrlContentService();
        var fileService = new StubBatchHttpFileService
        {
            InputPath = "requests.txt",
            OutputPath = "responses.txt",
            InputLines = ["https://slow.test/request", "https://never.test/request"],
        };
        using var lifetime = new TestPluginDocumentLifetime();
        using var viewModel = new BatchHttpGetViewModel(requestService, fileService, lifetime)
        {
            IsFileBatchMode = true,
        };

        await viewModel.SelectInputFileCommand.ExecuteAsync(null);
        var execution = viewModel.ExecuteRequestsCommand.ExecuteAsync(null);
        await requestService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.Dispose();
        await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(requestService.CancellationObserved);
        Assert.Equal(1, requestService.CallCount);
        Assert.Equal(0, fileService.WriteCount);
        Assert.Empty(viewModel.ResponseContent);
    }

    private sealed class BlockingUrlContentService : IUrlContentService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }
        public bool CancellationObserved { get; private set; }

        public async Task<string> GetStringAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return string.Empty;
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class RecordingUrlContentService : IUrlContentService
    {
        public List<string> RequestedUrls { get; } = [];

        public Task<string> GetStringAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            RequestedUrls.Add(url);
            if (url.Contains("/fail", StringComparison.Ordinal))
            {
                throw new UrlContentRequestException(
                    503,
                    "temporary failure",
                    new InvalidOperationException("request failed"));
            }

            return Task.FromResult($"response: {url}");
        }
    }

    private sealed class StubBatchHttpFileService : IBatchHttpFileService
    {
        public string? InputPath { get; init; }
        public string? OutputPath { get; init; }
        public string[] InputLines { get; init; } = [];
        public int WriteCount { get; private set; }
        public string WrittenContent { get; private set; } = string.Empty;

        public Task<string?> PickInputFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InputPath);

        public Task<string?> PickOutputFileAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OutputPath);

        public Task<string[]> ReadAllLinesAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InputLines);

        public Task WriteAllTextAtomicallyAsync(
            string path,
            string content,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            WrittenContent = content;
            return Task.CompletedTask;
        }
    }
}
