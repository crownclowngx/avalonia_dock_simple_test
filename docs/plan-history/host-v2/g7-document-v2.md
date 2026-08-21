# Managed Plugin V2 G7：Document V2

> 状态：已完成
>
> 实施日期：2026-08-21
>
> 性质：开发阶段非发布整改；未运行 AIFLOW、Windows CI、Windows Smoke、ReleaseAcceptance、
> 发布包门禁、上传、标签或任何发布操作。

## 1. 结果与阶段边界

G7 在 G6 Host Dock Adapter 上建立了唯一 Document V2 创建、恢复、保存、关闭和 Scope 释放链。
Host internal 工厂异步等待插件初始化，`DocumentActivationContext` 统一承载默认新建、合法 Creation
Intent 和 `RestoredContent`。未发布模型的初始化、Adapter、Presentation、View、状态登记与发布失败
全部原子回滚，不留下 Dock 标签或持久化登记。

Plugin SDK public API、2.0.0 版本和 v2 API 文本基线没有改变。四个业务插件仍保留到 G9–G12 迁移；
其旧源码测试在各自测试组合根使用测试专用 Scope 夹具，不会把 `IDocumentScopeFactory` 注册回生产
Host。layout-v1 与生命周期编排未修改，仍属于 G8。

## 2. SOLID 与设计取舍

Serializer 只解释线格式；Coordinator 只编排用例；Save Service 只处理捕获、主文件提交与提交后警告；
State Store 只保存 Host 事实；Adapter 只投影 Dock/View 并拥有 Lease；Close Coordinator 只处理用户
决策与一次性重入。依赖通过窄构造注入连接，所有权由 Scope Lease 显式表达。

采用的模式只有 Factory、Adapter、Coordinator 和 Scope Lease。没有引入仓储框架、通用状态机、
策略注册双轨、动态代理、反射恢复、事件溯源或 reader chain。中文 XML 注释集中说明所有权、异步边界、
提交点、失败回滚与设计取舍，没有为显而易见赋值堆砌逐行注释。

## 3. 唯一 V2 线格式

根对象固定六字段：`schemaVersion`、`pluginId`、`documentTypeId`、`title`、`savedAtUtc`、`content`；
`content` 固定为 `schemaVersion` 与原生 JSON `payload`。payload 支持全部 JSON 值并通过
`JsonElement.WriteTo` 嵌套写入，读取结果由 `DocumentContent` 克隆。

严格 reader 拒绝未知、重复、缺失、大小写错误、类型错误、V1、注释、尾逗号、非规范 ID、非 UTC、
空白标题、空文件、超过 8 MiB 和深度超过 8。生产路径已删除 V1 信封、字符串快照、Legacy 保存状态、
Document 加载异常、Newtonsoft Document 转换、Legacy Scope 工厂和双生命周期注册。

## 4. 提交点、恢复与关闭

原子主文件写入是唯一提交点。提交前捕获或写入失败不会改变 Host 路径、标题、恢复标记或插件脏状态；
提交后才更新 Host 事实、清理恢复保护并调用 `AcceptChanges`。提交后的回调或备份失败只产生固定的
“已保存但有警告”，不能把成功主文件报告为失败。

并发操作共享串行门。打开按规范路径查重、长度、严格解析、Registry、未发布初始化、状态提交、发布
执行。恢复备份必须先完整初始化再询问；拒绝立即释放。恢复副本由 `RequiresSave` 强制参与关闭确认和
另存，禁止覆盖损坏原件或备份。关闭取消不触发令牌，最终关闭按 View、ClosingToken、模型与依赖顺序
幂等释放；交互、插件回调和重入异常均保持标签打开并使用脱敏提示。

详细当前设计见 [Document V2 持久化设计](../../design/document-persistence-v2-design.md)。

## 5. 失败矩阵

| 失败点 | 发布/磁盘结果 | 所有权结果 |
| --- | --- | --- |
| 初始化、Presentation 或 View | 不发布 | 取消令牌并释放暂存 Scope |
| Creation Intent 非法 | 不创建模型、不发布 | 无 Scope 或立即回滚 |
| 严格信封/所有者/能力不匹配 | 不发布、不写输入 | 未创建或释放暂存 Scope |
| 主文件写入失败 | 不提交路径/标题/脏状态 | Document 保持打开 |
| `AcceptChanges`/备份失败 | 主文件已保存，显示警告 | 可继续关闭 |
| 恢复拒绝 | 不修改原件/备份 | 释放备份暂存 Scope |
| 关闭取消/保存取消 | 标签保持打开 | ClosingToken 不取消 |
| 最终关闭/Runtime 退出 | Dock 所有权结束 | View、令牌、模型、依赖依次释放 |

## 6. 实际测试证据

本轮实际执行 `scripts/Test-DocumentV2.ps1 -Configuration Release -NoRestore`：Unit **59**、Plugin
**8**、Headless UI **16**，合计 **83/83**。摘要位于
`artifacts/test-results/DocumentV2/summary.json`，明确记录 `windowsCi=false`、
`windowsSmoke=false`、`releaseGate=false`。

Host 全量覆盖率门禁实际为 Unit **171**、UI **44**、Plugin **159**，合计 **374/374**；Host 行覆盖率
**82.22%**、分支覆盖率 **67.22%**。G7 关键文件行覆盖率为：Serializer **100%**、Persistence
Coordinator **94.51%**、Save Service **97.40%**、Close Coordinator **97.62%**、State Store
**100%**；既有 Host Dock Adapter Factory **100%**、Document Adapter **96.55%**、Scope Manager
**90.91%**，整体与既有关键文件阈值均未降低。

完整非发布验收还实际通过：解决方案 locked restore、Release `-warnaserror` 全解决方案构建（0 警告、
0 错误）、Plugin SDK 单元 **32/32**、Core/UI API v2 兼容门禁、SDK Core/UI 隔离包正反向消费门禁，
以及 BiliDownloader **720/720**、DaTangAccountingHelpPlug **64/64**、MySmallTools **183/183**，
三个业务插件合计 **967/967**。文档核心门禁与完整门禁均通过；完整门禁核验 45 份文档、273 个本地
链接、84 个脚本路径和 43 个项目路径。

专项还覆盖 V1 拒绝、精确字段、任意 JSON payload、克隆生命周期、8 MiB/深度边界、异步初始化、
Creation Intent、初始化/View 回滚、所有者与能力核对、批量隔离、并发查重、原子保存、提交后警告、
恢复另存保护、关闭重入、回调异常、Runtime 残余 Scope 及生产结构禁用符号。

## 7. 回滚

回滚单位是完整 G7 代码、测试、脚本和文档，目标是 G6 无持久化 Adapter 基线。不得选择性恢复
Document V1 reader、Legacy 保存接口或双 Scope 工厂，也不得读取、迁移、覆盖、删除或降级写回任何
用户 V1/V2 `.mamdoc` 文件。
