# MyAvaloniaManagement

MyAvaloniaManagement 是基于 .NET 10、Avalonia 和 Dock 的模块化桌面工作台。Host 提供停靠工作区、文件打开与保存、插件生命周期和诊断；业务能力由内部可信 Managed Plugin 贡献。

> 当前说明，核对日期：2026-09-16。版本以 [Directory.Version.props](Directory.Version.props) 为准；操作与维护入口统一见[文档导航](docs/README.md)。

当前产品版本为 `3.0.0`，六个自有 NuGet 包统一为 `3.4.1`，Avalonia / Dock 基线为 `12.1.2` / `12.1.0.6`。Host 的 Dock 呈现层使用[可重建区域回停补丁](patches/dock-area-fill/README.md) `12.1.0.7-area.5`。包发布状态、最低兼容版本与数据格式分别说明，见[版本与交付边界](docs/reference/platform-baseline.md)。

正式支持范围为 Windows x64、同一团队维护的可信进程内插件。更新插件需要退出 Host、整体替换插件目录并重新启动；不提供插件沙箱、在线市场或热卸载。macOS 仍为[实验路径](docs/quick-start/macos-experiment.md)。

## 最短启动步骤

安装 [global.json](global.json) 指定的 .NET SDK `10.0.302`（允许 latestPatch），在仓库根目录执行：

```powershell
pwsh -NoProfile -File tools/Build-DockAreaFillPackage.ps1
dotnet build Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug
dotnet build Plugins/MyPlugTest/MyPlugTest/MyPlugTest.csproj -c Debug
dotnet run --project Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug --no-build
```

本仓只保留 Host 与示例插件 MyPlugTest。其他业务插件独立交付，不是主仓启动、构建或默认验证的前提。插件从 Host 对应的 `Controls/<PluginFolder>/` 加载；示例插件构建完成后再以 `--no-build` 启动 Host。

## 核心模型

| 对象 | 用途与寿命 |
| --- | --- |
| Document | 多实例工作页面；每个实例拥有独立 Scope，关闭后释放 |
| Tool | 插件 Provider 中的单例辅助面板；关闭表示隐藏，恢复同一实例 |
| 插件服务 | 仓储、后台任务等业务能力；寿命不依赖页面是否可见 |
| Workbench Command | 菜单、快捷键与命令面板共用的用户操作 |
| Workflow Action | 通过受控 JSON 契约调用的跨插件能力 |

## 开发与维护

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

`verify` 验证本仓 Host 与 MyPlugTest，不启动 Windows Smoke，也不授予发布资格。完整说明见[主仓验证与封板](docs/maintenance/verification.md)。

- 使用工作台：[功能、页面与工具搜索](docs/quick-start/workbench-search.md)、[浮动窗口与布局恢复](docs/quick-start/floating-windows-and-layout.md)。
- 开发独立插件：[Managed Plugin 快速开始](docs/quick-start/README.md)。
- 维护 Host：[内部架构与兼容约束](Host/MyAvaloniaManagement/docs/README.md)。
- 查看未完成工作：[待办与验收](docs/roadmap/README.md)。
- 查找过去的决策、失败与发布证据：[历史归档](docs/archive/README.md)。

## 仓库结构

```text
Host/         Host、SDK、图标资源及测试；应用内帮助原文
Plugins/      MyPlugTest 示例插件及测试
Packaging/    Build 包和插件项目模板
tools/        Gate 验证工具与帮助资源开发工具
build/        插件构建协议、macOS 实验打包入口
docs/         当前指南、契约、维护说明、待办、理论与历史归档
```

许可证见 [LICENSE](LICENSE)。
