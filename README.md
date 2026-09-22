# MyAvaloniaManagement

MyAvaloniaManagement 是基于 .NET 10、Avalonia 和 Dock 的模块化桌面工作台。Host 提供停靠工作区、文件打开与保存、插件生命周期和诊断；业务能力由内部可信 Managed Plugin 贡献。

> 当前说明，核对日期：2026-09-21。版本以 [Directory.Version.props](Directory.Version.props) 为准；操作与维护入口统一见[文档导航](docs/README.md)。

当前产品版本为 `3.0.0`，六个自有 NuGet 包统一为 `3.4.1`，Avalonia / Dock 基线为 `12.1.2` / `12.1.0.6`。Host 的 Avalonia.Base 使用[跨窗布局运行时补丁](patches/avalonia-cross-window-layout/README.md) `12.1.2-host-layout.1`；Dock 呈现层使用[可重建区域回停补丁](patches/dock-area-fill/README.md) `12.1.0.7-area.5`。包发布状态、最低兼容版本与数据格式分别说明，见[版本与交付边界](docs/reference/platform-baseline.md)。

正式支持范围为 Windows x64、同一团队维护的可信进程内插件。更新插件需要退出 Host、整体替换插件目录并重新启动；不提供插件沙箱、在线市场或热卸载。macOS 仍为[实验路径](docs/quick-start/macos-experiment.md)。

V19 复杂度收敛的实际开发验证见[V19 开发记录](docs/archive/records/host-v19/development-acceptance.md)。最新单文件自包含安装版的交付状态、产物身份和清理结果见[本机部署](docs/maintenance/local-deployment.md)。既有[手工验收确认](docs/archive/records/host/manual-acceptance-20260921.md)保留原始范围，本机部署不扩展人工验收结论。

当前源码已实施 [V20 历史布局退役与现行入口收敛](docs/archive/plans/host-v20-layout-retirement-plan.md)：Layout 只支持 V3，旧文件不参与恢复，旧文档创建分组查询已删除。实际验证与逐项去向见[开发记录](docs/archive/records/host-v20/development-acceptance.md)及[专用开发验证](docs/maintenance/host-v20-layout-retirement-verification.md)。后续本机交付见[专用说明](docs/archive/records/host-v20/local-deployment-guide.md)，完成状态以部署 JSON 为准，不扩展 V19 的人工验收范围。

[V21 开发门禁与测试效能收敛](docs/archive/plans/host-v21-gate-and-test-efficiency-plan.md)及[专用开发验证计划](docs/maintenance/host-v21-gate-and-test-efficiency-verification.md)已实施；夹具复制、UI 等待、测试职责和 Gate 证据的实际结果见[开发记录](docs/archive/records/host-v21/development-acceptance.md)。开发阶段仅使用本机验证。

[V22 Host 内置插件源与安装升级方案](docs/roadmap/host-v22-plugin-distribution-plan.md)及[专用开发验证计划](docs/roadmap/host-v22-plugin-distribution-verification.md)已于 2026-09-22 暂停，尚未实施。保留已有方案及 Gitee 默认源约定；明确恢复后重新确认自动下载、ZIP 托管方式及实施范围。

## 最短启动步骤

安装 [global.json](global.json) 指定的 .NET SDK `10.0.302`（允许 latestPatch），在仓库根目录执行：

```powershell
pwsh -NoProfile -File tools/Build-AvaloniaLayoutPatch.ps1
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
- 查看未完成工作：[待办与候选](docs/roadmap/README.md)。
- [V18 命令面板交互](docs/archive/plans/host-v18-command-palette-interaction-plan.md)已实施；测试映射和实机验证边界见[专用开发验证](docs/maintenance/host-v18-command-palette-verification.md)。
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
