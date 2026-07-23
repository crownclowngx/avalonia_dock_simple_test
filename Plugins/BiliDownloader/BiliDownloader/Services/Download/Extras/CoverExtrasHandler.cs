namespace BiliDownloader.Services.Download.Extras;

/// <summary>
/// 封面图下载处理器。
/// 通过 HTTP GET 下载视频封面图片并保存到输出目录。
/// </summary>
public class CoverExtrasHandler : IExtrasHandler
{
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
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
            httpClient.DefaultRequestHeaders.Add("Referer", "https://www.bilibili.com/");

            // 确保使用 HTTPS
            var url = context.CoverUrl.Replace("http:", "https:");
            if (url.StartsWith("//"))
                url = "https:" + url;

            var bytes = await httpClient.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(outputPath, bytes, ct);

            return ExtrasResult.Succeeded(Type, outputPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExtrasResult.Failed(Type, $"封面下载失败: {ex.Message}");
        }
    }
}
