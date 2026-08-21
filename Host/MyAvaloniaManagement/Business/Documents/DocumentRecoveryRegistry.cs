using System;
using System.Collections.Generic;
using MyAvaloniaManagement.Business.Docking;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>
/// 保存“从哪个损坏主文件恢复”的宿主临时事实。
/// </summary>
/// <remarks>
/// 恢复保护不是插件业务状态，不能要求每个插件重复实现。该注册表让恢复出的 Document
/// 保持原插件类型，同时由宿主统一强制另存、拒绝覆盖原件并支持重复打开时激活已有标签。
/// </remarks>
internal sealed class DocumentRecoveryRegistry
{
    internal const string BackupSuffix = ".recovery.bak";

    private readonly Dictionary<ManagedDocumentDockable, RecoveryEntry> _byDocument = [];
    private readonly Dictionary<string, ManagedDocumentDockable> _bySourcePath =
        new(StringComparer.OrdinalIgnoreCase);

    internal void Register(ManagedDocumentDockable document, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = DocumentPathIdentity.Normalize(sourcePath);
        var entry = new RecoveryEntry(normalized, GetBackupPath(normalized));
        _byDocument[document] = entry;
        _bySourcePath[normalized] = document;
    }

    internal bool TryGet(ManagedDocumentDockable document, out RecoveryEntry entry) =>
        _byDocument.TryGetValue(document, out entry!);

    internal bool TryGetBySourcePath(
        string sourcePath,
        out ManagedDocumentDockable document) =>
        _bySourcePath.TryGetValue(
            DocumentPathIdentity.Normalize(sourcePath),
            out document!);

    internal void Clear(ManagedDocumentDockable document)
    {
        if (!_byDocument.Remove(document, out var entry))
        {
            return;
        }

        _bySourcePath.Remove(entry.SourcePath);
    }

    internal static string GetBackupPath(string primaryPath) =>
        $"{DocumentPathIdentity.Normalize(primaryPath)}{BackupSuffix}";

    internal sealed record RecoveryEntry(string SourcePath, string BackupPath);
}
