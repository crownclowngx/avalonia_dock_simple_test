using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Plugins.Enablement;

/// <summary>保存用户的禁用集合；未出现的稳定 PluginId 默认启用，集合不随安装目录变化裁剪。</summary>
/// <remarks>冻结集合避免 UI 或调用者反向更改启动事实。该类型不表达加载成功、生命周期或可用性。</remarks>
internal sealed class PluginEnablementSettings(IEnumerable<PluginId> disabledPluginIds)
{
    internal IReadOnlySet<PluginId> DisabledPluginIds { get; } = disabledPluginIds.ToFrozenSet();
    internal bool IsEnabled(PluginId id) => !DisabledPluginIds.Contains(id);

    /// <summary>返回新的意图，其他（包括暂未安装）插件的选择完整保留。</summary>
    internal PluginEnablementSettings WithEnabled(PluginId id, bool enabled) =>
        new(enabled ? DisabledPluginIds.Where(item => item != id) : DisabledPluginIds.Append(id));
}

/// <summary>一次配置读取；Settings 为空表示不能决定启动策略，绝不能解释成默认全部启用。</summary>
/// <remarks>Revision 是文件内容摘要，仅供保存冲突比较。备份恢复有设置但只读；路径和异常正文不进入展示。</remarks>
internal sealed record PluginEnablementReadResult(PluginEnablementSettings? Settings, string Revision, string? ErrorCode = null)
{
    internal static PluginEnablementReadResult Default { get; } = new(new([]), "missing");
    internal bool CanWrite => Settings is not null && ErrorCode is null;
    internal string Message => PluginEnablementMessages.ForCode(ErrorCode);
}

/// <summary>只有 Success 表示落盘提交完成；失败返回旧快照，窗口不能抢先提交开关。</summary>
internal sealed record PluginEnablementSaveResult(bool Success, PluginEnablementReadResult Snapshot, string? ErrorCode = null)
{
    internal string Message => ErrorCode is null ? "设置已保存，重启 Host 后生效。" : PluginEnablementMessages.ForCode(ErrorCode);
}

/// <summary>仅输出固定中文说明，配置文件、插件文本和个人路径不能成为错误文案。</summary>
internal static class PluginEnablementMessages
{
    internal static string ForCode(string? code) => code switch
    {
        null => string.Empty,
        "PLUGIN_ENABLEMENT_RESTART_PENDING" => "正在准备重启，暂不能修改插件设置。",
        "PLUGIN_ENABLEMENT_RECOVERED" => "插件开关主文件不可用，本次采用有效备份；原文件保留，请修复后重启，当前设置只读。",
        "PLUGIN_ENABLEMENT_SCHEMA_UNSUPPORTED" => "插件开关配置版本不受支持，本次不加载插件；请使用匹配版本或修复配置后重启。",
        "PLUGIN_ENABLEMENT_INVALID" => "插件开关配置损坏且无有效备份，本次不加载插件；原文件保留，请修复后重启。",
        "PLUGIN_ENABLEMENT_READ_FAILED" => "无法读取插件开关配置，本次不加载插件；请检查权限或文件占用，修复后重启。",
        "PLUGIN_ENABLEMENT_CONFLICT" => "插件开关已被其他实例修改；本次未保存，请重新读取设置后再操作。",
        "PLUGIN_ENABLEMENT_WRITE_FAILED" => "插件开关保存失败；原设置保留，请检查文件占用、权限或磁盘空间后重试。",
        "PLUGIN_ENABLEMENT_READ_ONLY" => "插件开关配置当前只读，请先修复配置并重启 Host。",
        "PLUGIN_ENABLEMENT_UNKNOWN_PLUGIN" => "该候选没有本次发现确认的唯一插件身份，无法修改开关。",
        _ => "插件开关操作未完成，请检查配置后重试。",
    };
}
