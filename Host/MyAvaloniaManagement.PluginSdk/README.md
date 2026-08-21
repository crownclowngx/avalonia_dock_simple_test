# MyAvaloniaManagement Plugin SDK

本包是 Managed Plugin V2 的平台无关 Core 契约程序集。它只依赖 .NET BCL，并提供稳定身份、
Document 模型、内容快照、关闭观察、插件生命周期和同步事件端口。

Core 不引用 Avalonia、Dock、Newtonsoft、Microsoft DI 或 Host 实现。需要声明模块、私有服务以及
Document/Tool 与 Avalonia View 映射的插件，应引用同版本 `MyAvaloniaManagement.PluginSdk.UI`。

当前仓库处于 V2 G2：最终 SDK 已可独立编译和消费，但 Host 与四个业务插件仍通过不可打包的
Legacy 编译桥运行。该阶段状态不能解释为完整 V2 Host 已经能够加载新 SDK 插件，也不得发布到公共源。
