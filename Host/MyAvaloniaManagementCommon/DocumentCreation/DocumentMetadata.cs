namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 描述一种 Document 扩展贡献。
/// </summary>
/// <remarks>
/// 元数据在注册后会被多个菜单、保存和恢复流程共享，因此采用不可变对象，避免插件在运行期
/// 修改 ID 或显示属性导致注册表与界面观察到不同事实。旧 ID 只用于输入迁移，宿主永远以
/// <see cref="DocumentTypeId"/> 作为新建和保存时的唯一身份。
/// </remarks>
public sealed class DocumentMetadata
{
    public DocumentMetadata(
        DocumentTypeId documentTypeId,
        string displayName,
        IEnumerable<DocumentTypeId>? legacyIds = null)
    {
        DocumentTypeId = documentTypeId ??
                         throw new ArgumentNullException(nameof(documentTypeId));
        DisplayName = displayName ?? string.Empty;
        LegacyIds = Array.AsReadOnly((legacyIds ?? []).ToArray());
    }

    public DocumentTypeId DocumentTypeId { get; }
    public string DisplayName { get; }
    public string Description { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public bool ShowInMenu { get; init; } = true;
    public string MenuCategory { get; init; } = "未归类插件";
    public IReadOnlyList<DocumentTypeId> LegacyIds { get; }
}
