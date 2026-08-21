using System;
using System.IO;
using System.Text.Json;
using MyAvaloniaManagement.Business.Diagnostics;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>
/// 把文档加载、保存和恢复边界的异常转换为稳定提示与最小诊断。
/// </summary>
/// <remarks>
/// 插件初始化和内容捕获可以抛出任意异常，因而 <see cref="Exception.Message"/> 不能被宿主当作可信
/// 展示文本。该映射器只按 Host internal 异常类型选择固定
/// 文案；默认日志也只保留错误码和异常类型。原始异常仅能经显式敏感调试开关进入临时输出。
/// </remarks>
internal static class DocumentPersistenceErrorMapper
{
    internal const string SaveFailureMessage =
        "保存文档失败，请检查目标位置是否可写。文档状态未被修改。";

    internal const string BackupFailureMessage =
        "文档已保存，但恢复备份更新失败；下次保存前请妥善保管主文件。";

    internal const string AcceptChangesFailureMessage =
        "文档已保存，但插件未能确认新的保存基线；关闭前请核对脏状态。";

    internal static string ToOpenFailureMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var reason = exception switch
        {
            DocumentEnvelopeException => "文档内容不受支持或已损坏。",
            JsonException => "文件结构损坏或不是受支持的 Document。",
            NotSupportedException => "当前宿主不支持该 Document 类型。",
            _ => "读取文件失败，请检查文件是否仍然存在且可访问。",
        };
        return $"无法打开所选文件：{reason} 原文件未被修改。";
    }

    internal static void Report(string errorCode, Exception? exception = null)
    {
        Console.Error.WriteLine(
            $"DocumentPersistence errorCode={errorCode} " +
            $"type={exception?.GetType().Name ?? "None"}");
        HostSensitiveDiagnosticDebugOutput.Write(
            errorCode,
            HostDiagnosticPhase.HostBootstrap,
            exception);
    }
}
