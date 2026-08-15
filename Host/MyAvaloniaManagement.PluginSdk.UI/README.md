# MyAvaloniaManagement Plugin SDK UI Profile

该依赖 Profile 面向需要直接使用 Fluent、Semi、Ursa 或 Dock UI 类型和资源的 Managed Plugin。
它会同时引入同版本的基础 `MyAvaloniaManagement.PluginSdk`，并把第三方 UI 依赖限制为宿主已经
验证的精确版本。

只使用标准 Avalonia 控件和宿主 `App*` 语义资源的插件不应引用本包。全局主题由宿主统一加载；
插件可以打包自己的局部 `StyleInclude`，但不得向 `Application.Current.Styles` 注入全局主题。

本包不包含 Host 程序集，也不产生需要随插件部署的运行时 DLL。宿主共享 UI 程序集必须由默认
加载上下文提供，插件目录不得携带其副本。

当前包不会自动发布到公共 NuGet 源。仓库尚未选择对外许可证，对外分发前必须完成许可证评审。
