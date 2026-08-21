using System;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 在实例化插件代码之前验证 manifest v2 精确指定的模块入口。
/// </summary>
/// <remarks>
/// 设计意图：该验证器只解释类型结构，不构造模块，也不调用
/// <see cref="IPluginModule.Configure(IPluginRegistration)"/>。它不枚举程序集中的其他模块，
/// 因而未声明类型既不能劫持入口，也不能因为偶然新增第二个模块而改变加载结果。
/// </remarks>
internal static class PluginModulePreflight
{
    /// <summary>
    /// 验证清单指定类型是否可按当前 G3 阶段的模块引导契约创建。
    /// </summary>
    /// <param name="entryType">从清单指定入口程序集按完整名称精确取得的类型。</param>
    /// <param name="moduleType">清单精确声明且已经由加载器按大小写敏感全名解析的入口类型。</param>
    /// <param name="errorCode">失败时返回可用于自动化判定的稳定错误码。</param>
    /// <param name="errorDetail">失败时返回不执行插件代码即可形成的简短原因。</param>
    /// <returns>入口类型是否满足 G3 阶段的 Managed Plugin 约定。</returns>
    internal static bool TryValidate(
        Type? entryType,
        out Type? moduleType,
        out string? errorCode,
        out string? errorDetail)
    {
        moduleType = null;
        errorCode = null;
        errorDetail = null;

        if (entryType is null)
        {
            errorCode = HostDiagnosticCodes.PluginEntryInvalid;
            errorDetail = "入口程序集不存在清单指定的入口类型。";
            return false;
        }

        if (!entryType.IsPublic || entryType.IsNested || entryType.IsAbstract ||
            entryType.IsInterface || entryType.ContainsGenericParameters ||
            !typeof(IPluginModule).IsAssignableFrom(entryType))
        {
            errorCode = HostDiagnosticCodes.PluginEntryInvalid;
            errorDetail =
                "入口类型必须是入口程序集中的 public、非抽象、非泛型 V2 IPluginModule 实现。";
            return false;
        }

        if (entryType.GetConstructor(Type.EmptyTypes) is null)
        {
            errorCode = HostDiagnosticCodes.PluginEntryInvalid;
            errorDetail = $"入口类型 {entryType.FullName} 缺少 public 无参构造函数。";
            return false;
        }

        moduleType = entryType;
        return true;
    }
}
