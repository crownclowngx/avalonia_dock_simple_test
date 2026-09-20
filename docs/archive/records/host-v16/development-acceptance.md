# V16 Document 浮窗关闭与命令面板新建：开发记录

> 日期：2026-09-20。范围：本地开发实现、自动化与文档同步。原生桌面待验收；未部署、未发布。
> 最终完整 verify 的状态、输入 HEAD/tree、命令、退出码、TRX 数量和产物摘要记录在同目录 `final-development-evidence.json`。本文在该次运行前冻结，不能单独当作最终门禁通过证明。
> 方案：[V16 实施方案](../../../roadmap/host-v16-document-floating-close-and-creation-target-plan.md)；矩阵、命令及 C/N 到实际测试的映射：[专用开发验证](../../../maintenance/host-v16-document-window-verification.md)。

## 1. 输入与复现

初始源码为 `61ff50cf077b39b6f41debd25a4ec255e60bb7d4`，在 `codex/host-v16-document-windows` 分支实施。先提交既有 V16 文档，再提交两项正式失败测试；修改前证据见 `p0-red-evidence.json`，原始 TRX 位于 `artifacts/v16/tests/red/v16-p0-red.trx`。

| 入口 | 修改前观察 | 修改后保护 |
| --- | --- | --- |
| 唯一 Document 的真实标签 × | 文档移除后窗口仍 `IsVisible`，形成模型已解除的空壳 | 在内容仍附着时接入原生整窗确认；取消不拆除，获准后才由 Dock 收尾 |
| 浮窗 Ctrl+Shift+P、选择功能并 Enter | 新文档 Owner 落到主文档组，未进入来源浮窗 | 面板显示前捕获源根和组，显式传递到最终发布 |

P0 为 Headless 的真实 XAML/输入链证据，不是 Windows 桌面黑框截图。已检查固定 Dock 的 Document 标签按钮只有关闭命令，没有 ToolChrome 的双入口联动，未添加 Document 点击吞噬逻辑。

Dock 依旧使用 `patches/dock-area-fill/baseline.json` 指定的 commit `cc08602d02fde1b85067cec064da29f34785e505`，包 `12.1.0.7-area.5`。本轮不修改 Dock/Avalonia 补丁、NuGet 缓存、依赖版本、Gate 实现或阈值；最终 verify 按原图准备并核对固定补丁，摘要归入最终证据。

## 2. 实现与设计取舍

关闭由 `HostDockFactory` 在最后一项仍存在时转接 `HostFloatingWindow.Close`。已有 `DockWindowCloseCoordinator` / `DocumentCloseCoordinator` 完成范围确认、保存修订检查及命令排空；能力和 Dock 可取消事件在原生关闭前预检，通过的一次性许可仅供最终清理消费。拒绝、异常、移除或成功均撤销许可。单页等待期间相邻页消失，只接续精确的一页许可，不能扩大为多页或应用退出。

新建使用普通不可变 `DocumentCreationTarget`，借用来源根及优先文档组。`DocumentCreationTargetResolver` 只做选组：原组仍属于原根时优先原组，否则同源有效组，再主窗最近/默认组。接收能力禁止或窗口已关闭/关闭中不会被选中。按根隔离的弱最近组记录不拥有布局；标签选择和组内焦点共同更新记录，Tool 焦点不覆盖文档组。

面板会话将目标复制给一次调用，`WorkspacePaletteActions → DocumentPersistenceCoordinator → WorkspaceSession` 沿原串行门、初始化、Scope 和持久化登记链执行。Session 在发布前重验，仅向最终组插入，失败撤销部分写入并释放候选。重复发布检查覆盖整个工作区。未传目标的原入口仍使用主默认组，不暗中采用最近浮窗。

创建结果携带页面身份；面板结束后，焦点恢复还校验原会话号、窗口激活序号、页面仍存活且为当前页。用户已切页、关页、切窗或打开新面板时，旧回调不抢回焦点。已有页面搜索仍定位原实例和承载窗口。

SOLID 为首要约束：输入来源、布局规则、发布所有权、关闭协调和框架适配各自独立。只复用现有端口、具体协作者和短期许可，不增加每窗 Runtime、通用事件总线、策略注册框架或插件 API。核心注释使用中文，说明先确认后拆除、借用身份、失效回退和迟到回调的设计原因。

## 3. 阶段验证

以下为独立运行的实际结果，不累加冒充一次完整测试。全部使用 Release；最终完整项目及契约/包验收由文档冻结后的 verify 覆盖，最终数字以 JSON 为准。

