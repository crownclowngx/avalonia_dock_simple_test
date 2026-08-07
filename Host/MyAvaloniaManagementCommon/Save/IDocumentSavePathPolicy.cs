namespace MyAvaloniaManagementCommon.Save;

/// <summary>
/// 为需要保护原文件的文档提供可选保存路径策略。
/// </summary>
/// <remarks>
/// 未知未来版本可以安全展示少量公共字段，但普通“保存”可能丢失宿主尚不理解的数据。
/// 宿主通过此窄接口强制选择新路径，并在磁盘写入真正成功后通知文档解除保护。
/// </remarks>
public interface IDocumentSavePathPolicy
{
    /// <summary>当前保存是否必须选择不同于原文件的新路径。</summary>
    bool RequiresSaveAs { get; }

    /// <summary>供界面展示的稳定中文原因，不包含文件正文或敏感数据。</summary>
    string SaveAsReason { get; }

    /// <summary>
    /// 宿主成功写入新文件后调用。写入失败时不得调用，以免错误解除原文件保护。
    /// </summary>
    void NotifySaveCompleted(string filePath);
}
