# V11-P1 Tool 分割故障与修复记录

> 后续确认（2026-09-20）：当前 Host 人工验收已由项目所有者实际手工使用后[确认通过](../host/manual-acceptance-20260920.md)；历次本机交付见[部署索引](../../../maintenance/local-deployment.md)。下文与相邻 JSON 保留原阶段事实，“未执行／待验收／未部署”均指当时状态。外部业务与发布事项由[现行待办](../../../roadmap/README.md)继续跟踪。

> 起始日期：2026-09-16。基线：`0525321`。本记录追加 V11 已部署版本之后的源码补丁事实；本补丁不覆盖安装目录。范围见 [P1 计划](../../plans/host-v11-p1-tool-split-fix-plan.md)。

## 已确认故障

用户在已部署单 EXE 中将 Tool 放到另一个 Tool 下方，Windows Application 事件 1026 记录未处理 `ArgumentOutOfRangeException`。调用链为 DockService.SplitToolDockable → HostDockFactory.SplitToDock → ToolDockCoordinator.FlattenTemporarySplit → InsertDockable。

旧逻辑把 Tool 局部上下分割归一化到全宽区域；移除临时容器时 Dock 同时清理孤立分隔条，即使 collapse=false 也会缩短父列表。再用移除前的索引插入会越界；未越界时仍可能丢失分隔条或移动位置。纯模型复现与实际崩溃堆栈一致，Document 分割不会进入该分支。

## 开发验证约定

先提交能失败的正式回归测试，再修改生产逻辑。专项与最终完整 verify 的实际结果在完成后登记。最终文档需要嵌入 Host，最终验证后只回填非嵌入 JSON，不沿用修改文档之前的产物身份。

真实桌面鼠标、多屏及外部插件业务需独立观察；本记录不将纯模型或 Headless 当作真实桌面通过。不运行 AIFLOW、Windows CI、seal 或发布门禁。

## P1-1 正式失败基线

执行 `dotnet test Host/MyAvaloniaManagement.PluginTests -c Release --no-restore -m:1 -warnaserror --filter FullyQualifiedName~P1 --logger "trx;LogFileName=p1-red.trx" --results-directory artifacts/host-v11-p1/red`：编译无警告，30 项均失败、无跳过。24 项覆盖 Tool 局部上下分割，6 项覆盖保留全宽停靠的容器替换；失败包括原索引越界、错误全宽迁移、分隔条丢失和引用不一致。此阶段故意保留生产缺陷，不将红灯记录为通过。

## P1-2 修复与职责

`HostDockFactory.SplitToDock` 保存原目标、保留基类协议，在有效分割且插入节点挂接后才通过 `IWorkspaceDockCallbacks.OnDockSplitCompleted` 报告；无父目标不伪造完成，非法操作继续由基类抛错。Session 只沿主窗口树确认目标及插入节点。Coordinator 按对象引用识别稳定全局目标，仅对它们和主窗口 DocumentDock 保留全宽兼容，其余使用局部分割。

临时容器检查仍挂接、仅剩一项且不是固定骨架，保存比例及 Active/Default 后取出子项，重查容器位置，先插入子项再移除容器。没有截断索引或吞掉结构异常。整理前提不成立时通过 `LAYOUT_TOOL_NORMALIZATION_SKIPPED` 及有限原因码诊断；诊断回调自身失败不介入业务控制流。保持原模型、View、Document Scope 的唯一所有权，不新增回滚框架。

测试同时发现锁定 Dock 同方向分割直接插入分隔条而未初始化其 Owner。Factory 在分割完成边界仅初始化当前父容器中尚无 Owner 的分隔条，避免重复初始化工作区或窗口。结构测试夹具也明确设置嵌套父级的 Active/Default，避免把夹具原有悬空引用归因于补丁。

## 专项结果与断言

以下均为 Release、`--no-restore -m:1 -warnaserror` 本地开发测试，原始 TRX 位于 `artifacts/host-v11-p1/`：

| 测试范围 | 结果 | 证明的边界 |
| --- | --- | --- |
| PluginTests：P1 或 RuntimeVerticalSplit | 39 通过，0 失败/跳过 | 上下方向、首中尾、父方向、嵌套、全局目标、同名节点不匹配、骨架缺失与诊断故障、原全宽回归 |
| Unit：WorkspaceSessionAndDockFactoryTests | 16 通过，0 失败/跳过 | 内部完成回调与原目标、挂接状态、批量范围、基类事件及异常契约 |
| UI：DockToolSplitUiTests 或 DockLayoutV3UiTests | 27 通过，0 失败/跳过 | V3 恢复后同组/跨组/整组、浮窗内部连续分割及回停、文档子组全宽兼容、关闭取消、全屏、保存失败 |

断言覆盖实际上下关系、分隔条 Owner、唯一挂接、邻居比例、折叠比例、Active/Default 引用及原实例；不同方向新建容器时保留 Dock 的 NaN 默认均分语义。同一 UI 调用内观察暂未挂接状态，布局文件仍为原有效字节；延后捕获及排空后重新读取严格 V3、启动新上下文，对比顺序、比例、分组、窗口和活动项。V3 分组 ID 与运行时稳定停靠别名分别验证，不混为一个身份。

## 最终验证与桌面边界

文档定稿后运行一次完整 `dotnet run --project tools/MyAvaloniaManagement.Gate -- verify`，实际运行 ID、结果、TRX 和 Host DLL/EXE 哈希归档到 [非嵌入 P1 证据](p1-final-development-evidence.json)。最终验证后只回填该 JSON，不改嵌入 MD 或构建输入。原 V11 证据继续代表其原始基线。

真实鼠标桌面验收待补：当前可用计算机工具不提供原生应用控制表面，无法实际投放截图中的停靠提示。本次 Headless 窗口协议测试不能宣称 Windows 鼠标投放、跨屏或外部 Bili 下载业务已通过。复现操作和记录项见 [专项指南](../../../maintenance/floating-layout-verification.md)。部署未执行，后续继续采用单文件、自包含方式；未运行 Windows CI、seal 或正式发布门禁。
