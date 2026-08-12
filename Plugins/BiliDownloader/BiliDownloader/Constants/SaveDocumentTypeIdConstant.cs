using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.ToolCreation;

namespace BiliDownloader.Constants;

/// <summary>BiliDownloader 对宿主公开的全部稳定身份。</summary>
public static class SaveDocumentTypeIdConstant
{
    public static readonly PluginId PluginId = new("myavalonia.plugin.bili-downloader");
    public static readonly DocumentTypeId BiliDownloaderDocumentId =
        new("myavalonia.plugin.bili-downloader.document.download");
    public static readonly DocumentTypeId LegacyBiliDownloaderDocumentId =
        new("A3F7E1B2-9C4D-4E8A-B6F1-2D5E8A7C3B10");
    public static readonly ToolTypeId SchedulerToolId =
        new("myavalonia.plugin.bili-downloader.tool.scheduler");
    public static readonly ToolTypeId LegacySchedulerToolId = new("BiliSchedulerTool");
}
