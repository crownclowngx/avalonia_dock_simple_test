using System;
using MyAvaloniaManagement.Business.Plugins.Discovery;

namespace MyAvaloniaManagement.Business.Plugins.Installation;

/// <summary>安装层的受控失败。消息只由 Host 规则生成，界面不得拼接底层异常或任意包内正文。</summary>
internal sealed class PluginInstallException(string code, string message) : Exception(message)
{
    internal string Code { get; } = code;
}

internal enum PluginInstallAction { Install, Upgrade, Reinstall, Downgrade, Restore, Unchanged }
internal enum PluginInstallPhase { Staged, Applying, AwaitingStartup, Committed, RecoveryRequired, RolledBack, Cancelled }

/// <summary>检查完成的不可变候选。路径全部属于 Host 暂存根，不再依赖用户选择的原 ZIP。</summary>
internal sealed record CheckedPluginPackage(string OperationId, string DirectoryName, string PayloadPath,
    PluginManifest Manifest, string ArchiveHash, string ArtifactHash, bool HasReleaseManifest);

/// <summary>纯决策的输入不持有程序集；禁用和加载失败的插件同样具有可更新的磁盘身份。</summary>
internal sealed record InstalledPluginFile(string DirectoryName, PluginManifest Manifest, string ArtifactHash);
internal sealed record PluginInstallPlan(PluginInstallAction Action, string TargetDirectory,
    string? PreviousVersion, string? PreviousHash, bool RequiresExplicitChoice);
internal sealed record PluginInstallPreview(CheckedPluginPackage Package, PluginInstallPlan Plan)
{
    public string PluginId => Package.Manifest.PluginId.Value;
    public string Version => Package.Manifest.PluginVersion.ToString(3);
    public string Summary => $"{PluginId}\n磁盘版本：{Plan.PreviousVersion ?? "未安装"} → 候选版本：{Version}\n" +
        $"入口：{Package.Manifest.EntryPoint.Assembly}\nSDK：{Package.Manifest.Sdk}\n目标目录：{Plan.TargetDirectory}\n" +
        (Package.HasReleaseManifest ? "基础检查通过；与配套发布清单一致。" : "基础检查通过；未进行发布摘要对照，也未提供独立 RID 声明。") +
        "\n安装确认后重启生效；完整旧目录将保留为备份，目录内业务数据不会自动迁移。";
}

/// <summary>持久化结构使用必填属性，防止缺字段被默认值悄悄解释成新的安装指令。</summary>
internal sealed record PluginInstalledRecord
{
    public required string PluginId { get; init; }
    public required string DirectoryName { get; init; }
    public required string Version { get; init; }
    public required string ArtifactHash { get; init; }
    public required string? ArchiveHash { get; init; }
    public required bool HasReleaseManifest { get; init; }
    public required string Validation { get; init; }
    public required string? BackupOperationId { get; init; }
    public required string? BackupVersion { get; init; }
    public required string? BackupHash { get; init; }
}

internal sealed record PluginInstallationIndex
{
    public required int SchemaVersion { get; init; }
    public required PluginInstalledRecord[] Plugins { get; init; }
    internal static PluginInstallationIndex Empty => new() { SchemaVersion = 1, Plugins = [] };
}

/// <summary>
/// 日志先于目录移动落盘。前后摘要和旧登记足以识别移动成功但日志尚未更新的中断，
/// 不使用“文件存在”推测提交成功，也不接受任意绝对路径作为恢复目标。
/// </summary>
internal sealed record PluginInstallOperation
{
    public required int SchemaVersion { get; init; }
    public required string OperationId { get; init; }
    public required PluginInstallAction Action { get; init; }
    public required PluginInstallPhase Phase { get; init; }
    public required string PluginId { get; init; }
    public required string TargetDirectory { get; init; }
    public required string? PreviousVersion { get; init; }
    public required string? PreviousHash { get; init; }
    public required string Version { get; init; }
    public required string ArtifactHash { get; init; }
    public required string? ArchiveHash { get; init; }
    public required bool HasReleaseManifest { get; init; }
    public required PluginInstalledRecord? PreviousRecord { get; init; }
    public required int? OwnerPid { get; init; }
    public required long? OwnerStartedUtcTicks { get; init; }
    public required string? Result { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPending => Phase is PluginInstallPhase.Staged or PluginInstallPhase.Applying or
        PluginInstallPhase.AwaitingStartup or PluginInstallPhase.RecoveryRequired;
}

/// <summary>界面只读取值快照；运行状态仍由原生命周期读模型负责。</summary>
internal sealed record PluginInstallationStatus(PluginInstallOperation? Operation, string Message)
{
    public bool RequiresRestart => Operation?.IsPending == true;
    public bool CanCancel => Operation?.Phase == PluginInstallPhase.Staged;
}
