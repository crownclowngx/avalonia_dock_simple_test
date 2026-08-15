using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 在实例化插件代码之前验证 Managed Plugin 的唯一模块入口。
/// </summary>
/// <remarks>
/// 设计意图：该验证器只解释类型结构，不构造模块、不读取 <see cref="IPluginModule.PluginId"/>，
/// 也不调用 <see cref="IPluginModule.ConfigureServices"/>。调用方因此可以把无模块、重复模块或
/// 构造契约错误限制在单个插件目录内，而不会向共享服务集合留下部分注册。
/// </remarks>
internal static class PluginModulePreflight
{
    /// <summary>
    /// 验证预检类型中是否存在唯一且可按模块引导契约创建的模块类型。
    /// </summary>
    /// <param name="types">入口程序集已经完整加载的类型集合。</param>
    /// <param name="moduleType">成功时返回唯一模块类型。</param>
    /// <param name="errorCode">失败时返回可用于自动化判定的稳定错误码。</param>
    /// <param name="errorDetail">失败时返回不执行插件代码即可形成的简短原因。</param>
    /// <returns>模块结构是否满足 Managed Plugin v1 约定。</returns>
    internal static bool TryValidate(
        IReadOnlyList<Type> types,
        out Type? moduleType,
        out string? errorCode,
        out string? errorDetail)
    {
        ArgumentNullException.ThrowIfNull(types);

        moduleType = null;
        errorCode = null;
        errorDetail = null;

        var candidates = types
            .Where(type => typeof(IPluginModule).IsAssignableFrom(type)
                           && !type.IsAbstract
                           && !type.IsInterface)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0)
        {
            errorCode = HostDiagnosticCodes.PluginModuleMissing;
            errorDetail = "入口程序集没有实现 IPluginModule 的具体模块类型。";
            return false;
        }

        if (candidates.Length > 1)
        {
            errorCode = HostDiagnosticCodes.PluginModuleMultiple;
            errorDetail = $"入口程序集包含 {candidates.Length} 个 IPluginModule 模块类型。";
            return false;
        }

        if (candidates[0].GetConstructor(Type.EmptyTypes) is null)
        {
            errorCode = HostDiagnosticCodes.PluginModuleConstructorInvalid;
            errorDetail = $"模块 {candidates[0].FullName} 缺少 public 无参构造函数。";
            return false;
        }

        moduleType = candidates[0];
        return true;
    }
}
