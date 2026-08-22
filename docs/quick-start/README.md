# Managed Plugin 快速开始

本组文档以 `MyPlugTest` 为可运行事实源，介绍 manifest schema 2、Core/UI SDK、声明式贡献、普通模型与
Host Dock Adapter。当前仓库处于未发布 V3 G1：版本为 3.0.0，但 API 形状和运行语义仍沿用 V2 G14；
教程不使用 Legacy、Strategy、独立 View 注册或 Dock 类型。

## 完成后你将得到什么

- 一个由严格 `plugin.manifest.json` 精确加载的 V3 G1 插件；
- 一个每次激活都创建独立 DI Scope 的 Document；
- 一个插件级 singleton、关闭时隐藏的 Tool；
- 一个由 Host 创建 View 并设置 `DataContext` 的完整 UI 链路；
- 一个不携带 SDK、Avalonia、Dock 或 Host 共享程序集的独立 ZIP。

## 前置条件

- Windows、PowerShell 7；
- 仓库 [`global.json`](../../global.json) 指定的 .NET 10 SDK；
- 能够构建 `Host/MyAvaloniaManagement`。

## 生命周期速查

| 能力 | 所有者 | 生命周期 |
| --- | --- | --- |
| Document 模型与局部服务 | Document Scope | 每个标签独立；关闭时取消后释放 |
| Tool 模型 | 插件 Provider | 每个插件一个实例；隐藏/恢复不重建 |
| View | Host Adapter | 激活时创建，模型由插件容器拥有 |
| 插件私有 singleton | 插件 Provider | 插件关闭时释放 |
| `IPluginLifecycle` | 插件 Provider + Host 协调器 | 仅后台资源确有启停需求时使用 |

推荐阅读顺序：

1. [创建 Managed 插件](./create-managed-plugin.md)
2. [添加 Document 与 Tool](./add-document-and-tool.md)
3. [验证与排错](./verification-and-troubleshooting.md)

完整实现与迁移取舍见 [`MyPlugTest`](../../Plugins/MyPlugTest/MyPlugTest/) 和 [G9 专项记录](../plan-history/host-v2/g9-my-plug-test-v2.md)。
