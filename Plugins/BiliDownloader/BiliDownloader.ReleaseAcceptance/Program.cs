using System.Runtime.InteropServices;

namespace BiliDownloader.ReleaseAcceptance;

internal static class Program
{
    private const string BvidVariable = "BILIDOWNLOADER_G8_TEST_BVID";
    private const string CookieVariable = "BILIDOWNLOADER_G8_COOKIE";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("发布验收已取消。");
            return 2;
        }
        catch (Exception ex)
        {
            // 顶层同样不输出 Message，避免外部 HTTP 异常把签名 URL 写入发布日志。
            Console.Error.WriteLine($"发布验收失败：{ex.GetType().Name}");
            return 2;
        }
    }

    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            WriteUsage();
            return 2;
        }

        var command = args[0].ToLowerInvariant();
        var reportPath = RequiredOption(args, "--report");
        var sandbox = RequiredOption(args, "--sandbox");
        var cookie = Environment.GetEnvironmentVariable(CookieVariable);
        ReleaseGateContext context;
        IReadOnlyList<IReleaseGate> gates;

        switch (command)
        {
            case "live":
                if (!OperatingSystem.IsWindows()
                    || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                    throw new PlatformNotSupportedException("正式 G8 联网门禁只支持 Windows x64。");
                context = new ReleaseGateContext(
                    sandbox,
                    Environment.GetEnvironmentVariable(BvidVariable),
                    cookie);
                gates =
                [
                    new LiveFfmpegInstallationGate(),
                    new LiveBilibiliDownloadGate(),
                    new LiveRangeRecoveryGate(),
                    new LivePersistenceEvidenceGate(),
                    new SensitiveEvidenceGate([sandbox]),
                ];
                break;
            case "scan":
                var root = RequiredOption(args, "--root");
                context = new ReleaseGateContext(sandbox, null, cookie);
                gates = [new SensitiveEvidenceGate([root])];
                break;
            case "verify-package":
                var package = RequiredOption(args, "--package");
                context = new ReleaseGateContext(sandbox, null, cookie);
                gates = [new PackageVerificationGate(package)];
                break;
            case "media-output":
                context = new ReleaseGateContext(sandbox, null, null);
                context.Items["ffmpeg"] = RequiredOption(args, "--ffmpeg");
                context.Items["ffprobe"] = RequiredOption(args, "--ffprobe");
                gates = [new OfflineMediaOutputGate()];
                break;
            case "bandwidth":
                context = new ReleaseGateContext(sandbox, null, null);
                gates = [new BandwidthLimitGate()];
                break;
            default:
                WriteUsage();
                return 2;
        }

        Directory.CreateDirectory(context.SandboxRoot);
        var report = await new ReleaseGatePipeline(gates).ExecuteAsync(context, cancellationToken);
        await ReleaseGatePipeline.WriteReportAsync(reportPath, report, cancellationToken);
        Console.WriteLine(report.Passed ? "发布验收门禁通过。" : "发布验收门禁未通过。");
        return report.Passed ? 0 : 1;
    }

    private static string RequiredOption(string[] args, string name)
    {
        var index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index == args.Length - 1 || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"缺少参数 {name}。");
        return args[index + 1];
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine("用法：");
        Console.Error.WriteLine("  live --sandbox <目录> --report <JSON>");
        Console.Error.WriteLine("  scan --root <目录> --sandbox <目录> --report <JSON>");
        Console.Error.WriteLine("  verify-package --package <ZIP> --sandbox <目录> --report <JSON>");
        Console.Error.WriteLine("  media-output --ffmpeg <ffmpeg.exe> --ffprobe <ffprobe.exe> --sandbox <目录> --report <JSON>");
        Console.Error.WriteLine("  bandwidth --sandbox <目录> --report <JSON>");
    }
}
