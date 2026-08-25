# Managed Plugin 快速开始

本组文档面向外部插件作者：不需要克隆 Host 仓库，只需要 .NET 10 SDK、Rider 或其他 .NET IDE，以及
能够访问 NuGet.org。当前公开基线是 Plugin SDK `3.0.0`、manifest schema 2、Avalonia 12、Windows x64。

Workflow Action G2 已验证本地候选 SDK `3.1.0` / Templates `1.1.0`，但它们尚未上传或发布；Build
协议未变化，仍为已发布 `1.1.2`。本快速开始的 NuGet.org 命令继续使用公开 Templates `1.0.4`。

## 最短路径

```powershell
dotnet new install MyAvaloniaManagement.Plugin.Templates@1.0.4
dotnet new myavalonia-plugin -n ExamplePlugin --plugin-id myavalonia.plugin.example
cd ExamplePlugin
dotnet restore
dotnet build -c Debug -warnaserror
dotnet test -c Debug --no-build
dotnet run --project src/ExamplePlugin.Standalone
```

模板生成：

```text
ExamplePlugin/
├─ ExamplePlugin.slnx
├─ src/
│  ├─ ExamplePlugin.Plugin/       # 唯一真实插件程序集
│  └─ ExamplePlugin.Standalone/   # Avalonia 独立预览程序
├─ tests/
│  └─ ExamplePlugin.Tests/
└─ docs/                           # 随项目生成的快速开始、职责和部署说明
```

View、ViewModel、`IPluginModule` 和插件业务默认放在同一个 Plugin 项目。只有业务需要被多个插件、命令行
或服务共同消费时才提取 Core；Standalone 与 Tests 都直接引用同一个 Plugin 项目。

## 两层验证

Standalone 是快速开发工作台，真实 Host 是最终验收环境。二者职责不能混为一谈：

| 能力 | Standalone | 真实 Host |
| --- | --- | --- |
| View、绑定、命令和插件私有 DI | 可以 | 可以 |
| 多 Document 页面和 Tool 的快速查看 | 可由极简工作台模拟 | 可以 |
| Document Scope、Tool singleton | 可模拟并做单元测试 | 最终事实 |
| manifest、插件发现、程序集隔离 | 不验证 | 必须验证 |
| 真实 Dock、布局恢复、保存与关闭语义 | 不验证 | 必须验证 |
| Host Port、生命周期和卸载 | 只使用显式 Stub | 必须验证 |

模板 `1.0.4` 自带的 Standalone 直接预览一个 `MainDocument + MainView`。当插件有多个 Document 或 Tool
时，应把它扩展为“贡献浏览器”：调用同一个 Module 收集注册结果，左侧列出贡献，中间打开 Document
标签，右侧或底部显示 Tool。不要复制完整 Host，也不要维护第二份贡献清单。

## 生命周期速查

| 对象 | 生命周期 |
| --- | --- |
| Document Model 与局部服务 | 每打开一个实例创建一个 DI Scope；关闭标签时释放 |
| Document View | 每个打开实例一个，由工作台或 Host 设置 `DataContext` |
| Tool Model | 每种 Tool 在插件 Provider 中一个 singleton |
| Tool View | 展示层对象；不能拥有 Tool Model 生命周期 |
| 插件私有 singleton | 插件 Provider 释放时结束 |

## 阅读顺序

1. [从只有 Rider 和 Avalonia 的机器创建插件](./create-managed-plugin.md)
2. [添加多个 Document、Tool 和独立预览工作台](./add-document-and-tool.md)
3. [编译、打包、真实 Host 验收与排错](./verification-and-troubleshooting.md)

SDK、Build 和模板包的发布方式见
[外部 Managed Plugin 开发、模板与 NuGet 发布指南](../design/external-managed-plugin-development-and-installation-plan.md)。
