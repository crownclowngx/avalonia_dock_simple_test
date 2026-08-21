using System;
using System.Collections.Generic;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.PluginSdk.UI;
using LegacyDocumentId = MyAvaloniaManagementCommon.DocumentCreation.DocumentTypeId;
using LegacyDocumentMetadata = MyAvaloniaManagementCommon.DocumentCreation.DocumentMetadata;
using LegacyIntentId = MyAvaloniaManagementCommon.DocumentCreation.CreationIntentId;
using LegacyMenuEntry = MyAvaloniaManagementCommon.DocumentCreation.DocumentCreationMenuEntry;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 集中保存 G7/G8 前仍需读取的 Document v1 与 layout v1 历史身份。
/// </summary>
/// <remarks>
/// 本映射是磁盘阶段桥，不是贡献元数据。声明式 Descriptor 和 Registry 永远只保存 V2 主 ID；
/// 旧 GUID、短名称和别名只能在旧格式输入边界被归一化，且不会写回 public SDK。
/// </remarks>
internal static class LegacyContributionIdMap
{
    private static readonly IReadOnlyDictionary<string, string> DocumentAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DD7A1E38-07C5-B38C-FB02-1B991896EF49"] =
                HostExtensionIds.V2WelcomeDocument.Value,
        };
    private static readonly IReadOnlyDictionary<string, string> ToolAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fileSystemTree"] = HostExtensionIds.V2FileSystemTree.Value,
            ["plugGroupMenu"] = HostExtensionIds.V2PluginMenu.Value,
            ["pluginStatus"] = HostExtensionIds.V2PluginStatus.Value,
            ["toolManagement"] = HostExtensionIds.V2ToolManagement.Value,
        };

    internal static LegacyDocumentId ResolveDocument(LegacyDocumentId documentTypeId)
    {
        ArgumentNullException.ThrowIfNull(documentTypeId);
        return new LegacyDocumentId(
            DocumentAliases.GetValueOrDefault(documentTypeId.Value, documentTypeId.Value));
    }

    internal static LegacyDocumentMetadata ToLegacyMetadata(DocumentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new LegacyDocumentMetadata(
            new LegacyDocumentId(descriptor.DocumentTypeId.Value),
            descriptor.DisplayName)
        {
            Description = descriptor.Description,
            IconPath = descriptor.IconPath,
            MenuCategory = descriptor.MenuCategory,
            ShowInMenu = true,
        };
    }

    internal static string ResolveTool(string toolId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        return ToolAliases.GetValueOrDefault(toolId, toolId);
    }

    internal static LegacyMenuEntry ToLegacyMenuEntry(DocumentCreationMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new LegacyMenuEntry(
            new LegacyDocumentId(entry.DocumentTypeId.Value),
            entry.CreationIntentId is null
                ? null
                : new LegacyIntentId(entry.CreationIntentId.Value),
            entry.DisplayName,
            entry.Description,
            entry.IconPath,
            entry.MenuCategory);
    }
}
