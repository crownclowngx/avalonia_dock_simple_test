# Managed Plugin 快速开始

> 用途：外部插件作者入门。状态：当前；核对日期：2026-09-16。事实源：[模板内容](../../Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin)、[版本与交付边界](../reference/platform-baseline.md)。

不需要克隆 Host；需要 .NET 10 SDK、Rider 或其他 .NET IDE，以及 NuGet.org 访问能力。当前模板使用 SDK/UI、Icons、Build 3.4.1，manifest schema 2，目标 Windows x64。

## 最短路径

```powershell
dotnet new install MyAvaloniaManagement.Plugin.Templates@3.4.1
dotnet new myavalonia-plugin -n ExamplePlugin --plugin-id myavalonia.plugin.example
cd ExamplePlugin
dotnet restore --locked-mode
dotnet build -c Debug --no-restore -warnaserror
dotnet test -c Debug --no-build --no-restore
dotnet run --project src/ExamplePlugin.Standalone --no-build
```

模板生成真实 Plugin 程序集、Standalone 预览程序、Tests 和随项目携带的 docs。View、模型、Module 与业务默认放在同一 Plugin 项目；只有存在真实复用需求时才拆分 Core。

## 两层验证

| 能力 | Standalone | 真实 Host |
| --- | --- | --- |
| View、绑定、命令、私有 DI | 快速开发与预览 | 最终集成 |
| Document Scope、Tool singleton | 可以模拟和测试 | 最终所有权事实 |
| manifest、程序集隔离、共享 SDK | 不验证 | 必须验证 |
| Dock、布局、保存、关闭与 Host Port | 仅使用显式替身 | 必须验证 |

模板默认预览一个 MainDocument 和 MainView。多个贡献可扩展为调用同一 Module 的贡献浏览器，不复制完整 Host，也不维护第二份贡献清单。

## 生命周期速查

| 对象 | 寿命 |
| --- | --- |
| Document 模型与局部服务 | 每个实例一个 Scope，最终关闭时释放 |
| Document View | 每个打开实例一个，由工作台设置 DataContext |
| Tool 模型 | 所属插件 Provider singleton |
| Tool View | 展示对象，不拥有 Tool 模型寿命 |
| 插件私有 singleton | 插件 Provider 释放时结束 |

## 阅读顺序

1. [创建插件](create-managed-plugin.md)
2. [增加 Document、Tool 与预览](add-document-and-tool.md)
3. [构建、打包、Host 验收与排错](verification-and-troubleshooting.md)
4. [图标接入](plugin-icons.md)、[Workflow Action 接入](workflow-action-development.md)、[Workbench Command 契约](../reference/workbench-commands.md)

主仓维护者另读 [Gate 验证](../maintenance/verification.md)和[六包发布维护](../maintenance/nuget-release.md)。应用使用说明从[总导航](../README.md)进入。
