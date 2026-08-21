using System;
using System.Collections.Generic;
using MyAvaloniaManagement.Business.Constants;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 集中保存 layout-v1 仍需读取的 Tool 历史身份。
/// </summary>
/// <remarks>
/// Document V2 不接受任何历史身份，因此这里不再包含 Document 映射。Tool 短名称只在本阶段
/// 明确保留的 layout-v1 输入边界归一化，不会写回声明式 Registry。
/// </remarks>
internal static class LegacyContributionIdMap
{
    private static readonly IReadOnlyDictionary<string, string> ToolAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fileSystemTree"] = HostExtensionIds.V2FileSystemTree.Value,
            ["plugGroupMenu"] = HostExtensionIds.V2PluginMenu.Value,
            ["pluginStatus"] = HostExtensionIds.V2PluginStatus.Value,
            ["toolManagement"] = HostExtensionIds.V2ToolManagement.Value,
        };

    internal static string ResolveTool(string toolId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        return ToolAliases.GetValueOrDefault(toolId, toolId);
    }

}
