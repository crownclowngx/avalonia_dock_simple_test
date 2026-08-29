using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyAvaloniaManagement.Gate;

internal static class GateChecks
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".axaml", ".props", ".targets", ".json", ".md",
    };

    public static void AssertArchitectureRule(ArchitectureRuleConfiguration rule, string repositoryRoot)
    {
        var regex = new Regex(rule.Pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline);
        foreach (var configuredPath in rule.Paths)
        {
            var path = Path.Combine(repositoryRoot, configuredPath);
            var files = File.Exists(path)
                ? [path]
                : Directory.Exists(path)
                    ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                        .Where(IsSourceFile)
                    : throw new GateFailureException($"架构规则 {rule.Id} 的路径不存在：{path}。");
            foreach (var file in files)
            {
                if (regex.IsMatch(File.ReadAllText(file)))
                {
                    throw new GateFailureException($"架构规则 {rule.Id} 在 {file} 中发现禁止内容。");
                }
            }
        }
    }

    public static void AssertCurrentDocumentation(string repositoryRoot)
    {
        var currentDocuments = new[]
        {
            Path.Combine(repositoryRoot, "README.md"),
            Path.Combine(repositoryRoot, "docs", "README.md"),
            Path.Combine(repositoryRoot, "Host", "MyAvaloniaManagement", "docs", "README.md"),
        }.Where(File.Exists);
        var markdownLink = new Regex(@"\[[^\]]+\]\((?<target>[^)]+)\)", RegexOptions.CultureInvariant);
        foreach (var document in currentDocuments)
        {
            foreach (Match match in markdownLink.Matches(File.ReadAllText(document)))
            {
                var target = match.Groups["target"].Value.Split('#')[0];
                if (string.IsNullOrWhiteSpace(target) || Uri.TryCreate(target, UriKind.Absolute, out _))
                {
                    continue;
                }

                var decoded = Uri.UnescapeDataString(target).Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(document)!, decoded));
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                {
                    throw new GateFailureException($"文档链接不存在：{document} -> {target}。");
                }
            }
        }
    }

    public static async Task RunWindowsSmokeAsync(
        ProcessRunner processes,
        string repositoryRoot,
        string project,
        string stageRoot,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        var publishRoot = Path.Combine(stageRoot, "publish");
        var dataRoot = Path.Combine(stageRoot, "data");
        Directory.CreateDirectory(dataRoot);
        await processes.RunCheckedAsync(
            "dotnet",
            ["publish", Path.Combine(repositoryRoot, project), "-c", "Release", "-o", publishRoot,
                "--no-restore", "--nologo", "-p:SkipPluginDeploy=true"],
            repositoryRoot, environment, Path.Combine(stageRoot, "publish.log"), cancellationToken);
        var executable = Path.Combine(publishRoot, "MyAvaloniaManagement.exe");
        if (!File.Exists(executable))
        {
            throw new GateFailureException($"Windows Smoke 可执行文件不存在：{executable}。");
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = publishRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var entry in environment.Where(entry => entry.Value is not null))
        {
            startInfo.Environment[entry.Key] = entry.Value!;
        }
        startInfo.Environment["MYAVALONIA_DATA_DIRECTORY"] = dataRoot;
        startInfo.Environment["MYAVALONIA_SMOKE_TEST"] = "1";
        using var process = Process.Start(startInfo) ??
            throw new GateFailureException("无法启动 Windows Smoke。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new GateFailureException("Host 未在 15 秒内完成真实窗口 Smoke。");
        }

        if (process.ExitCode != 0)
        {
            throw new GateFailureException($"Windows Smoke 退出码为 {process.ExitCode}。");
        }

        var layoutPath = Path.Combine(dataRoot, "layout-v2.json");
        using var layout = JsonDocument.Parse(File.ReadAllText(layoutPath));
        if (layout.RootElement.GetProperty("schemaVersion").GetInt32() != 2 ||
            File.Exists(Path.Combine(dataRoot, "layout-v1.json")))
        {
            throw new GateFailureException("Windows Smoke 没有生成唯一的 layout-v2.json。");
        }
    }

    private static bool IsSourceFile(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        TextExtensions.Contains(Path.GetExtension(path));
}
