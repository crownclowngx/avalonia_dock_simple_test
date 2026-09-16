# MyAvaloniaManagement Plugin SDK UI

> 用途：包消费者了解模块、View 和 UI 端口。当前包版本 3.4.1；核对日期：2026-09-16。事实源为本程序集 public 类型、ApiCompatibility/v3 与项目精确依赖。

UI SDK 依赖同版本 Core，将 Avalonia、Fluent、Semi 和 Ursa 约束为 Host 验证的精确版本。它不公开 Dock 或 Host 实现；插件不能通过 UI SDK 携带另一份共享框架。

## 声明与所有权

模块只在 `Configure(IPluginRegistration)` 中注册私有服务和显式贡献。每个插件起始 Services 为空，模块返回后登记入口封闭；Host 校验归属与保留类型，再提交窗口端口、生命周期和 Scope 基础设施。

Document 模型为 scoped，Tool/Lifecycle 为插件 singleton；View 使用精确冻结工厂。普通和 keyed 注册不能影子覆盖 Host 端口。运行状态归 Host 内部服务，Registry 只保存不可变声明。

`IPluginWindowInteraction` 返回本地路径或操作结果，不暴露 Window、StorageProvider 或剪贴板实例；原生选择器返回后仍需检查取消，丢弃关闭期间的迟到结果。

全屏接口只有 `IDisposable? TryPresent(Control content)`。成功租约排他且幂等，Host 自动失效后再次释放无操作；插件不取得 Window、Dock、owner 或旧 TryRestore API。

## Workflow 与 Workbench Command

Provider 用可选 `IWorkflowActionRegistration` 注册 scoped Handler，Consumer 用 `UseWorkflowActionGateway` 请求 caller-bound Gateway。同一插件可以兼任；Host 过滤自有目录、拒绝自调用，并拒绝 Handler 异步链的嵌套调用。编排放在顶层应用层或 Studio Runner。

`IWorkbenchCommandRegistration` 兼容扩展原登记接口；插件声明 CommandDescriptor、目标 Document、菜单末端位置及快捷键。声明不保存 Target、Provider、Control 或回调；菜单、KeyBinding、上下文和执行投影由 Host 拥有。

## 图标贡献

`IPluginIconRegistration` 与 AddIcon 首次提供于 SDK 3.4.0；当前包及新模板使用 3.4.1。旧注册替身缺少能力时抛 NotSupportedException。

```csharp
var reference = registration.AddIcon("review", new VectorIconDefinition(
    "M1,1 H19 V19 H1 Z M4,4 V16 H16 V4 Z", 20, 20));
// 用于 DocumentDescriptor 的 iconPath。
```

Host 生成 `plugin:<真实 PluginId>/<本地名称>`，校验名称、归属和重复注册，随其他贡献原子提交。几何绘制失败回退默认图标，不阻止创建。

可选 `MyAvaloniaManagement.Icons 3.4.1` 提供公共矢量数据。资源对象不跨契约传递，复制路径与画布字段注册即可；它作为声明过的私有依赖部署，不能加入共享 SDK 闭包。

## 模板与兼容

```powershell
dotnet new install MyAvaloniaManagement.Plugin.Templates@3.4.1
dotnet new myavalonia-plugin -n ExamplePlugin --plugin-id myavalonia.plugin.example
```

模板精确使用 Core/UI、Icons、Build 3.4.1 与 Avalonia.Desktop 12.1.2，包含中性 Document、实例级 Command、测试和自包含文档，不默认占用快捷键。

UI v3 Shipped 为 45 条、Unshipped 为 79 条；历史 v2 文本保留。增量文件中的部分签名已随公开包交付，文本分类与 Host seal 是独立维护事项，不能为通过门禁删改旧承诺。
