using System;
using System.Diagnostics;
using System.IO;

namespace MyAvaloniaManagement.Business.Diagnostics;

/// <summary>
/// 在开发者显式承担风险时，把原始异常写入进程级临时输出。
/// </summary>
/// <remarks>
/// 该输出与 <see cref="HostDiagnosticRecord"/> 完全分离，不能写入宿主 JSONL、UI 或剪贴板。
/// 环境变量只接受精确值 <c>1</c>，避免部署环境中的模糊布尔值意外开启敏感输出。
/// </remarks>
internal static class HostSensitiveDiagnosticDebugOutput
{
    internal const string EnvironmentVariableName =
        "MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS";

    internal static bool IsEnabled => string.Equals(
        Environment.GetEnvironmentVariable(EnvironmentVariableName),
        "1",
        StringComparison.Ordinal);

    internal static void Write(
        string code,
        HostDiagnosticPhase phase,
        Exception? exception)
    {
        if (!IsEnabled || exception is null)
        {
            return;
        }

        var warning =
            "[敏感诊断已开启] 以下异常原文可能包含密码、Token、正文和本地路径；" +
            "宿主不会把它写入诊断 JSONL，请仅在本地短期使用。";
        var detail = $"{warning}{Environment.NewLine}" +
                     $"errorCode={code} phase={phase}{Environment.NewLine}{exception}";
        try
        {
            Trace.TraceWarning(detail);
            Console.Error.WriteLine(detail);
        }
        catch (Exception outputException) when (
            outputException is IOException or ObjectDisposedException)
        {
            // 调试旁路不是产品诊断通道，终端或 Trace 监听器不可用时不能反向破坏宿主。
        }
    }
}
