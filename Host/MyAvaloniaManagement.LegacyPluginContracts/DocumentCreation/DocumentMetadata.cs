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
    /// <summary>创建一种 Document 贡献的不可变身份和显示元数据。</summary>
    /// <param name="documentTypeId">插件拥有的规范稳定 ID。</param>
    /// <param name="displayName">展示给用户的名称。</param>
    /// <param name="legacyIds">仅用于读取旧布局或旧入口的历史别名。</param>
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

    /// <summary>获取新建、保存和诊断使用的主身份。</summary>
    public DocumentTypeId DocumentTypeId { get; }
    /// <summary>获取展示名称。</summary>
    public string DisplayName { get; }
    /// <summary>获取功能说明。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>获取可选图标资源路径。</summary>
    public string IconPath { get; init; } = string.Empty;
    /// <summary>获取该类型是否进入宿主的创建菜单。</summary>
    public bool ShowInMenu { get; init; } = true;
    /// <summary>获取创建菜单分组；宿主只负责展示，不解释业务含义。</summary>
    public string MenuCategory { get; init; } = "未归类插件";
    /// <summary>获取只读历史别名；宿主不会用别名写入新状态。</summary>
    public IReadOnlyList<DocumentTypeId> LegacyIds { get; }
}
