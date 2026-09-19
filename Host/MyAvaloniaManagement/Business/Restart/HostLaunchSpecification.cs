using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MyAvaloniaManagement.Business.Storage;

namespace MyAvaloniaManagement.Business.Restart;

/// <summary>冻结本次 Host 的启动身份；只描述启动，不拥有进程、窗口或插件。</summary>
/// <remarks>参数逐项复制并通过 ArgumentList 传递，中文路径和 shell 元字符都只作为数据。</remarks>
internal sealed class HostLaunchSpecification
{
    private readonly string[] _prefix;
    private readonly string[] _arguments;

    internal HostLaunchSpecification(string executable, string? entryDll, string[] arguments,
        string workingDirectory, string dataRoot)
    {
        Executable = Path.GetFullPath(executable);
        WorkingDirectory = Path.GetFullPath(workingDirectory);
        DataRoot = Path.GetFullPath(dataRoot);
        _prefix = entryDll is null ? [] : [Path.GetFullPath(entryDll)];
        _arguments = arguments.ToArray();
        if (!File.Exists(Executable) || !Directory.Exists(WorkingDirectory) ||
            _prefix.Any(path => !File.Exists(path)) ||
            _arguments.Any(value => value.StartsWith(RestartHelperRunner.Switch, StringComparison.Ordinal)))
            throw new InvalidOperationException("当前启动信息不支持自动重启。");
    }

    internal string Executable { get; }
    internal string WorkingDirectory { get; }
    internal string DataRoot { get; }

    /// <summary>仅 dotnet 托管启动需要入口 DLL；单文件直接使用进程路径，不查询 Assembly.Location。</summary>
    internal static HostLaunchSpecification Capture(string[] arguments)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("无法取得 Host 启动路径。");
        var dll = string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase)
            ? Environment.GetCommandLineArgs()[0] : null;
        if (dll is not null && !string.Equals(Path.GetExtension(dll), ".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("无法识别 dotnet 入口。");
        return new(processPath, dll, arguments, Environment.CurrentDirectory, HostDataRootPolicy.ResolveDefault());
    }

    /// <summary>助手和普通启动复用同一可信目标；一次性参数只放在助手命令，绝不流入新 Host。</summary>
    internal ProcessStartInfo CreateStartInfo(string[]? helperArguments = null)
    {
        var start = new ProcessStartInfo(Executable)
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = WorkingDirectory };
        foreach (var value in _prefix) start.ArgumentList.Add(value);
        if (helperArguments is not null)
            foreach (var value in helperArguments) start.ArgumentList.Add(value);
        foreach (var value in _arguments) start.ArgumentList.Add(value);
        start.Environment[HostDataRootPolicy.EnvironmentVariableName] = DataRoot;
        // Smoke/测试探针属于发起进程，继承会令新 Host 自行关闭或重复执行测试。
        foreach (var key in start.Environment.Keys.ToArray())
            if (key.StartsWith("MYAVALONIA_", StringComparison.Ordinal) &&
                (key.Contains("TEST", StringComparison.Ordinal) || key.Contains("PROBE", StringComparison.Ordinal)))
                start.Environment.Remove(key);
        return start;
    }
}
