using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Download.Extras;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.ReleaseAcceptance;

/// <summary>
/// P1-G7/G9 无凭据媒体验收门禁。它用固定版本 ffmpeg 生成完全可控的微型输入，随后只通过
/// 生产 <see cref="FfmpegService"/> 执行 stream copy 与软字幕封装，并由 ffprobe 验证成品事实。
/// </summary>
internal sealed class OfflineMediaOutputGate : IReleaseGate
{
    public string Name => "p1-g7-offline-media-output";

    public async Task<ReleaseGateResult> ExecuteAsync(
        ReleaseGateContext context,
        CancellationToken cancellationToken)
    {
        var ffmpeg = GetRequiredPath(context, "ffmpeg");
        var ffprobe = GetRequiredPath(context, "ffprobe");
        var version = await RunAsync(ffmpeg, ["-version"], cancellationToken);
        var probeVersion = await RunAsync(ffprobe, ["-version"], cancellationToken);
        if (!version.StandardOutput.Contains("ffmpeg version 8.1.2", StringComparison.OrdinalIgnoreCase)
            || !probeVersion.StandardOutput.Contains("ffprobe version 8.1.2", StringComparison.OrdinalIgnoreCase))
            return ReleaseGateResult.Fail(Name, "门禁只接受固定版本 ffmpeg/ffprobe 8.1.2。", new Dictionary<string, object?>
            {
                ["expectedVersion"] = "8.1.2",
            });

        var root = Path.Combine(context.SandboxRoot, "p1-g7-media");
        Directory.CreateDirectory(root);
        var avc = Path.Combine(root, "input-avc.mp4");
        var hevc = Path.Combine(root, "input-hevc.mp4");
        var av1 = Path.Combine(root, "input-av1.mkv");
        var audio = Path.Combine(root, "input-aac.m4a");
        await GenerateInputsAsync(ffmpeg, avc, hevc, av1, audio, cancellationToken);

        var paths = new AcceptanceDataPaths(root);
        var muxer = new FfmpegService(new FfmpegProcessFactory(), paths) { CustomPath = ffmpeg };
        var runtime = await muxer.DetectAsync(cancellationToken);
        if (!runtime.IsReady)
            return ReleaseGateResult.Fail(Name, "生产 ffmpeg 运行时未能验证固定版本可执行文件。");
        var capabilities = await muxer.GetCapabilitiesAsync(cancellationToken);
        if (!capabilities.SupportsMp4 || !capabilities.SupportsMkv)
            return ReleaseGateResult.Fail(Name, "固定运行时缺少 MP4 或 Matroska muxer。", new Dictionary<string, object?>
            {
                ["mp4"] = capabilities.SupportsMp4,
                ["mkv"] = capabilities.SupportsMkv,
            });

        var avMp4 = Path.Combine(root, "audio-video-avc.mp4");
        var avMkv = Path.Combine(root, "audio-video-hevc.mkv");
        var videoMkv = Path.Combine(root, "video-only-av1.mkv");
        await MuxAndPublishAsync(muxer,
            new MediaMuxRequest(avc, audio, Staging(avMp4), OutputContainer.Mp4, OutputMediaMode.AudioVideo),
            avMp4, cancellationToken);
        await MuxAndPublishAsync(muxer,
            new MediaMuxRequest(hevc, audio, Staging(avMkv), OutputContainer.Mkv, OutputMediaMode.AudioVideo),
            avMkv, cancellationToken);
        await MuxAndPublishAsync(muxer,
            new MediaMuxRequest(av1, null, Staging(videoMkv), OutputContainer.Mkv, OutputMediaMode.VideoOnly),
            videoMkv, cancellationToken);

        var native = Path.Combine(root, "audio-only-aac.m4a");
        await new NativeAudioPublisher().PublishAsync(
            audio, Staging(native), native, overwrite: false, cancellationToken);

        // 字幕文本由生产格式化策略生成，门禁不维护另一套容易漂移的手写样本。
        // 两种容器都使用两条轨，可同时证明轨数、顺序、语言、标题和 codec 元数据。
        var cues = new[] { new SubtitleCue(TimeSpan.Zero, TimeSpan.FromMilliseconds(900), "你好\nP1-G9", 0) };
        var srt = Path.Combine(root, "subtitle.zh.srt");
        var ass = Path.Combine(root, "subtitle.en.ass");
        await File.WriteAllTextAsync(srt, new SrtSubtitleFormatter().Format(cues),
            new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(ass, new AssSubtitleFormatter().Format(cues),
            new UTF8Encoding(false), cancellationToken);
        var subtitleTracks = new[]
        {
            new SubtitleMuxTrack(srt, "zh", "中文", SubtitleOutputFormat.Srt),
            new SubtitleMuxTrack(ass, "en", "English", SubtitleOutputFormat.Ass),
        };
        var softMp4 = Path.Combine(root, "soft-subtitle.mp4");
        var softMkv = Path.Combine(root, "soft-subtitle.mkv");
        await muxer.MuxSubtitlesAsync(avMp4, subtitleTracks, Staging(softMp4),
            OutputContainer.Mp4, cancellationToken);
        File.Move(Staging(softMp4), softMp4, overwrite: false);
        await muxer.MuxSubtitlesAsync(avMkv, subtitleTracks, Staging(softMkv),
            OutputContainer.Mkv, cancellationToken);
        File.Move(Staging(softMkv), softMkv, overwrite: false);
        var subtitleVerifier = new FfprobeSubtitleTrackVerifier(muxer, new FfmpegProcessFactory());
        await subtitleVerifier.VerifyAsync(softMp4, subtitleTracks, OutputContainer.Mp4, cancellationToken);
        await subtitleVerifier.VerifyAsync(softMkv, subtitleTracks, OutputContainer.Mkv, cancellationToken);

        var checks = new[]
        {
            await VerifyAsync(ffprobe, avMp4, "h264", "aac", "mov,mp4", cancellationToken),
            await VerifyAsync(ffprobe, avMkv, "hevc", "aac", "matroska", cancellationToken),
            await VerifyAsync(ffprobe, videoMkv, "av1", null, "matroska", cancellationToken),
            await VerifyAsync(ffprobe, native, null, "aac", "mov,mp4", cancellationToken),
        };
        if (checks.Any(check => !check.Passed))
            return ReleaseGateResult.Fail(Name, "ffprobe 检测到编码、容器或流数量与输出计划不一致。",
                new Dictionary<string, object?> { ["checks"] = checks.Select(check => check.Label).ToArray() });

        return ReleaseGateResult.Pass(Name, "媒体输出及 MP4 mov_text、MKV SubRip/ASS 软字幕均通过 ffprobe 验证。",
            new Dictionary<string, object?>
            {
                ["ffmpegVersion"] = "8.1.2",
                ["outputs"] = checks.Length + 2,
                ["subtitleTracks"] = subtitleTracks.Length * 2,
                ["transcodingArguments"] = 0,
            });
    }

    private static async Task GenerateInputsAsync(
        string ffmpeg,
        string avc,
        string hevc,
        string av1,
        string audio,
        CancellationToken cancellationToken)
    {
        await RequireSuccessAsync(ffmpeg,
            ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=black:s=64x64:r=2", "-t", "1", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", avc], cancellationToken);
        await RequireSuccessAsync(ffmpeg,
            ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=black:s=64x64:r=2", "-t", "1", "-an", "-c:v", "libx265", "-x265-params", "log-level=error", "-pix_fmt", "yuv420p", hevc], cancellationToken);
        await RequireSuccessAsync(ffmpeg,
            ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=black:s=64x64:r=2", "-t", "1", "-an", "-c:v", "libaom-av1", "-cpu-used", "8", "-pix_fmt", "yuv420p", av1], cancellationToken);
        await RequireSuccessAsync(ffmpeg,
            ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=1000:sample_rate=48000", "-t", "1", "-vn", "-c:a", "aac", "-b:a", "96k", audio], cancellationToken);
    }

    private static async Task MuxAndPublishAsync(
        IMediaMuxer muxer,
        MediaMuxRequest request,
        string output,
        CancellationToken cancellationToken)
    {
        await muxer.MuxAsync(request, cancellationToken);
        File.Move(request.OutputPath, output, overwrite: false);
    }

    private static async Task<ProbeCheck> VerifyAsync(
        string ffprobe,
        string path,
        string? videoCodec,
        string? audioCodec,
        string formatFragment,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(ffprobe,
            ["-v", "error", "-show_entries", "stream=codec_type,codec_name:format=format_name", "-of", "json", path], cancellationToken);
        if (result.ExitCode != 0) return new ProbeCheck(Path.GetFileName(path), false);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var expectedCount = (videoCodec is null ? 0 : 1) + (audioCodec is null ? 0 : 1);
        var format = document.RootElement.GetProperty("format").GetProperty("format_name").GetString() ?? "";
        var videoOk = videoCodec is null || streams.Any(stream =>
            stream.GetProperty("codec_type").GetString() == "video"
            && stream.GetProperty("codec_name").GetString() == videoCodec);
        var audioOk = audioCodec is null || streams.Any(stream =>
            stream.GetProperty("codec_type").GetString() == "audio"
            && stream.GetProperty("codec_name").GetString() == audioCodec);
        return new ProbeCheck(Path.GetFileName(path),
            streams.Length == expectedCount && videoOk && audioOk
            && format.Contains(formatFragment, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task RequireSuccessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(executable, arguments, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("固定媒体输入生成失败。");
    }

    private static async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动媒体验收进程。");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static string GetRequiredPath(ReleaseGateContext context, string key)
    {
        if (!context.Items.TryGetValue(key, out var value) || value is not string path || !File.Exists(path))
            throw new FileNotFoundException($"缺少 {key} 可执行文件。");
        return Path.GetFullPath(path);
    }

    private static string Staging(string output)
        => Path.ChangeExtension(output, $".staging-g7{Path.GetExtension(output)}");

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
    private sealed record ProbeCheck(string Label, bool Passed);
}
