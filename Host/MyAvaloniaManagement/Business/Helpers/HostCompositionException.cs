using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 标识一个参与宿主组合的具体类型及其程序集来源。
/// </summary>
public sealed record HostCompositionContributor(
    string TypeName,
    string AssemblyName);

/// <summary>
/// 宿主启动阶段发现的一条确定性契约诊断。
/// </summary>
public sealed record HostCompositionDiagnostic(
    string Code,
    string? StableId,
    IReadOnlyList<HostCompositionContributor> Contributors);

/// <summary>
/// 插件模块或扩展贡献无法形成无歧义注册表时抛出的异常。
/// </summary>
/// <remarks>
/// 设计意图：内部可信插件发生身份冲突时必须尽早失败，不能任意选择一个实现继续运行。
/// Diagnostics 按错误码、稳定 ID 和来源排序，使 CI、日志与本地启动得到完全相同的结果。
/// </remarks>
public sealed class HostCompositionException : Exception
{
    public HostCompositionException(IEnumerable<HostCompositionDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics, out var snapshot))
    {
        Diagnostics = new ReadOnlyCollection<HostCompositionDiagnostic>(snapshot);
    }

    public IReadOnlyList<HostCompositionDiagnostic> Diagnostics { get; }

    private static string BuildMessage(
        IEnumerable<HostCompositionDiagnostic> diagnostics,
        out List<HostCompositionDiagnostic> snapshot)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        snapshot = diagnostics
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.StableId, StringComparer.Ordinal)
            .Select(item => item with
            {
                Contributors = item.Contributors
                    .OrderBy(source => source.AssemblyName, StringComparer.Ordinal)
                    .ThenBy(source => source.TypeName, StringComparer.Ordinal)
                    .ToArray()
            })
            .ToList();
        if (snapshot.Count == 0)
        {
            throw new ArgumentException("至少需要一条组合诊断。", nameof(diagnostics));
        }

        return "宿主插件组合失败：" + string.Join(
            "; ",
            snapshot.Select(item =>
                $"{item.Code}[{item.StableId ?? "-"}] " +
                string.Join(", ", item.Contributors.Select(source =>
                    $"{source.TypeName} ({source.AssemblyName})"))));
    }
}
