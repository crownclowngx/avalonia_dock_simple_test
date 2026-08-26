# MyAvaloniaManagement.PluginSdk.Workflow

该包冻结 Workflow Action 使用的窄 JSON Schema Profile，并提供 Host 与 Workflow Studio
共同使用的实例校验、引用路径、保守类型兼容和目录修订算法。

它不是通用 JSON Schema 引擎，也不包含工作流定义、执行器或 UI 模型。

字符串长度统一按 Unicode Rune 计算；`integer` 使用 Int64，`number` 使用 decimal。路径解析对静态 Schema
要求 required/minItems 保证，对运行时 JSON 使用相同对象和数组 segment 语法。Catalog 分别生成执行契约
与展示修订，使文案变化不再无条件使工作流失效。
