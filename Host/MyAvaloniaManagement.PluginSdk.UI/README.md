# MyAvaloniaManagement Plugin SDK UI Profile

> V2 G1 期间本包仍是未发布的 dependency-only 阶段桥；G2 才会把它重建为真实 UI 契约程序集。

该依赖 Profile 面向需要直接使用 Fluent、Semi、Ursa 或 Dock UI 类型和资源的 Managed Plugin。
它会同时引入同版本的基础 `MyAvaloniaManagement.PluginSdk`，并把第三方 UI 依赖限制为宿主已经
验证的精确版本。

只使用标准 Avalonia 控件和宿主 `App*` 语义资源的插件不应引用本包。全局主题由宿主统一加载；
插件可以打包自己的局部 `StyleInclude`，但不得向 `Application.Current.Styles` 注入全局主题。

本包不包含 Host 程序集，也不产生需要随插件部署的运行时 DLL。宿主共享 UI 程序集必须由默认
加载上下文提供，插件目录不得携带其副本。

当前 V2 包不得发布到公共 NuGet 源。G2 契约重建、后续插件迁移和许可证评审完成前，不构成外部分发物。
