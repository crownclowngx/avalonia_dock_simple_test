using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Services.Download.Extras;

/// <summary>
/// 字幕下载处理器。
/// 调用 B站 player API 获取字幕列表，下载 JSON 并转换为 SRT 格式保存。
/// </summary>
public class SubtitleExtrasHandler : IExtrasHandler
{
    private static readonly IPluginLogger Log = PluginLog.For<SubtitleExtrasHandler>();

    public string Type => "subtitle";
    public string DisplayName => "字幕";

    public async Task<ExtrasResult> ExecuteAsync(ExtrasContext context, CancellationToken ct)
    {
        if (context.Cid == 0)
            return ExtrasResult.Failed(Type, "视频 cid 为空，无法获取字幕");

        // 计算实际输出目录
        var actualOutputDir = string.IsNullOrEmpty(context.SubFolder)
            ? context.OutputDirectory
            : Path.Combine(context.OutputDirectory, context.SubFolder);
        Directory.CreateDirectory(actualOutputDir);

        try
        {
            context.ProgressReporter?.Report("正在获取字幕列表...");
            var subtitles = await context.ApiService.GetSubtitleListAsync(
                context.Aid, context.Cid, context.Cookie);

            if (subtitles.Count == 0)
                return ExtrasResult.Failed(Type, "该视频没有可用字幕");

            var outputFiles = new List<string>();

            foreach (var sub in subtitles)
            {
                ct.ThrowIfCancellationRequested();

                context.ProgressReporter?.Report($"正在下载字幕: {sub.LanDoc}...");

                try
                {
                    var srtContent = await context.ApiService.GetSubtitleSrtAsync(
                        sub.SubtitleUrl, context.Cookie);

                    if (string.IsNullOrWhiteSpace(srtContent))
                    {
                        Log.Warn($"字幕 {sub.LanDoc} 内容为空，跳过");
                        continue;
                    }

                    // 文件名格式：{BaseFileName}.{lan}.srt
                    var outputPath = Path.Combine(actualOutputDir,
                        $"{context.BaseFileName}.{sub.Lan}.srt");
                    await File.WriteAllTextAsync(outputPath, srtContent, ct);
                    outputFiles.Add(outputPath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Warn($"字幕 {sub.LanDoc} 下载失败: {ex.Message}");
                    // 单个字幕失败不影响其他字幕
                }
            }

            if (outputFiles.Count == 0)
                return ExtrasResult.Failed(Type, "所有字幕下载均失败");

            return ExtrasResult.Succeeded(Type, outputFiles.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ExtrasResult.Failed(Type, $"字幕下载失败: {ex.Message}");
        }
    }
}
