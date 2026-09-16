# 浮窗与 Layout V3 开发验证

> 用途：V11 专用开发操作与故障排查。状态：当前；核对日期：2026-09-16。实现与实际结果见 [V11 开发记录](../archive/records/host-v11/development-acceptance.md)，使用说明见 [浮动窗口指南](../quick-start/floating-windows-and-layout.md)。

## 开发入口

在主仓根目录运行：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

这是开发验证，包含 locked restore、Release 零警告构建、SDK/Host Unit/Plugin/Headless UI/MyPlugTest Unit、API 比较、实际 MyPlugTest ZIP 打包验收。不要用阶段过滤器替代最终完整入口。

本轮不运行 Windows CI、seal、Windows Smoke、发布覆盖率或发布动作。现有发布 Smoke 仍断言 V2 文件，必须在实际发布前适配 V3 并自测，不能为通过它而写回 V2 或放宽阈值。

## 自动化覆盖

| 行为 | 主要测试 |
| --- | --- |
| 严格格式、数量/深度/尺寸边界、V2 原件与未来版本 | DockLayoutV3Tests、DockLayoutV3BoundaryTests、DockLayoutV3StoreTests |
| 隐藏/缺插件分组、稳定身份、跨窗口查询 | DockLayoutTreeTests、DockLayoutWorkspaceStateTests、DockLayoutAvailabilityTests、DockWorkspaceNavigationTests |
| 命令排空、取消、范围变化和迟到许可 | DocumentCloseTests、DockWindowCloseTests、DockLayoutV3UiTests |
| 四个浮动入口、Scope/View 保持、重启与重置回滚 | DockLayoutV3UiTests、MyPlugTestV3UiTests、HostDockAdapterTests |
| 自动保存合并、修订顺序、失败重试与释放 | DockLayoutSaveQueueTests |
| DPI/屏幕工作区与最大化/最小化 | DockScreenPlacementTests、ApplicationAndWindowTests |
| 四向边栏展开、正文复用、快捷键与 Owner | AutoHideRestoreVisualTests、WorkbenchCommandPresentationUiTests、既有视觉回归 |

跨进程写锁使用当前 Release DLL 的真实 internal Store，需 PowerShell 7.6 / .NET 10：

```powershell
pwsh -NoProfile -File tools/Verify-LayoutV3WriterLease.ps1
```

脚本自建临时数据根，只终止自己创建的持锁子进程，验证崩溃释放、第二实例只读、不同数据根独立、只读实例不接管、后续实例迁移及 V2 原字节保持；返回 JSON 结果和 DLL SHA-256。它是本地专项工具，不是 CI 或发布门禁；PowerShell 不满足要求时记录未执行原因，不能记作通过。

## 本地真实桌面

用隔离数据目录启动普通开发产物，不使用日常用户布局：

```powershell
$env:MYAVALONIA_DATA_DIRECTORY = Join-Path $PWD 'artifacts/host-v11/manual-data'
dotnet run --project Host/MyAvaloniaManagement -c Release --no-build
```

结束验证后在同一终端执行 `Remove-Item Env:MYAVALONIA_DATA_DIRECTORY`。人工观察仍需真实桌面环境；Headless 通过不能替代下列结果。

1. 单 Tool、Document、整组拖出/拖回，空主文档区域仍可接收新文档。
2. 浮窗多组分割，关闭取消后焦点和命令继续可用；隐藏最后工具后按原位置重开一个成员。
3. 实际文件选择器和关闭确认的 Owner；跨窗命令面板切换、Esc 与失活。
4. 负坐标屏幕、不同 DPI、移除屏幕、小工作区、最大化/最小化后重启与找回窗口。
5. 带未保存修改重置；取消确认、应用失败回滚、保存失败提示和重试。
6. 原生视频/WebView/下载等外部控件真实业务，浮动不会重复创建资源或提前结束后台任务。

记录 OS、屏幕与缩放、源码/产物身份、操作步骤和结果，不记录业务文档正文或私人路径。缺环境的项目留在 [集中待办](../roadmap/README.md)。

## 文件与故障排查

先退出所有使用该数据根的实例，再在隔离目录制作夹具。使用专项契约中的合法 V3；对主文件、备份分别构造损坏、未知未来 schema、文件占用。验证读取优先级及写入失败后有效文件仍在。

不要手动删除仍有持锁进程的 lock 文件来抢占写入。第二实例整个会话只读；需要写入时结束该实例后重新启动。

运行时恢复记录保留隐藏与不可用工具；磁盘文件不是当前可见树的完整序列化。排查时区分“组记录存在”和“原生窗口存在”。主窗口固定骨架 ID 与 V3 的稳定分组 ID 也不是同一概念。

最终验证前先定稿 MD：这些文档会嵌入 Host DLL。最终运行 ID、TRX 摘要、源码差异与产物哈希记录在非嵌入的 `docs/archive/records/host-v11/final-development-evidence.json`，不要在验证后修改嵌入文档并继续沿用旧产物哈希。
