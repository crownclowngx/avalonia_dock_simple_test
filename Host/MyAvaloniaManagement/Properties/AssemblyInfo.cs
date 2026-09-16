using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("MyAvaloniaManagement.PluginTests")]
[assembly: InternalsVisibleTo("MyAvaloniaManagement.Tests")]
[assembly: InternalsVisibleTo("MyAvaloniaManagement.UiTests")]
// 验收入口与 Host 同仓维护，只读取内部证据和身份，不形成新的插件公开契约。
[assembly: InternalsVisibleTo("MyAvaloniaManagement.Compatibility")]
// 真实窗口 Harness 与插件领域测试只在仓库内验证 Host 行为；friend access 不会把
// Host 实现重新暴露为插件二进制契约，也不得被生产插件项目使用。
[assembly: InternalsVisibleTo("VideoSecurityPlayer.Playback.IntegrationHarness")]

[assembly: InternalsVisibleTo("VideoSecurityPlayer.HostTests")]
[assembly: InternalsVisibleTo("VideoSecurityPlayer.HostUiTests")]

[assembly: InternalsVisibleTo("DaTangWorkPlugin.HostTests")]
[assembly: InternalsVisibleTo("DaTangWorkPlugin.HostUiTests")]
