using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace MyAvaloniaManagement.Business.Compatibility;

/// <summary>区分磁盘程序集与单文件内嵌程序集，不用应用目录冒充程序集文件。</summary>
internal static class AssemblyFilePath
{
    // 设计思路：IL3000 提醒 Location 在单文件中为空；这里有意读取并返回“无独立文件”，
    // 由调用方选择 EXE 指纹或拒绝确认身份。插件仍从 Controls 加载，需要其真实 DLL 路径。
    // 仅对此已处理空值的边界抑制告警，不关闭整个项目的单文件分析。
    [UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "内嵌或动态程序集返回 null；调用方显式处理，不把空路径用作文件身份。")]
    internal static string? ReadOptional(Assembly assembly) =>
        assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location) ? null : assembly.Location;
}
