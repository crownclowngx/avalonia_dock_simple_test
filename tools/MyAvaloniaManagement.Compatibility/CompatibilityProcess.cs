using System.Diagnostics;
using System.Xml.Linq;

namespace MyAvaloniaManagement.CompatibilityTool;

/// <summary>只负责独立测试进程及 TRX 结果；超时结束子进程树，保留日志，不把退出码零等同于测试通过。</summary>
internal static class CompatibilityProcess
{
    internal static (int Passed, int Failed) ReadTrx(string file)
    {
        var doc = XDocument.Load(file);
        var counters = doc.Descendants().Single(e => e.Name.LocalName == "Counters");
        int Count(string name) => int.Parse(counters.Attribute(name)?.Value ?? "0");
        var passed = Count("passed");
        var failed = Count("failed");
        if (Count("total") != 1 || passed + failed != 1 || Count("notExecuted") != 0)
            throw new InvalidDataException("验收必须恰好执行一个用例，不允许零测试或跳过。");
        return (passed, failed);
    }

    internal static async Task<bool> RunAsync(string testAssembly, string filter, string controls,
        string pluginId, string output, int timeout, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(output);
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "test", testAssembly, "--filter", "FullyQualifiedName~" + filter,
                     "--results-directory", output, "--logger", "trx;LogFileName=result.trx" }) info.ArgumentList.Add(arg);
        info.Environment["MYAVALONIA_EXTERNAL_CONTROLS"] = controls;
        info.Environment["MYAVALONIA_WORKSPACE_PLUGIN_IDS"] = pluginId;
        info.Environment["MYAVALONIA_DATA_DIRECTORY"] = Path.Combine(output, "data");
        using var process = Process.Start(info) ?? throw new IOException("无法启动验收进程。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeout));
        try { await process.WaitForExitAsync(timeoutSource.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(output, "process.log"), await stdout + Environment.NewLine + await stderr, CancellationToken.None);
        }
        var result = ReadTrx(Path.Combine(output, "result.trx"));
        return process.ExitCode == 0 && result.Passed == 1 && result.Failed == 0;
    }
}
