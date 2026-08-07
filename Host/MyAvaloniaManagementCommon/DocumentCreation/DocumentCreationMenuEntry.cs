namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 宿主菜单使用的扁平创建项，同时携带稳定文档类型和可选入口意图。
/// </summary>
public sealed class DocumentCreationMenuEntry
{
    public DocumentCreationMenuEntry(
        string documentTypeId,
        string creationIntentId,
        string displayName,
        string description,
        string iconPath,
        string menuCategory)
    {
        DocumentTypeId = documentTypeId;
        CreationIntentId = creationIntentId;
        DisplayName = displayName;
        Description = description;
        IconPath = iconPath;
        MenuCategory = menuCategory;
    }

    public string DocumentTypeId { get; }
    public string CreationIntentId { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string IconPath { get; }
    public string MenuCategory { get; }
}
