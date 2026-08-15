# MyAvaloniaManagement Plugin SDK

该包是 Managed Plugin v1 的基础编译契约，程序集名称保持为
`MyAvaloniaManagementCommon`。它提供插件身份、Document、Tool、生命周期、消息和保存接口，
不包含宿主可执行程序集、全局主题或第三方 UI 控件实现。

普通插件只引用本包，并通过宿主提供的 `App*` 语义资源适配浅色与深色主题。需要直接使用
Semi、Ursa 或 Dock UI 控件的插件应改用同版本的
`MyAvaloniaManagement.PluginSdk.UI`。插件交付目录不得包含本包或宿主共享 UI 程序集的副本。

当前包仅作为仓库和正式发布流水线的可验证制品，不会自动发布到公共 NuGet 源。
仓库尚未选择对外许可证；对外分发前必须由项目所有者补充许可证并完成发布评审。

完整用法见仓库 `docs/quick-start/create-managed-plugin.md` 和
`Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md`。
