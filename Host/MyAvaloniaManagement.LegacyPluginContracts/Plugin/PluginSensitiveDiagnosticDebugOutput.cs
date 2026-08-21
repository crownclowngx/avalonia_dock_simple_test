using System.Diagnostics;

namespace MyAvaloniaManagementCommon.Plugin;

/// <summary>
/// 为插件生命周期提供显式、进程级且不持久化的原始异常调试出口。
/// </summary>
/// <remarks>
/// Common 不能依赖 Host 的诊断实现，否则 Plugin SDK 会反向引用宿主可执行程序集。因此该边界在
/// SDK 程序集内部保持最小实现，并与 Host 使用同一个精确环境变量协议。默认状态只允许稳定错误码、
/// 插件身份和异常类型进入 stderr；只有本地开发者显式设置值为 <c>1</c> 时才输出异常原文。
/// </remarks>
internal static class PluginSensitiveDiagnosticDebugOutput
{
    private const string EnvironmentVariableName =
        "MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS";

    internal static void Write(string errorCode, Exception? exception)
    {
        if (exception is null ||
            !string.Equals(
                Environment.GetEnvironmentVariable(EnvironmentVariableName),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var detail =
            "[敏感诊断已开启] 以下插件生命周期异常原文可能包含密码、Token、正文和本地路径；" +
            "请仅在本地短期使用。" + Environment.NewLine +
            $"errorCode={errorCode}" + Environment.NewLine + exception;
        try
        {
            Trace.TraceWarning(detail);
            Console.Error.WriteLine(detail);
        }
        catch (Exception outputException) when (
            outputException is IOException or ObjectDisposedException)
        {
            // 调试旁路不可用时保持原生命周期结果，不能把日志失败升级为插件失败。
        }
    }
}
