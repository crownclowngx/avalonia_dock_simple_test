# 术语表：从名词跳回机制

> 核对日期：2026-09-28。用途：当前 Host 架构文档的查找与维护入口。

[返回文档总览](../README.md)

## 宿主与插件

| 术语 | 本项目中的含义 | 继续阅读 |
| --- | --- | --- |
| Host | 桌面工作台及公共编排能力 | [系统边界](../00-overview/01-system-boundary.md) |
| Runtime | 一次组合得到的对象图和生命周期边界 | [运行时](../01-runtime/README.md) |
| Composition Root | 建立服务、所有权和依赖关系的入口 | [启动组合](../01-runtime/01-startup-and-composition.md) |
| Provider | DI 容器，Host 与插件分别拥有 | [私有容器](../02-plugins/02-registration-and-containers.md) |
| ALC | 程序集加载上下文，决定依赖解析身份 | [发现加载](../02-plugins/01-discovery-and-loading.md) |
| Manifest | 磁盘运行清单，给出身份与精确入口 | [发现加载](../02-plugins/01-discovery-and-loading.md) |
| Registration | 冻结的贡献构造和所有权事实 | [注册](../02-plugins/02-registration-and-containers.md) |
| Registry | 已接受的真实插件声明集合 | [注册](../02-plugins/02-registration-and-containers.md) |
| Availability | 当前生命周期是否允许使用 | [可用性](../02-plugins/03-lifecycle-and-availability.md) |
| Enablement | 本次启动选择及下次启动意图 | [启用设置](../02-plugins/04-enablement.md) |

## 工作区与文档

| 术语 | 本项目中的含义 | 继续阅读 |
| --- | --- | --- |
| Catalog | 面向特定用途的只读目录 | [目录与 Session](../03-workspace/01-catalog-session-activation.md) |
| WorkspaceSession | 当前工作区的业务实例所有者 | [工作区](../03-workspace/README.md) |
| Document | 独立页面实例，拥有独立 Scope | [文档概念](../04-documents/01-concepts.md) |
| Tool | 复用 singleton 模型的辅助面板 | [工具显隐](../03-workspace/03-tools-and-visibility.md) |
| DocumentTypeId | 页面类型身份 | [页面与目标](../03-workspace/02-pages-targets-and-queries.md) |
| WorkspacePageId | 当前运行中的页面身份 | [页面与目标](../03-workspace/02-pages-targets-and-queries.md) |
| CreationIntentId | 新建同一类型时的具体创建意图 | [文档概念](../04-documents/01-concepts.md) |
| Activation | New 或 Restore 的互斥初始化请求 | [创建机制](../04-documents/03-create-and-activate.md) |
| Adapter | Host 将普通模型接入 Dock 的对象 | [Adapter](../05-docking-layout/01-adapters-and-views.md) |
| Scope | 一组实例的 DI 寿命边界 | [文档所有权](../04-documents/07-ownership-and-concurrency.md) |
| PendingWorkspaceDocument | 发布前的局部回滚义务 | [发布](../03-workspace/01-catalog-session-activation.md) |
| Envelope | Host 身份和插件内容组成的文件信封 | [打开恢复](../04-documents/04-open-and-restore.md) |
| Revision | 保存快照对应的插件内容修订 | [保存](../04-documents/05-save-and-revision.md) |
| RequiresSave | Host 要求另存的运行事实 | [文档概念](../04-documents/01-concepts.md) |
| CloseApproval | 可撤销的一次性关闭许可 | [关闭](../04-documents/06-close-and-release.md) |
| LayoutSnapshot | 可保存的纯工具布局和文档占位 | [布局快照](../05-docking-layout/03-snapshot-store-and-restore.md) |

## 执行和治理

| 术语 | 本项目中的含义 | 继续阅读 |
| --- | --- | --- |
| Command | 用户工作台操作的稳定身份 | [命令模块](../06-commands/README.md) |
| Context / Capture | 纯值上下文 / 携带当前实例的内部捕获 | [命令执行](../06-commands/02-context-and-execution.md) |
| ExpectedTarget | 一次调用预期的页面与上下文代次 | [命令执行](../06-commands/02-context-and-execution.md) |
| Lease | 限定责任或有效期的凭据，需按具体类型理解 | [所有权矩阵](02-ownership-and-threading.md) |
| Drain | 等待真实在途操作结束 | [退出回滚](../01-runtime/02-shutdown-and-rollback.md) |
| Workflow Action | 跨插件 JSON 能力 | [Workflow](../07-workflow-actions/README.md) |
| Run | 一组 Workflow 调用的治理范围 | [目录与 Run](../07-workflow-actions/01-contracts-and-catalog.md) |
| caller-bound Gateway | 固定可信 CallerId 的调用门面 | [调用机制](../07-workflow-actions/02-invocation-and-authorization.md) |
| Staged | 已提交但尚未应用的安装待办 | [安装](../02-plugins/05-installation-and-recovery.md) |
| AwaitingStartup | 文件就位，等待指定进程试启动确认 | [安装](../02-plugins/05-installation-and-recovery.md) |
| Handoff | 旧进程向助手交付重启许可 | [重启](../01-runtime/03-restart-handoff.md) |

同名 Revision 在内容保存、命令上下文、布局队列和 Workflow 目录中属于不同事实，不能跨模块比较。
