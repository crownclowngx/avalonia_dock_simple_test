using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagementCommon.Save;

/// <summary>
/// 宿主与插件共享的 Document 保存信封。
/// </summary>
public sealed class DocumentSaveData
{
    public required DocumentTypeId DocumentTypeId { get; set; }
    public required string Title { get; set; }
    public DateTime SaveTime { get; set; }
    public required string Content { get; set; }
    public required string PluginMetadata { get; set; }
}
