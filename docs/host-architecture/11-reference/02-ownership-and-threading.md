# 所有权、线程与提交点矩阵

> 核对日期：2026-09-28。用途：当前 Host 架构文档的查找与维护入口。

[返回文档总览](../README.md)

## 生命周期与所有者

| 对象或责任 | 所有者 | 结束条件 |
| --- | --- | --- |
| Host Provider | HostRuntime / RuntimeShutdown | 各关闭依赖满足后，插件 Provider 之后 |
| Plugin Provider | PluginProviderOwner | 调用排空、生命周期可安全停止 |
| Document 模型与 scoped 服务 | 所属 DocumentScopeManager | 真实关闭或候选回滚 |
| Document Adapter / View | Session 经统一释放链持有 | 撤回上下文和缓存后最终释放 |
| Tool 模型 | Host 或插件 Provider | 容器释放，隐藏不影响 |
| Tool Adapter / View | Session | Session 收尾 |
| DocumentControlRecycling | 当前 Host DI 容器 | 单文档关闭移除，容器最终释放 |
| PendingWorkspaceDocument | 当前准备用例 | 发布成功解除义务，否则 Dispose 回滚 |
| DocumentCloseApproval | 当前关闭范围调用者 | 提交、否决或范围变化时撤销 |
| Command 单页 Lease | 命令执行调用 | Task 完成且定向刷新后 |
| Workflow 调用 Scope | 当前 Handler 调用 | 实际 Handler 结束并异步释放 |
| Layout 写锁 | DockLayoutV3Store | 队列排空后随 Provider 收尾 |
| 安装运行共享租约 | 进程级安装会话交接 | ProcessExit |
| 重启交接会话 | Program | 最终退出结论确定 |
| 窗口事件订阅 | 对应 ViewModel / 窗口服务 | 窗口会话释放或构造失败 |

## 线程与调度

| 行为 | 执行边界 | 约束 |
| --- | --- | --- |
| Splash、主窗、控件、Dock 操作 | Avalonia UI 线程 | 不把 UI 对象传给后台布局写入 |
| Windows 初次模块构造 / Configure | StartupWorker 的 STA 入口 | 不保证所有插件 await 后 continuation 仍为 STA |
| 文档用例 | UI 协调入口加异步 IO / 插件初始化 | 跨 await 后重查目标和准入 |
| 布局捕获 | UI 后续调度回合 | 避开框架操作中的半完成树 |
| 布局文件队列 | 不捕获 UI 上下文的后台写入 | 串行提交，Flush 不打断原子写入 |
| 命令状态与模型事件 | 可能来自后台线程 | 展示层定向调度并核对代次 |
| Workflow Handler | 异步执行协议 | 不承诺 UI 线程，插件自行治理自身线程需求 |
| 用户 Dispose / 事件访问器 | 调用方执行，可能抛异常 | 关键 finally 继续清理，不在集合锁内执行 |

## 提交点矩阵

| 操作 | 中间成功 | 真正提交点 | 失败处理 |
| --- | --- | --- | --- |
| 插件注册 | Configure 或 Provider 构建成功 | Registry 接受所有者 | 丢弃声明并释放拒绝 Provider |
| Document 创建 | Scope / 初始化 / View 成功 | Session 发布成功 | Pending 义务回滚 |
| Document 保存 | 内容捕获成功 | 主文件写入完成 | 提交前失败；提交后警告分别处理 |
| 范围关闭 | 用户确认完成 | 最终复核且框架实际关闭 | 撤销许可，恢复入口 |
| 布局保存 | 快照捕获成功 | Store 文件提交 | 报告失败，可重试 |
| 插件启用设置 | UI 已选择 | 设置文件提交 | 不提前发布下次意图 |
| 插件安装 | Staged 或目录就位 | 启动确认后最终成功日志 | 后续独占恢复 |
| 自动重启 | 主窗关闭 | 最终许可且旧进程实际退出 | 不启动继任者 |

## 阅读原则

“持有引用”不自动意味着“有权 Dispose”；“取消已经请求”不等于“任务已经结束”；“快照可见”不等于“执行仍获准”。多个 Runtime 测试尤其需要验证资源没有通过静态字段或 App 全局状态互相串用。

出处：[运行时收尾](../01-runtime/02-shutdown-and-rollback.md)、[文档所有权](../04-documents/07-ownership-and-concurrency.md)、[布局调度](../05-docking-layout/04-save-queue-and-close.md)、[Workflow 预算](../07-workflow-actions/03-budgets-and-shutdown.md)、[安装恢复](../02-plugins/05-installation-and-recovery.md)。
