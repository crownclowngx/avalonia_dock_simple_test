using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Services.Download.Extras;

/// <summary>
/// 封面图下载处理器。
/// 通过 HTTP GET 下载视频封面图片并保存到输出目录。
/// </summary>
public sealed class CoverExtrasHandler : IExtrasHandler, IDisposable
{
    private readonly HttpClient _httpClient;

    public CoverExtrasHandler(IBiliHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateCoverClient();
    }

    /// <summary>兼容独立构造；生产路径由 DI 提供客户端工厂。</summary>
    public CoverExtrasHandler()
        : this(new BiliHttpClientFactory())
    {
    }

    public string Type => "cover";
    public string DisplayName => "封面";

    public async Task<ExtrasResult> ExecuteAsync(ExtrasContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.CoverUrl))
            return ExtrasResult.Failed(Type, "封面 URL 为空，跳过封面下载");

        // 计算实际输出目录（含子文件夹）
        var actualOutputDir = string.IsNullOrEmpty(context.SubFolder)
            ? context.OutputDirectory
            : Path.Combine(context.OutputDirectory, context.SubFolder);
        Directory.CreateDirectory(actualOutputDir);

        var outputPath = Path.Combine(actualOutputDir, $"{context.BaseFileName}_cover.jpg");

        try
        {
            var url = NormalizeHttpsUrl(context.CoverUrl);
            var bytes = await _httpClient.GetByteArrayAsync(url, ct);
            await ExtrasOutputWriter.WriteBytesAsync(outputPath, bytes, context, ct);

            return ExtrasResult.Succeeded(Type, outputPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExtrasResult.Failed(Type, $"封面下载失败: {ex.Message}");
        }
    }

    private static Uri NormalizeHttpsUrl(string value)
    {
        var normalized = value.StartsWith("//", StringComparison.Ordinal)
            ? "https:" + value
            : value;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new UriFormatException("封面 URL 不是有效的 HTTP(S) 地址");
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return uri;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = -1,
        };
        return builder.Uri;
    }

    public void Dispose() => _httpClient.Dispose();
}
