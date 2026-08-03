namespace BiliDownloader.Models;

/// <summary>
/// 最终输出文件发生冲突时采用的处理策略。
/// <para>
/// 该枚举属于可持久化的下载意图，而不是某个 ViewModel 的临时状态。将策略放在模型层，
/// 可以保证 Document 恢复、预设复用、提交预检和后台重试始终观察同一项用户选择。
/// </para>
/// </summary>
public enum FileConflictPolicy
{
    /// <summary>目标基础文件名已被占用时跳过该项，不创建后台任务。</summary>
    Skip,

    /// <summary>用户在本次提交明确确认后，使用暂存文件原子替换已有成品。</summary>
    Overwrite,

    /// <summary>只恢复身份与长度事实均匹配的未完成任务；不把已有成品当作续传文件。</summary>
    ResumeVerified,

    /// <summary>为冲突项追加稳定序号；这是旧 Document 的安全兼容默认值。</summary>
    AutoNumber,
}

public static class FileConflictPolicyText
{
    public static string ToDisplayText(this FileConflictPolicy policy) => policy switch
    {
        FileConflictPolicy.Skip => "跳过已有文件",
        FileConflictPolicy.Overwrite => "覆盖已有文件",
        FileConflictPolicy.ResumeVerified => "校验后续传",
        _ => "自动追加序号",
    };
}
