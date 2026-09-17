# 浮窗与 Layout V3 开发验证

区域默认居中回停的补丁重建、命中规则和新增验收见[区域回停专项](dock-area-fill-verification.md)。本文原有布局与关闭回归继续执行。

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
| P1 局部分割、全宽兼容、分隔条及父引用、主骨架目标判定 | DockFourWayLayoutTests.ToolSplit、WorkspaceSessionAndDockFactoryTests |
| P1 真实 DockManager 同组/跨组/整组移动、浮窗分割回停、V3 文件重启和中间状态不落盘 | DockToolSplitUiTests（Headless 窗口协议，非桌面鼠标验收） |
| P2 真实 ToolChrome 按钮 Click/Command、菜单、关闭/隐藏及工具中心、取消和重试、多组关闭、V3 重启 | DockToolWindowCloseUiTests（Headless，不是 Windows 桌面） |
| P2 Tool 关闭预检、能力约束、一次事件通知、许可清理及异常 | WorkspaceSessionAndDockFactoryTests、DockToolWindowCloseUiTests；原 DockWindowCloseTests 继续保护内容范围 |

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

### V11-P1 桌面补验步骤

1. 在隔离数据根打开两个 Tool，投放到目标中心提示的下方，复现用户截图动作；检查两个工具只在原工具区域上下排列、侧边邻居不移动。
2. 重复上方投放，调整分隔条比例；交换目标组首中尾位置并连续操作，确认内容、工具数量与交互仍正常。
3. 整组浮动，在浮窗内上下分割，再整组拖回主窗工具目标；检查无空白残留窗口、View 状态和后台任务延续。
4. Tool 放到主文档区及拆分文档子组的上下提示，确认仍全宽；检查外侧全局提示、Document 四向分割、全屏限制与关闭取消。
5. 隐藏并重新显示工具，正常退出、重启，核对组顺序、上下关系、比例、显隐和窗口归属。文档不会自动重开。

每项分别填写实际通过、失败或未执行及原因，不以自动化结果代填。遇到 `LAYOUT_TOOL_NORMALIZATION_SKIPPED` 时，原因码表示整理前提不满足；先记录当前有效树和复现步骤，不重置用户布局或把全部拖放视为可回滚事务。修复事实见 [P1 记录](../archive/records/host-v11/p1-tool-split-fix.md)。

### V11-P2 桌面补验步骤

1. 单 Tool 浮动，直接点击浮窗右上角关闭按钮；确认外壳消失、主窗口继续响应，工具中心状态为隐藏。
2. 从工具中心重新显示，检查原位置、尺寸与业务状态；分别通过工具菜单“隐藏工具”和工具中心隐藏最后一项。
3. 同组多个 Tool 逐个关闭，前几次仅隐藏目标，最后一次关闭外壳；多组窗口整体关闭后检查所有工具状态与原分组比例。
4. 取消原生关闭/文档确认后检查内容、活动项与命令可用；混合文档与 Tool 的浮窗隐藏工具不应关闭剩余文档。
5. 正常退出重启，隐藏工具不产生空窗，显示工具恢复原位置与分组。外部下载、视频/WebView 的业务寿命另行验证。

当前自动化覆盖上述窗口协议；真实 Windows 操作仍待实测，结果见 [P2 修复记录](../archive/records/host-v11/p2-tool-window-close-fix.md)。最终验证身份单独写入 [P2 JSON](../archive/records/host-v11/p2-final-development-evidence.json)，不覆盖 P1 记录。

## 文件与故障排查

先退出所有使用该数据根的实例，再在隔离目录制作夹具。使用专项契约中的合法 V3；对主文件、备份分别构造损坏、未知未来 schema、文件占用。验证读取优先级及写入失败后有效文件仍在。

不要手动删除仍有持锁进程的 lock 文件来抢占写入。第二实例整个会话只读；需要写入时结束该实例后重新启动。

运行时恢复记录保留隐藏与不可用工具；磁盘文件不是当前可见树的完整序列化。排查时区分“组记录存在”和“原生窗口存在”。主窗口固定骨架 ID 与 V3 的稳定分组 ID 也不是同一概念。

最终验证前先定稿 MD：这些文档会嵌入 Host DLL。最终运行 ID、TRX 摘要、源码差异与产物哈希记录在非嵌入的 `docs/archive/records/host-v11/final-development-evidence.json`，不要在验证后修改嵌入文档并继续沿用旧产物哈希。

V11-P1 使用独立的 [p1-final-development-evidence.json](../archive/records/host-v11/p1-final-development-evidence.json)，不覆盖 V11 原证据；安装目录部署也不包含在 P1 源码补丁验证中。
