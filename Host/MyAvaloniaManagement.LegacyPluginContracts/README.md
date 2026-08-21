# MyAvaloniaManagement Legacy Plugin Contracts

本项目是 Managed Plugin V2 G2 期间保留的仓库内部编译桥。项目名称明确标注为
`MyAvaloniaManagement.LegacyPluginContracts`。G12 后 Host 与四个业务插件均已迁移；为让 G13 删除前的
历史测试夹具与 Harness 继续编译，输出程序集仍暂时保持 `MyAvaloniaManagementCommon.dll`，类型也仍
位于旧命名空间。

它不是 Plugin SDK，也不得被打包或发布：

- `IsPackable=false`，不具有 NuGet 包身份；
- 不再拥有活动的 Public API 基线；历史 v1 文本事实已随 Core SDK 保存；
- 新 Core/UI SDK 均不引用本项目，生成的两个 nupkg 也不包含或依赖本程序集；
- 当前没有生产插件消费者；只允许 G13 删除工作所需的既有测试与 Harness 继续引用，新的生产项目不得增加引用；
- G13 将整体删除本项目，不提供双向适配或长期兼容层。

最终 V2 Core 契约位于
[`MyAvaloniaManagement.PluginSdk`](../MyAvaloniaManagement.PluginSdk/README.md)，Avalonia 与插件注册契约位于
[`MyAvaloniaManagement.PluginSdk.UI`](../MyAvaloniaManagement.PluginSdk.UI/README.md)。这两个包已经可以独立编译消费，
当前 Host 与四个业务插件已使用 manifest v2、每插件独立 Provider、声明式 Registry、Dock Adapter 和
Document v2；本项目只继续承载待 G13 清理的旧模块、Strategy、Dock 与生命周期测试事实。

设计上保留该桥是一次明确的阶段切分：G2 只建立干净 SDK 边界，后续整改包再迁移运行时消费者。
回滚时应把新 Core、真实 UI、两套 v2 API 基线、脚本和本隔离设置作为一个整体处理，不得把新类型重新塞回
`MyAvaloniaManagementCommon` 形成混合程序集。
