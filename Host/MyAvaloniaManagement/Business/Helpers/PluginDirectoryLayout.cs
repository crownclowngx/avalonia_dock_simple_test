using System;
using System.IO;
using System.Reflection;
using MyAvaloniaManagement.Business.Diagnostics;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 描述一个满足当前 Managed Plugin 构建约定的插件目录。
/// </summary>
/// <remarks>
/// 设计意图：目录布局只验证清单声明的入口和标准依赖清单，不扫描或猜测其他 DLL。
/// 这样 <see cref="PluginLoadContext"/> 可以完全依赖 .NET 的 deps/RID 图解析依赖，避免重新引入
/// 无清单、无 deps 或按目录碰运气加载的第二套二进制插件协议。
/// </remarks>
internal sealed class PluginDirectoryLayout
{
    private PluginDirectoryLayout(
        string directoryPath,
        string entryAssemblyPath)
    {
        DirectoryPath = directoryPath;
        EntryAssemblyPath = entryAssemblyPath;
    }

    /// <summary>
    /// 规范化后的插件目录绝对路径。
    /// </summary>
    internal string DirectoryPath { get; }

    /// <summary>
    /// 用于创建 <see cref="System.Runtime.Loader.AssemblyDependencyResolver"/> 的唯一入口程序集。
    /// </summary>
    internal string EntryAssemblyPath { get; }

    /// <summary>
    /// 尝试建立插件目录布局。
    /// </summary>
    /// <param name="pluginDirectory">一个插件独占的部署目录。</param>
    /// <param name="layout">成功时返回不可变布局。</param>
    /// <param name="errorCode">失败时返回稳定诊断码。</param>
    /// <param name="errorDetail">失败时返回不包含异常堆栈的简短原因。</param>
    /// <param name="manifest">已经通过严格解析与版本兼容检查的清单。</param>
    /// <returns>目录是否满足清单入口和私有依赖唯一性约定。</returns>
    internal static bool TryCreate(
        string pluginDirectory,
        PluginManifest manifest,
        out PluginDirectoryLayout? layout,
        out string? errorCode,
        out string? errorDetail)
    {
        layout = null;
        errorCode = null;
        errorDetail = null;

        try
        {
            ArgumentNullException.ThrowIfNull(manifest);
            var directoryPath = Path.GetFullPath(pluginDirectory);
            if (!Directory.Exists(directoryPath))
            {
                errorCode = "PLUGIN_ENTRY_INVALID";
                errorDetail = "插件目录不存在。";
                return false;
            }

            var entryAssemblyPath = Path.GetFullPath(
                Path.Combine(directoryPath, manifest.EntryPoint.Assembly));
            if (!File.Exists(entryAssemblyPath))
            {
                errorCode = "PLUGIN_ENTRY_INVALID";
                errorDetail = $"清单入口 {manifest.EntryPoint.Assembly} 不存在。";
                return false;
            }

            // 当前 Managed Plugin 只有标准 deps/RID 图一条依赖解析路径。缺少 deps 时立即拒绝，
            // 不能退回目录索引，否则发布包的真实依赖闭包会再次变成不可审阅的隐式规则。
            var dependencyPath = Path.ChangeExtension(entryAssemblyPath, ".deps.json");
            if (!File.Exists(dependencyPath))
            {
                errorCode = HostDiagnosticCodes.PluginDependencyManifestMissing;
                errorDetail = $"清单入口 {manifest.EntryPoint.Assembly} 缺少同名 .deps.json。";
                return false;
            }

            try
            {
                _ = AssemblyName.GetAssemblyName(entryAssemblyPath);
            }
            catch (BadImageFormatException)
            {
                errorCode = "PLUGIN_ENTRY_INVALID";
                errorDetail = $"清单入口 {manifest.EntryPoint.Assembly} 不是有效托管程序集。";
                return false;
            }

            layout = new PluginDirectoryLayout(directoryPath, entryAssemblyPath);
            return true;
        }
        catch (Exception exception)
        {
            errorCode = "PLUGIN_ENTRY_INVALID";
            errorDetail = exception.GetType().Name;
            return false;
        }
    }

}
