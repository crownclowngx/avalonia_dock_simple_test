using MyAvaloniaManagement.PluginSdk;

namespace MyPlugTest.Constants;

/// <summary>
/// 集中保存 MyPlugTest 对 Host V2 声明的稳定身份。
/// </summary>
/// <remarks>
/// 这些值属于贡献协议而不是保存实现。类名刻意不再包含 Save，避免把 Document、Tool 和插件身份
/// 错误归入持久化职责。V2 没有历史 ID 别名，旧 GUID 与旧 Tool 名称不会进入 Registry 或布局恢复。
/// </remarks>
public static class MyPlugTestContributionIds
{
    /// <summary>获取 manifest 与模块共同使用的插件身份。</summary>
    public static readonly PluginId Plugin = new("myavalonia.plugin.my-plug-test");

    /// <summary>获取可持久化欢迎 Document 的稳定身份。</summary>
    public static readonly DocumentTypeId WelcomeDocument =
        new("myavalonia.plugin.my-plug-test.document.welcome");

    /// <summary>获取消息接收 Document 的稳定身份。</summary>
    public static readonly DocumentTypeId MessageReceiverDocument =
        new("myavalonia.plugin.my-plug-test.document.message-receiver");

    /// <summary>获取逐行 HTTP GET Document 的稳定身份。</summary>
    public static readonly DocumentTypeId BatchHttpGetDocument =
        new("myavalonia.plugin.my-plug-test.document.batch-http-get");

    /// <summary>获取 Excel GET 地址生成 Document 的稳定身份。</summary>
    public static readonly DocumentTypeId ExcelGetUrlGeneratorDocument =
        new("myavalonia.plugin.my-plug-test.document.excel-get-url-generator");

    /// <summary>获取自定义 Tool 的稳定身份。</summary>
    public static readonly ToolTypeId CustomTool =
        new("myavalonia.plugin.my-plug-test.tool.custom");
}
