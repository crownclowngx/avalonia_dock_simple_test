using MyPlugTest.Services;
using MyPlugTest.ViewModels;

namespace MyAvaloniaManagement.PluginTests;

public sealed class BatchHttpGetDocumentTests
{
    [Fact]
    public async Task 请求严格按非空行顺序执行且单行失败不阻断后续行()
    {
        var service = new RecordingUrlContentService();
        var viewModel = new BatchHttpGetViewModel(service)
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
        var viewModel = new BatchHttpGetViewModel(service)
        {
            RequestLines = "ftp://example.com/file",
        };

        await viewModel.ExecuteRequestsCommand.ExecuteAsync(null);

        Assert.Empty(service.RequestedUrls);
        Assert.Contains("网址必须使用 http 或 https", viewModel.ResponseContent);
        Assert.Equal("执行完成：成功 0，失败 1，共 1 个请求", viewModel.StatusText);
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
}
