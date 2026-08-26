# Workflow Action 开发说明（SDK 3.2）

Provider 继续通过 `IWorkflowActionRegistration.AddWorkflowAction<THandler>` 注册窄 Descriptor 和 scoped
Handler。输入输出只使用冻结 JSON Schema Profile；不要公开插件私有 DTO、Provider、`IServiceProvider` 或
任意脚本入口。

G3.1 后，Schema 的权威实现位于 `MyAvaloniaManagement.PluginSdk.Workflow 1.0.0`：

- 字符串 `minLength`/`maxLength` 按 Unicode Rune；
- `integer` 必须是 Int64，`number` 必须能表示为 decimal；
- object 必须声明 properties 且 `additionalProperties: false`；
- array 必须声明 items 与有限 maxItems；
- Descriptor 敏感指针必须唯一、规范并指向输入 Schema 已声明属性。

开发时先用 `WorkflowSchemaValidator.ValidateDescriptor` 验证 Descriptor，再为 enum、范围、required、数组
边界和极端 Unicode/decimal 输入建立单元测试。Host 注册和调用边界会再次使用同一实现，并将问题映射为
稳定失败码；插件不得依赖校验器 Message 文案或原始异常。

Consumer 如果使用 Workflow SDK，manifest 下限必须为 `3.2.0`。正式插件包不得携带 Core、UI 或 Workflow
SDK DLL，这些程序集由候选 Host 默认 ALC 提供。Action ID、风险、确认策略、敏感指针与去除 description
后的 Schema 都属于 Contract revision；名称、说明和 Schema description 只属于 Presentation revision。
