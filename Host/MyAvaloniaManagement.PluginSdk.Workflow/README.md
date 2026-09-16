# MyAvaloniaManagement.PluginSdk.Workflow

> 用途：当前包消费说明；核对日期：2026-09-16。事实源为本包项目、公开类型或随包构建/模板内容。

该包冻结 Workflow Action 使用的窄 JSON Schema Profile，并提供 Host 与 Workflow Studio
共同使用的实例校验、引用路径、保守类型兼容和目录修订算法。

它不是通用 JSON Schema 引擎，也不包含工作流定义、执行器或 UI 模型。

字符串长度统一按 Unicode Rune 计算；`integer` 使用 Int64，`number` 使用 decimal。路径解析对静态 Schema
要求 required/minItems 保证，对运行时 JSON 使用相同对象和数组 segment 语法。Catalog 分别生成执行契约
与展示修订，使文案变化不再无条件使工作流失效。

V9 统一 NuGet 发布版本为 `3.4.1`，依赖 Core SDK `3.4.1`。本次没有公共 API 变更；
既有 `v1` API 文本与 `AssemblyVersion=1.0.0.0` 保留，FileVersion/InformationalVersion 反映当前包号。
