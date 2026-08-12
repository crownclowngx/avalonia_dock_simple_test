using System;
using System.Collections.Generic;
using System.Linq;

namespace MyAvaloniaManagement.Business.Diagnostics;

/// <summary>
/// 在生产组合根创建失败后，把只读诊断快照交给最小 Avalonia 错误界面。
/// </summary>
/// <remarks>
/// 设计意图：失败路径没有可用 DI 容器，不能通过主工作台服务传递状态；该进程级槽位
/// 只在 Main 进入错误应用分支前设置一次，不承担一般运行期服务定位职责。
/// </remarks>
internal sealed record HostStartupFailureContext(
    IReadOnlyList<HostDiagnosticRecord> Diagnostics,
    string? LogPath)
{
    private static HostStartupFailureContext? _current;

    internal static HostStartupFailureContext? Current => _current;

    internal static void Set(
        IEnumerable<HostDiagnosticRecord> diagnostics,
        string? logPath)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _current = new HostStartupFailureContext(
            diagnostics
                .OrderByDescending(item => item.Severity)
                .ThenBy(item => item.Code, StringComparer.Ordinal)
                .ThenBy(item => item.Sequence)
                .ToArray(),
            logPath);
    }

    internal static void Clear() => _current = null;
}
