using System.Diagnostics;
using System.Text;

namespace MyAvaloniaManagement.Gate;

internal sealed record ProcessResult(int ExitCode, string Output, TimeSpan Duration);

internal sealed class ProcessRunner(TextWriter output)
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        string? logPath,
        CancellationToken cancellationToken,
        bool quiet = false)
    {
        var argumentList = arguments.ToArray();
        if (!quiet)
        {
            output.WriteLine($"> {fileName} {string.Join(' ', argumentList.Select(QuoteForDisplay))}");
        }
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var entry in environment)
            {
                if (entry.Value is null)
                {
                    startInfo.Environment.Remove(entry.Key);
                }
                else
                {
                    startInfo.Environment[entry.Key] = entry.Value;
                }
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var combined = new StringBuilder();
        var sync = new object();
        process.OutputDataReceived += (_, eventArgs) => Append(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => Append(eventArgs.Data);
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new GateFailureException($"无法启动命令：{fileName}。");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        stopwatch.Stop();
        var text = combined.ToString();
        if (logPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.WriteAllTextAsync(logPath, text, new UTF8Encoding(false), cancellationToken);
        }

        return new(process.ExitCode, text, stopwatch.Elapsed);

        void Append(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (sync)
            {
                combined.AppendLine(line);
            }
            if (!quiet)
            {
                output.WriteLine(line);
            }
        }
    }

    public async Task<ProcessResult> RunCheckedAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        string? logPath,
        CancellationToken cancellationToken,
        bool quiet = false)
    {
        var result = await RunAsync(
            fileName, arguments, workingDirectory, environment, logPath, cancellationToken, quiet);
        if (result.ExitCode != 0)
        {
            throw new GateFailureException($"命令失败（exit {result.ExitCode}）：{fileName}。", result.Output);
        }

        return result;
    }

    private static string QuoteForDisplay(string argument) =>
        argument.Any(char.IsWhiteSpace) ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : argument;
}

internal sealed class GateFailureException(string message, string? detail = null) : Exception(message)
{
    public string? Detail { get; } = detail;
}
