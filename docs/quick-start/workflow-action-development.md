# Workflow Action 开发说明

> 用途：插件 Provider/Consumer 接入。状态：当前；核对日期：2026-09-16。事实源：[当前调用契约](../reference/workflow-actions.md)、Workflow SDK 和生成模板文档。

当前新项目使用同版本 Core/UI 与可选 Workflow SDK `3.4.1`。共享 Schema 协议首次提供于 SDK 3.2.0；它是历史能力下限，新模板 manifest 最低为 3.4.1，不应手动降级。

## 注册与调用

- Provider 用 `AddWorkflowAction<THandler>` 注册窄 Descriptor 与 scoped Handler；输入输出只使用 SDK/BCL/JSON，不公开插件私有 DTO、Provider 或任意脚本入口。
- Consumer 调用 `UseWorkflowActionGateway`，以构造注入取得 caller-bound Gateway；每次编排创建一个 Run，并在结束时异步释放。
- 同一插件可以兼任两种角色；目录隐藏自有 Action，Host 仍会拒绝手工构造的自调用。
- Handler 内不得再发起 Workflow Action 调用。跨插件编排放在顶层 Application Service、Document 或 Studio Runner。
- 请求不能提交 CallerId、OwnerId、RunId 或授权结果；真实身份和授权由 Host 管理。

## 共享 Schema 与版本

`MyAvaloniaManagement.PluginSdk.Workflow` 的 Schema/API 保持 v1：字符串长度按 Rune，integer 为 Int64，number 为 decimal；object 必须声明 properties 且禁止额外字段，array 必须声明 items 与有限 maxItems。

先用 `WorkflowSchemaValidator.ValidateDescriptor` 验证 Descriptor；为 enum、范围、required、数组边界和极端 Unicode/decimal 输入建立测试。敏感指针必须唯一、规范且指向声明属性。Host 注册与调用使用同一实现，插件不得依赖校验器 Message 或异常原文。

Action ID、风险、确认策略、敏感指针和去除 description 的 Schema 属于 ContractRevision；展示名称、说明及 Schema description 属于 PresentationRevision。契约变化后重新验证编排，不能仅修改展示 revision 绕过。

## 交付与验证

插件 ZIP 不携带 Core、UI 或 Workflow SDK DLL，由 Host 默认加载上下文提供。Standalone 可使用有限 Fake Gateway 预览，但真实目录、授权、独立 Scope、取消与关闭必须在 Host 验证。

除正常调用外，至少验证自调用拒绝、Handler 嵌套拒绝、输入/输出不合法、取消及 Run 释放。完整职责和失败边界见[当前契约](../reference/workflow-actions.md)；未来业务接入见[待办](../roadmap/README.md)。
