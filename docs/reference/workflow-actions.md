# Workflow Action 当前契约

> 用途：Host/SDK 维护者理解跨插件调用。状态：当前；核对日期：2026-09-16。事实源：[Host WorkflowActions](../../Host/MyAvaloniaManagement/Business/WorkflowActions)、[Workflow SDK](../../Host/MyAvaloniaManagement.PluginSdk.Workflow)、[内核测试](../../Host/MyAvaloniaManagement.Tests/WorkflowActionKernelTests.cs)。

## 角色与所有权

Provider 用 `AddWorkflowAction<THandler>` 注册不可变 Descriptor 与 scoped Handler；Consumer 显式使用 `UseWorkflowActionGateway`，从私有 Provider 取得 caller-bound Gateway。同一插件可以兼任两者。

Host 为 Consumer 绑定真实 manifest CallerId，目录过滤其自有 Action。手工构造自有 ActionId 仍返回 `WORKFLOW_ACTION_SELF_INVOCATION_FORBIDDEN`，发生在活跃计数、授权、Provider Scope 和 Handler 创建前。

Handler 异步调用链中再次进入 Gateway 返回 `WORKFLOW_ACTION_NESTED_INVOCATION_FORBIDDEN`；编排放在 Consumer 应用层或 Workflow Studio，不放在 Handler 中。顶层 Document/Application Service 调用不受该嵌套标记限制。

## Run 与调用

Consumer 每次编排创建一个 `IWorkflowActionRun`，结束时异步释放。Run 拥有取消、并发预算、授权缓存与 Contract revision 快照；Dispose 拒绝新调用、取消并等待在途工作。

请求只提交 ActionId 和 JSON 参数，不允许伪造 CallerId、OwnerId、RunId 或授权结果。Host 检查嵌套、自调用、目录 revision、关闭状态、并发、Schema 和授权，最后在目标插件私有 Provider 中创建独立 invocation scope 执行 Handler。

跨加载上下文仅使用 SDK/BCL/JSON，插件私有 DTO、Provider、Control 和服务定位器不能穿越边界。容量不足快速拒绝，不建立无界队列。关闭必须先取消并排空相关调用，无法证明安全排空时沿用 Host 的资源保留政策。

## 共享协议

`MyAvaloniaManagement.PluginSdk.Workflow` 是窄协议库，不是通用 JSON Schema 引擎，也不包含工作流定义、Runner 或 AI 客户端。

- 字符串长度按 Unicode Rune；integer 为 Int64，number 限于 decimal。
- object 声明 properties 并禁止 additionalProperties；array 有 items 与有限 maxItems。
- 静态引用用 required/minItems 保证路径存在，运行时采用相同 segment 语义。
- 输入、成功输出及 Descriptor 均按共享实现验证；敏感指针须唯一、规范且属于声明的输入 Schema。
- ContractRevision 表达执行契约，PresentationRevision 表达展示变化；Run 与授权指纹绑定前者。
- 结果、进度和诊断遵守预算与脱敏规则，不泄漏异常原文、Secret 或参数正文。

## 当前能力与后续候选

Host 当前具备注册、目录、Run、授权、资源治理、调用与关闭门控；Workflow Studio 的定义和 Runner 属于独立插件。Host 不因存在 Action 内核就自动具有每个业务插件的 Action，也没有承诺 AI 规划、删除源文件或持久化恢复引擎。

原计划中的“BiliDownloader 下载 Action G5”与后来“Provider/Consumer 双角色治理 G5”是不同任务，不能按编号互相关闭。后续工作按[待办名称](../roadmap/README.md)跟踪，设计历史见[原始方案](../archive/plans/ai-workflow-plugin-exploration.md)。

开发接入见[简明指南](../quick-start/workflow-action-development.md)；公开 API 及包版本见[API 维护](plugin-sdk-api-compatibility.md)与[版本基线](platform-baseline.md)。