| 阶段 | 命令/范围 | 结果与原始产物 |
| --- | --- | --- |
| P0 | `dotnet test Host/MyAvaloniaManagement.UiTests -c Release -m:1 --filter FullyQualifiedName~DocumentWindowV16UiTests` | 2 项，0 通过、2 预期失败、0 跳过；`artifacts/v16/tests/red/v16-p0-red.trx` |
| P1 | V16 C 关闭专项 + `DockToolWindowCloseUiTests` | 48 通过、0 失败/跳过；`artifacts/v16/tests/p1/v16-p1-close.trx` |
| P1 | `DocumentCloseTests` + `DockWindowCloseTests` | 20 通过、0 失败/跳过；`artifacts/v16/tests/p1/v16-p1-unit.trx` |
| P2 | `DocumentWindowV16UiTests` + `CognitiveUxV8UiTests` + `DockToolWindowCloseUiTests` | 79 通过、0 失败/跳过；`artifacts/v16/tests/p2/v16-p2-integrated-final.trx` |
| P2 | Host Unit 全量 | 608 通过、0 失败/跳过；`artifacts/v16/tests/unit/v16-host-unit.trx` |
| P3 | Gate 工具自测全量 | 68 通过、0 失败/跳过；`artifacts/v16/tests/gate-tool/v16-gate-tool.trx` |
| P3/P4 | `dotnet run --project tools/MyAvaloniaManagement.Gate -- verify` | 最终结果、阶段日志及各测试项目 TRX 见 `final-development-evidence.json` |

调试时保留了失败反馈：主窗多分组测试曾在首次布局未完成时发送键盘输入，改为等待真实标签就绪；随后新增真实鼠标测试，确证仅监听 ActiveDockable 会漏掉另一组已选中页的点击，补入 FocusedDockable 通知后通过。N10 关页分支最初在释放后读取已清空的 PreparedView，改为提前保留测试观察引用，并明确断言 Adapter 清空、View/Model 各释放一次。早期组合运行 77 通过/1 失败保留于 `artifacts/v16/tests/p2/v16-p2-integrated.trx`，不作为最终通过证据。

C01–C11 和 N01–N11 的新增 UI 方法、目标规则和回滚单元测试、真实插件夹具的映射列在专用验证第 3 节。C12/N12 的转移、重置、应用退出、重启、持久化、旧入口和插件生命周期由既有回归及最终完整 verify 共同覆盖。Headless 的窗口对象 Closed、模型引用解除、Scope/View 释放均为显式断言，不以 GC 时机或截图颜色充当资源证明。

## 4. 原生桌面验收

本次环境未提供可操作的原生桌面自动化表面，未以已有安装版、Headless 截图或源码推断替代桌面验收。以下逐项保持“未执行”，不是通过，也不阻止如实交付已完成的本地代码与自动化证据。

| 编号 | 状态 | 后续范围 |
| --- | --- | --- |
| M01 | 未执行 | 开发产物的标签 ×、右键、原生标题栏、Alt+F4；确认无黑框且主窗可继续使用 |
| M02 | 未执行 | 脏文档保存/放弃/取消/失败，对话框归属和继续编辑 |
| M03 | 未执行 | 主窗、两个浮窗和多文档组的真实快捷键新建 |
| M04 | 未执行 | 慢初始化期间切窗与布局变化，焦点和回退 |
| M05 | 未执行 | Tool 焦点、纯 Tool 回退、混合窗口关闭和恢复 |
| M06 | 未执行 | 同窗新增、逐页关闭、反复回停/重置 |
| M07 | 未执行 | 主窗退出与重启的取消和成功，含未保存内容 |
| M08 | 未执行 | 浅深主题、DPI、多屏激活与焦点 |

这是本地交互待验收，不是 Windows CI 或发布 Smoke。本轮没有调用这些发布流程。Windows 上运行本地 dotnet 单元/Headless 测试不等同于 Windows CI。

## 5. 文档、兼容与证据边界

已同步浮窗指南、工作区搜索、Workbench Command 契约、Document 持久化、Host 架构、设计取舍、兼容约束、总导航/待办/验证入口，以及 V16 方案和专用测试文档。Layout V3 正文已核对，其纯文档浮窗不持久化、工具布局恢复边界未变，不修改 schema。新增/修改 Markdown 的本地相对链接和 `git diff --check` 在最终提交前检查。

SDK public API、manifest、依赖/产品版本、文档格式与 Layout V3 未改变。纯 Document 浮窗继续只在本次运行存在；主 Runtime / Provider / Session 所有权不变。未使用 AIFLOW；未修改 CI；未运行 seal、覆盖率发布门禁、Windows Smoke 或发布重复性验证。verify 内的 MyPlugTest 打包和 ZIP 验收属于既有本地开发检查，不表示对外发布或安装目录部署。

最后一批帮助 Markdown 先提交，再运行最终完整 verify；随后只提交不嵌入应用的 JSON 证据。JSON 记载输入提交/tree、固定补丁及摘要、实际运行时间和日志、测试总数及跳过、Git 差异检查和 M01–M08 状态。若最终 verify 失败，保留失败结果，修复后重新运行，不把阶段专项拼成最终门禁通过。
