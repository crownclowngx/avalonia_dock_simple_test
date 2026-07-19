using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Services.Download.Extras;

/// <summary>
/// 弹幕下载处理器。
/// 按 360 秒分段获取 Protobuf 弹幕数据，解码后合并生成标准 B站弹幕 XML 文件。
/// </summary>
public class DanmakuExtrasHandler : IExtrasHandler
{
    private static readonly IPluginLogger Log = PluginLog.For<DanmakuExtrasHandler>();
    private static readonly Random Rng = new();

    /// <summary>每段弹幕的时长（秒）</summary>
    private const int SegmentDurationSec = 360;

    public string Type => "danmaku";
    public string DisplayName => "弹幕";

    public async Task<ExtrasResult> ExecuteAsync(ExtrasContext context, CancellationToken ct)
    {
        if (context.Cid == 0)
            return ExtrasResult.Failed(Type, "视频 cid 为空，无法获取弹幕");

        // 计算实际输出目录
        var actualOutputDir = string.IsNullOrEmpty(context.SubFolder)
            ? context.OutputDirectory
            : Path.Combine(context.OutputDirectory, context.SubFolder);
        Directory.CreateDirectory(actualOutputDir);

        try
        {
            // 计算分段数：ceil(duration / 360)
            var segments = Math.Max(1, (int)Math.Ceiling((double)context.Duration / SegmentDurationSec));
            var allElems = new List<DanmakuElem>();

            for (int i = 1; i <= segments; i++)
            {
                ct.ThrowIfCancellationRequested();

                context.ProgressReporter?.Report($"正在获取弹幕 ({i}/{segments})...");

                try
                {
                    var protobuf = await context.ApiService.GetDanmakuSegmentAsync(
                        context.Cid, i, context.Aid, context.Cookie);

                    var elems = ProtobufDanmakuDecoder.Decode(protobuf);
                    allElems.AddRange(elems);

                    Log.Info($"弹幕段 {i}/{segments}: {elems.Count} 条");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Warn($"弹幕段 {i}/{segments} 获取失败: {ex.Message}");
                    // 单段失败不中断，继续获取后续段
                }

                // 段间随机延迟 100-500ms（防风控），最后一段不需要延迟
                if (i < segments)
                {
                    var delayMs = Rng.Next(100, 500);
                    await Task.Delay(delayMs, ct);
                }
            }

            if (allElems.Count == 0)
                return ExtrasResult.Failed(Type, "未获取到任何弹幕数据");

            // 生成 XML
            var xmlContent = ProtobufDanmakuDecoder.ToXml(allElems);
            var outputPath = Path.Combine(actualOutputDir, $"{context.BaseFileName}.xml");
            await File.WriteAllTextAsync(outputPath, xmlContent, ct);

            context.ProgressReporter?.Report($"弹幕下载完成: {allElems.Count} 条");

            return ExtrasResult.Succeeded(Type, outputPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ExtrasResult.Failed(Type, $"弹幕下载失败: {ex.Message}");
        }
    }
}
