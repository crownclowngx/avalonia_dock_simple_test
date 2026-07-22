namespace BiliDownloader.Messages;

/// <summary>
/// 登录状态变更广播消息，通过消息总线发送给所有 Document 实例
/// </summary>
public class LoginStateChangedMessage
{
    /// <summary>
    /// 当前是否已登录
    /// </summary>
    public bool IsLoggedIn { get; }

    /// <summary>
    /// 用户名（未登录时为 null）
    /// </summary>
    public string? UserName { get; }

    /// <summary>
    /// 用户头像 URL（未登录时为 null）
    /// </summary>
    public string? UserAvatar { get; }

    /// <summary>登录态是否已经持久化到本地。</summary>
    public bool IsPersistent { get; }

    /// <summary>面向用户的简短状态提示。</summary>
    public string? StatusMessage { get; }

    public LoginStateChangedMessage(
        bool isLoggedIn,
        string? userName,
        string? userAvatar,
        bool isPersistent = false,
        string? statusMessage = null)
    {
        IsLoggedIn = isLoggedIn;
        UserName = userName;
        UserAvatar = userAvatar;
        IsPersistent = isPersistent;
        StatusMessage = statusMessage;
    }
}
