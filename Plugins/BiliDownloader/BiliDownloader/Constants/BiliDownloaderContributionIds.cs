using MyAvaloniaManagement.PluginSdk;

namespace BiliDownloader.Constants;

/// <summary>
/// 集中保存 BiliDownloader 在 Host V2 中公开的稳定身份。
/// </summary>
/// <remarks>
/// 本类型只描述当前契约，不保留 GUID 或字符串 Tool 别名。旧信封与旧别名属于
/// Legacy 桥的历史输入，不能重新进入最终插件程序集。
/// </remarks>
public static class BiliDownloaderContributionIds
{
    /// <summary>manifest 与代码共同使用的插件身份。</summary>
    public static readonly PluginId Plugin = new("myavalonia.plugin.bili-downloader");

    /// <summary>下载 Document 的全局稳定身份。</summary>
    public static readonly DocumentTypeId DownloadDocument =
        new("myavalonia.plugin.bili-downloader.document.download");

    /// <summary>调度 Tool 的全局稳定身份。</summary>
    public static readonly ToolTypeId SchedulerTool =
        new("myavalonia.plugin.bili-downloader.tool.scheduler");

    /// <summary>从链接输入开始创建 Document 的意图。</summary>
    public static readonly CreationIntentId QuickUrlIntent = new("quick-url");

    /// <summary>从个人内容来源开始创建 Document 的意图。</summary>
    public static readonly CreationIntentId PersonalSourceIntent = new("personal-source");
}
