namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 宿主菜单使用的不可变创建项，同时携带稳定 Document 类型和可选创建意图。
/// </summary>
public sealed record DocumentCreationMenuEntry(
    DocumentTypeId DocumentTypeId,
    CreationIntentId? CreationIntentId,
    string DisplayName,
    string Description,
    string IconPath,
    string MenuCategory);
