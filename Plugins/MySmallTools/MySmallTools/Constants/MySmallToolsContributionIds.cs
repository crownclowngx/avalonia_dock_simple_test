using MyAvaloniaManagement.PluginSdk;

namespace MySmallTools.Constants;

/// <summary>集中定义 MySmallTools 对 Host V2 暴露的稳定贡献身份。</summary>
/// <remarks>
/// 这些 ID 已经进入用户配置和插件清单，迁移只改变承载模型，不能改变业务身份。
/// Legacy GUID 不再属于 V2 输入，因此不在生产程序集保留双写或别名兼容路径。
/// </remarks>
public static class MySmallToolsContributionIds
{
    public static readonly PluginId Plugin = new("myavalonia.plugin.my-small-tools");
    public static readonly DocumentTypeId SecretVideoPlayerDocument =
        new("myavalonia.plugin.my-small-tools.document.secret-video-player");
    public static readonly DocumentTypeId VideoEncryptorDocument =
        new("myavalonia.plugin.my-small-tools.document.video-encryptor");
    public static readonly DocumentTypeId SecretVideoLibraryDocument =
        new("myavalonia.plugin.my-small-tools.document.secret-video-library");
    public static readonly DocumentTypeId VideoDecryptorDocument =
        new("myavalonia.plugin.my-small-tools.document.video-decryptor");
}
