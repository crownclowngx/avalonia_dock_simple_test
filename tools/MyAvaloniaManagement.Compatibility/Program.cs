using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MyAvaloniaManagement.Business.Compatibility;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Compatibility;

namespace MyAvaloniaManagement.CompatibilityTool;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help"))
        {
            Console.WriteLine("用法：--input <Controls目录、插件目录或ZIP> --host <Host输出目录> --output <证据目录> [--ui-tests <启用外部验收的UiTests.dll>] [--workspace <明确选择的插件ID,ID>] [--timeout <秒>]");
            return 0;
        }
        try { return await RunAsync(CompatibilityOptions.Parse(args)); }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException or FormatException or OperationCanceledException)
        { Console.Error.WriteLine($"验收输入或产物无效：{ex.Message}"); return 2; }
    }

    private static async Task<int> RunAsync(CompatibilityOptions options)
    {
        var inputPrefix = options.Input.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (options.Output.Equals(options.Input, StringComparison.OrdinalIgnoreCase) || options.Output.StartsWith(inputPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("证据目录必须位于输入产物之外，避免自引用。");
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        var host = await HostCompatibilityCapture.CaptureAsync(cancel.Token);
        await AssertHostAsync(host, options.Host, cancel.Token);
        if (options.UiTests is not null)
        {
            if (!File.Exists(options.UiTests)) throw new FileNotFoundException("缺少 UI 验收程序集。");
            await AssertHostAsync(host, Path.GetDirectoryName(options.UiTests)!, cancel.Token);
        }
        // 每次运行拥有独立子目录；不清理历史报告或覆盖已有验收证据。
        var run = Path.Combine(options.Output, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(run);
        // 在解压或读取输入前留下失败默认值；进程被中断或输入无效也不会留下貌似成功的目录。
        await File.WriteAllTextAsync(Path.Combine(run, "summary.json"), "{\"passed\":false,\"status\":\"输入验证或执行尚未完成；检查进程错误与日志。\"}", CancellationToken.None);
        Console.WriteLine("本次证据目录：" + run);
        var input = options.Input;
        if (File.Exists(input))
        {
            var extracted = Path.Combine(run, "input");
            using var archive = ZipFile.OpenRead(input);
            if (archive.Entries.Count > 50000 || archive.Entries.Sum(e => e.Length) > 4L * 1024 * 1024 * 1024)
                throw new InvalidDataException("ZIP 超过验收输入限制。");
            archive.ExtractToDirectory(extracted);
            input = extracted;
        }
        if (!Directory.Exists(input)) throw new DirectoryNotFoundException("缺少输入目录。");
        if (Directory.Exists(Path.Combine(input, "Controls"))) input = Path.Combine(input, "Controls");
        var plugins = File.Exists(Path.Combine(input, PluginManifestReader.FileName)) ? [input] :
            Directory.GetDirectories(input).Order().ToArray();
        if (plugins.Length == 0) throw new InvalidDataException("没有插件输入，不能产生通过报告。");
        var summaries = new List<object>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var failed = false;
        foreach (var pluginRoot in plugins)
        {
            try
            {
                var plugin = await PluginArtifactReader.ReadAsync(pluginRoot, cancel.Token);
                if (!seen.Add(plugin.PluginId)) throw new InvalidDataException("输入含重复插件身份。");
                var output = Path.Combine(run, plugin.PluginId);
                Directory.CreateDirectory(output);
                var controls = Path.Combine(output, "Controls");
                var copy = Path.Combine(controls, "Plugin");
                foreach (var file in plugin.Identity.Files)
                {
                    var target = Path.Combine(copy, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(Path.Combine(pluginRoot, file.Path), target);
                }
                var checks = new List<CompatibilityCheck>();
                var illegal = plugin.Identity.Files.Where(f => RuntimeProfile.Current.IsForbiddenAsset(f.Path)).Select(f => f.Path).ToArray();
                var references = plugin.Identity.Files.Where(f => f.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(f => ReadManagedReferences(Path.Combine(copy, f.Path))).ToArray();
                var forbiddenReferences = references.Where(r => RuntimeProfile.Current.IsForbiddenReference(r.Id)).ToArray();
                PluginManifestReader.TryRead(copy, out var manifest, out _, out _);
                var compatible = manifest!.Sdk.Contains(PluginSdkCompatibilityProfile.Current.SdkVersion);
                var staticPassed = illegal.Length == 0 && forbiddenReferences.Length == 0 && compatible &&
                    (await ArtifactFingerprint.CaptureDirectoryAsync(copy, cancel.Token)).Sha256 == plugin.Identity.Sha256;
                checks.Add(new(CompatibilityLevel.Static, staticPassed ? CompatibilityOutcome.Passed : CompatibilityOutcome.Failed,
                    "package-policy", staticPassed ? "清单区间、产物副本、禁带资产和托管引用检查通过。" : "存在 SDK 区间、禁带资产、内部引用或输入变化问题。"));
                foreach (var (level, filter, enabled) in new[]
                {
                    (CompatibilityLevel.Composition, "ExternalPluginBinaryAcceptanceTests", options.UiTests is not null),
                    (CompatibilityLevel.Workspace, "ExternalPluginWorkspaceAcceptanceTests", options.WorkspaceIds.Contains(plugin.PluginId))
                })
                {
                    if (!enabled || !staticPassed || (level == CompatibilityLevel.Workspace &&
                        !checks.Any(check => check.Level == CompatibilityLevel.Composition && check.Outcome == CompatibilityOutcome.Passed)))
                    { checks.Add(new(level, CompatibilityOutcome.NotRun, filter, "未选择或前置检查失败。")); continue; }
                    try
                    {
                        var passed = await CompatibilityProcess.RunAsync(options.UiTests!, filter, controls, plugin.PluginId,
                            Path.Combine(output, level.ToString()), options.TimeoutSeconds, cancel.Token);
                        checks.Add(new(level, passed ? CompatibilityOutcome.Passed : CompatibilityOutcome.Failed, filter,
                            passed ? "独立进程实际执行一个验收用例通过。" : "验收失败，详见本次 TRX 与进程日志。"));
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or InvalidOperationException or System.Xml.XmlException)
                    { checks.Add(new(level, CompatibilityOutcome.Interrupted, filter, "验收中断或证据不完整，不能记为通过。")); }
                }
                checks.Add(new(CompatibilityLevel.Business, CompatibilityOutcome.NotRun, "business", "此入口不执行业务或真机验收。"));
                var unchanged = (await ArtifactFingerprint.CaptureDirectoryAsync(pluginRoot, CancellationToken.None)).Sha256 == plugin.Identity.Sha256 &&
                    (await ArtifactFingerprint.CaptureDirectoryAsync(copy, CancellationToken.None)).Sha256 == plugin.Identity.Sha256;
                if (!unchanged)
                    checks.Add(new(CompatibilityLevel.Static, CompatibilityOutcome.Failed, "input-stability", "验收期间输入变化，本报告不能代表稳定产物。"));
                var report = new PluginCompatibilityReport(1, Guid.NewGuid(), DateTimeOffset.UtcNow,
                    plugin.PluginId, plugin.Version, plugin.Identity.Sha256, host, checks);
                var reportPath = Path.Combine(output, "compatibility-report.json");
                await File.WriteAllTextAsync(reportPath, CompatibilityReportJson.Serialize(report), CancellationToken.None);
                var success = checks.All(c => c.Outcome is CompatibilityOutcome.Passed or CompatibilityOutcome.NotRun);
                failed |= !success;
                summaries.Add(new { plugin.PluginId, success, report = Path.GetRelativePath(run, reportPath), summary = CompatibilityMatcher.Summary(report) });
                Console.WriteLine($"{plugin.PluginId}: {CompatibilityMatcher.Summary(report)}");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or OperationCanceledException or BadImageFormatException)
            {
                failed = true;
                summaries.Add(new { input = Path.GetFileName(pluginRoot), success = false, error = ex.GetType().Name });
            }
            if (cancel.IsCancellationRequested) break;
        }
        if (options.WorkspaceIds.Any(id => !seen.Contains(id)))
        { failed = true; summaries.Add(new { success = false, error = "指定 Workspace 插件不在输入中。" }); }
        await File.WriteAllTextAsync(Path.Combine(run, "summary.json"), JsonSerializer.Serialize(new { passed = !failed, host, inputs = summaries }, CompatibilityReportJson.Options));
        Console.WriteLine("证据目录：" + run);
        return failed || cancel.IsCancellationRequested ? 1 : 0;
    }

    private static IReadOnlyList<BuildDependency> ReadManagedReferences(string path)
    {
        try { return PluginArtifactReader.ReadReferences(path); }
        catch (BadImageFormatException) { return []; } // 原生 DLL 仍参与指纹，但没有托管引用表。
    }

    private static async Task AssertHostAsync(HostCompatibilityIdentity host, string directory, CancellationToken token)
    {
        var files = await ArtifactFingerprint.CaptureFilesAsync(host.Files.Select(f => (f.Path, Path.Combine(directory, f.Path))), token);
        if (files.Sha256 != host.RuntimeHash) throw new InvalidDataException("指定 Host / 测试进程的运行时与验收工具不同，请使用同次构建产物。");
    }
}
