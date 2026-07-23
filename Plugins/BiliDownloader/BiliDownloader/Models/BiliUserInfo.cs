namespace BiliDownloader.Models;

/// <summary>
/// B站用户信息模型
/// </summary>
public class BiliUserInfo
{
    /// <summary>
    /// 用户名
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 用户头像 URL
    /// </summary>
    public string UserAvatar { get; set; } = string.Empty;
}
