# 源码地图：从概念找到工程和代码

> 层级：Host 全景。核对日期：2026-09-28。依据：当前工作树源码、现行契约及文中测试；不代表发布或实机验收结论。

[返回文档总览](../README.md)

## 工程层次

下表的 SDK、Icons 和 Packaging 包 README 属于源码仓库材料，未作为安装版帮助资源收录；请在仓库阅读器中打开。

| 位置 | 阅读目的 |
| --- | --- |
| [Host 应用](../../../Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj) | 组合、运行、UI、存储及框架适配 |
| [基础 SDK](../../../Host/MyAvaloniaManagement.PluginSdk/README.md) | 插件业务模型和基础契约 |
| [UI SDK](../../../Host/MyAvaloniaManagement.PluginSdk.UI/README.md) | 显式注册、Avalonia 能力与独立预览 |
| [Workflow SDK](../../../Host/MyAvaloniaManagement.PluginSdk.Workflow/README.md) | Workflow 契约辅助工具 |
| [Icons](../../../Host/MyAvaloniaManagement.Icons/README.md) | 公共图标资源 |
| [主仓说明](../../../README.md)中的 Plugins | 示例消费者 MyPlugTest |
| [Build 包](../../../Packaging/MyAvaloniaManagement.Plugin.Build/README.md)与[模板包](../../../Packaging/MyAvaloniaManagement.Plugin.Templates/README.md) | 插件构建与交付边界 |

## Business 不是一个单独的业务层

Business 下同时存在用例编排、运行状态、基础设施和 UI 适配。判断依赖不能仅看目录名。比如 Presentation 包含窗口与命令呈现，Composition 包含组合根，Storage 包含文件端口和本机实现。

| 概念模块 | 主要目录 |
| --- | --- |
| 启动与退出 | Business/Composition、Startup、Restart、Lifecycle |
| 插件体系 | Business/Plugins 下 Discovery、Registration、Enablement、Installation |
| 工作区 | Business/Workspace |
| 文档 | Business/Documents 及 Ownership |
| Dock 与布局 | Business/Docking、Layout、Views/HostFloatingWindow.cs |
| 命令 | Business/Commands、Presentation/Commands |
| 跨插件调用 | Business/WorkflowActions |
| 界面 | Views、ViewModels、Business/Presentation |
| 支撑 | Business/Storage、Diagnostics、Compatibility、Help、Appearance |

这些路径均相对于 Host 应用目录。可以从[源码索引](../11-reference/03-code-index.md)直接跳到具体文件。

## 阅读一个类的四步

1. 先读构造函数和字段，识别它借用什么、持有什么。
2. 再找公开或 internal 用例入口，区分查询和修改。
3. 找到提交点与 finally/Dispose，检查失败时状态是否仍一致。
4. 最后读测试中的反例，确认这里保护的行为。

例如理解保存，不必先穷举所有 ViewModel。先读 DocumentSaveService 的 SaveCoreAsync，找到主文件写入，再向上看协调器、向下看存储。理解为什么关闭后仍不能释放，则从 HostRuntimeShutdown 反向查各个排空参与者。

## 测试分层

Host 单元测试主要保护规则、状态、协调和序列化；PluginTests 使用真实插件/容器/程序集或进程夹具验证边界；UiTests 保护控件、绑定、窗口及 Dock 行为。工程命名不代表每个测试都纯粹属于某一技术层，应以夹具和断言为准。

[测试索引](../11-reference/04-test-index.md)按风险映射。本文引用某个测试表示它是核对入口，不表示每次编辑文档都已运行该测试，更不表示已完成实机验收。

## 框架补丁的阅读位置

[Avalonia 跨窗布局补丁](../../../patches/avalonia-cross-window-layout/README.md)和[Dock 区域填充补丁](../../../patches/dock-area-fill/README.md)属于运行资产与适配边界。先读 Host 如何使用，再读补丁为何需要；不要把框架内部行为混入插件业务契约。

下一步从[运行时模块](../01-runtime/README.md)开始。也可以先选择一个[完整场景](../10-end-to-end/README.md)跟随调用链。
