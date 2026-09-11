using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>中心读取成功登记的声明，保留真实来源供筛选与专属图标解析。</summary>
internal sealed record ToolCatalogEntry(ToolDescriptor Descriptor, PluginId? OwnerId,
    string SourceName, bool IsAvailable, string UnavailableReason);
