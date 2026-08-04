using BiliDownloader.Models;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;

namespace BiliDownloader.Services.Download;

/// <summary>错误行动执行结果；取消属于正常结果，不使用异常污染状态栏。</summary>
public sealed record DownloadFailureActionResult(bool Success, string Message);

/// <summary>
/// 错误行动应用服务。它只负责把有限行动路由到已有领域入口，不拥有下载状态机；
/// Coordinator 仍然是重试、继续、迁移和合并重试的唯一事实修改者。
/// </summary>
public interface IDownloadFailureActionService
{
    /// <summary>
    /// 执行策略生成的有限行动。用户取消和可预期失败以结果返回；任务状态写入由 Coordinator 完成。
    /// </summary>
    Task<DownloadFailureActionResult> ExecuteAsync(
        DownloadTaskRecord task,
        DownloadFailureActionKind action,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 默认错误行动路由器。该类不按错误类型重新判断按钮，而是执行展示策略给出的行动枚举，
/// 从而让任务卡片、紧凑菜单和 Document 预检共享同一执行语义。
/// </summary>
public sealed class DownloadFailureActionService : IDownloadFailureActionService
{
    private readonly BiliDownloadCoordinator _coordinator;
    private readonly IFfmpegPackageInstaller _installer;
    private readonly IFfmpegRuntimeLocator _locator;
    private readonly ILoginDialogService _loginDialog;
    private readonly IUserPromptService _prompt;
    private readonly IFileRevealService _fileReveal;
    private readonly IBiliDataPaths _paths;
    private readonly ISettingsRepository _settings;

    public DownloadFailureActionService(
        BiliDownloadCoordinator coordinator,
        IFfmpegPackageInstaller installer,
        IFfmpegRuntimeLocator locator,
        ILoginDialogService loginDialog,
        IUserPromptService prompt,
        IFileRevealService fileReveal,
        IBiliDataPaths paths,
        ISettingsRepository settings)
    {
        _coordinator = coordinator;
        _installer = installer;
        _locator = locator;
        _loginDialog = loginDialog;
        _prompt = prompt;
        _fileReveal = fileReveal;
        _paths = paths;
        _settings = settings;
    }

    public async Task<DownloadFailureActionResult> ExecuteAsync(
        DownloadTaskRecord task,
        DownloadFailureActionKind action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            switch (action)
            {
                case DownloadFailureActionKind.LoginAndContinue:
                    if (!await _loginDialog.EnsureLoggedInAsync(cancellationToken))
                        return new(false, "登录未完成，任务保持原状态。");
                    await _coordinator.RetryTaskAsync(task);
                    return new(true, "登录成功，任务已重新排队。");

                case DownloadFailureActionKind.InstallOrRepairFfmpeg:
                    var installation = await _installer.InstallOrRepairAsync(cancellationToken);
                    if (!installation.Success) return new(false, installation.Message);
                    await _settings.InitAsync();
                    await _settings.SetSettingAsync("ffmpeg_custom_path", "");
                    await _coordinator.RetryMergeAsync(task.TaskId);
                    return new(true, "ffmpeg 已修复，合并阶段已完成。");

                case DownloadFailureActionKind.SelectCustomFfmpeg:
                    var executable = await _prompt.PickFfmpegExecutableAsync();
                    if (string.IsNullOrWhiteSpace(executable)) return new(false, "已取消选择 ffmpeg。");
                    if (!await _locator.ValidatePathAsync(executable, cancellationToken))
                        return new(false, "所选文件不是可用的 ffmpeg。");
                    _locator.CustomPath = executable;
                    await _settings.InitAsync();
                    await _settings.SetSettingAsync("ffmpeg_custom_path", executable);
                    if (task.ErrorType is "ffmpeg" or "merge")
                        await _coordinator.RetryMergeAsync(task.TaskId);
                    return new(true, "自定义 ffmpeg 已启用。");

                case DownloadFailureActionKind.ChangeOutputDirectory:
                    var directory = await _prompt.PickFolderAsync("选择新的任务输出目录", task.OutputDirectory);
                    if (string.IsNullOrWhiteSpace(directory)) return new(false, "已取消更换输出目录。");
                    await _coordinator.RelocateTaskOutputAsync(task.TaskId, directory);
                    return new(true, "输出目录已更换，任务已重新排队。");

                case DownloadFailureActionKind.Continue:
                    if (DownloadTaskStatusMapper.FromStorageString(task.Status) == DownloadTaskStatus.Paused)
                        await _coordinator.ResumeTaskAsync(task.TaskId);
                    else
                        await _coordinator.RetryTaskAsync(task);
                    return new(true, "任务已重新检查并继续。");

                case DownloadFailureActionKind.Retry:
                    await _coordinator.RetryTaskAsync(task);
                    return new(true, "任务已重新排队。");

                case DownloadFailureActionKind.RetryMerge:
                    await _coordinator.RetryMergeAsync(task.TaskId);
                    return new(true, "合并阶段已完成。");

                case DownloadFailureActionKind.OpenLogs:
                    await _fileReveal.RevealAsync(_paths.LogDirectory);
                    return new(true, "已打开日志目录。");

                case DownloadFailureActionKind.Restart:
                    await _coordinator.RestartTaskAsync(task.TaskId);
                    return new(true, "任务已从零重新开始。");

                default:
                    return new(false, "当前错误没有可执行行动。");
            }
        }
        catch (OperationCanceledException)
        {
            return new(false, "操作已取消，任务保持安全状态。");
        }
        catch (Exception ex)
        {
            return new(false, SensitiveDataSanitizer.Sanitize(ex.Message));
        }
    }
}
