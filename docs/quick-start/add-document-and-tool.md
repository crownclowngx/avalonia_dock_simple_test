# 添加多个 Document、Tool 和独立预览工作台

本篇从模板生成的 `ExamplePlugin` 出发，说明如何登记多个贡献，以及怎样在 Standalone 小窗口中查看
它们。核心原则只有一个：`ExamplePluginModule.Configure()` 是唯一注册事实源，Standalone 不再维护另一
份页面类型清单。

## 1. 先区分“多种”和“多实例”

- 多种 Document：订单列表、订单编辑、设置等，每种调用一次 `AddDocument`。
- 同一种 Document 多实例：同一种订单编辑页可以打开多个，每次创建独立 DI Scope。
- 多种 Tool：日志、属性、任务队列等，每种调用一次 `AddTool`。
- 同一种 Tool 重复显示：模型仍然是插件级 singleton，只隐藏和恢复，不重建业务状态。

## 2. 定义稳定身份

更新 `src/ExamplePlugin.Plugin/Constants/PluginIds.cs`：

```csharp
using MyAvaloniaManagement.PluginSdk;

namespace ExamplePlugin.Constants;

public static class PluginIds
{
    public static readonly PluginId Plugin =
        new("myavalonia.plugin.example");

    public static readonly DocumentTypeId OrderListDocument =
        new("myavalonia.plugin.example.document.order-list");

    public static readonly DocumentTypeId OrderEditorDocument =
        new("myavalonia.plugin.example.document.order-editor");

    public static readonly DocumentTypeId SettingsDocument =
        new("myavalonia.plugin.example.document.settings");

    public static readonly ToolTypeId LogTool =
        new("myavalonia.plugin.example.tool.log");

    public static readonly ToolTypeId PropertiesTool =
        new("myavalonia.plugin.example.tool.properties");
}
```

类名、文件夹和显示文字可以改变，已发布的稳定 ID 不应改变。

## 3. 每个功能放在自己的 Feature 目录

推荐按功能垂直组织，而不是建立全局 `Views/` 与 `ViewModels/` 大目录：

```text
src/ExamplePlugin.Plugin/
├─ Constants/PluginIds.cs
├─ Features/
│  ├─ OrderList/
│  │  ├─ OrderListDocument.cs
│  │  ├─ OrderListView.axaml
│  │  └─ OrderListView.axaml.cs
│  ├─ OrderEditor/
│  ├─ Settings/
│  ├─ Log/
│  └─ Properties/
└─ Plugin/
   ├─ ExamplePluginModule.cs
   └─ ExamplePluginServices.cs
```

Document Model 实现 `IPluginDocument`；需要 Host 保存和恢复业务内容时实现
`IPersistablePluginDocument`。View 必须继承 `Control` 并具有 public 无参构造。Tool Model 不需要实现
Dock 类型或特定接口。

## 4. 在唯一 Module 中登记全部贡献

```csharp
public void Configure(IPluginRegistration registration)
{
    ArgumentNullException.ThrowIfNull(registration);

    registration.Services.AddExamplePluginServices();

    registration.AddDocument<OrderListDocument, OrderListView>(
        new DocumentDescriptor(
            PluginIds.OrderListDocument,
            "订单列表",
            "查询和管理订单",
            "订单"));

    registration.AddPersistableDocument<OrderEditorDocument, OrderEditorView>(
        new DocumentDescriptor(
            PluginIds.OrderEditorDocument,
            "订单编辑",
            "创建或编辑订单",
            "订单"));

    registration.AddDocument<SettingsDocument, SettingsView>(
        new DocumentDescriptor(
            PluginIds.SettingsDocument,
            "插件设置",
            "配置插件选项",
            "设置"));

    registration.AddTool<LogToolViewModel, LogToolView>(
        new ToolDescriptor(
            PluginIds.LogTool,
            "运行日志",
            "显示插件运行日志",
            ToolDockSide.Bottom,
            ToolCloseBehavior.Hide));

    registration.AddTool<PropertiesToolViewModel, PropertiesToolView>(
        new ToolDescriptor(
            PluginIds.PropertiesTool,
            "属性",
            "显示当前对象属性",
            ToolDockSide.Right,
            ToolCloseBehavior.Hide));
}
```

注意：

- `AddDocument` 会让真实 Host 最终登记 scoped Model；
- `AddTool` 会让真实 Host 最终登记 singleton Model；
- 不要再对贡献根调用 `registration.Services.AddScoped/AddSingleton`；
- 插件内部 Repository、Client、Codec 等普通服务仍放入 `registration.Services`；
- View 不单独登记，Model、View 和 Descriptor 在同一个泛型调用中冻结。

## 5. 当前模板 Standalone 的限制

Templates `1.0.4` 的 `MainWindow` 直接实例化一个 `MainDocument`，XAML 也直接放置一个 `MainView`。新增
注册不会自动出现在这个窗口，因为 Standalone 没有执行 Module，也没有贡献目录。

这对第一个页面很方便，但多贡献时不应继续写：

```csharp
new OrderListDocument();
new OrderEditorDocument();
new LogToolViewModel();
```

这种写法绕过 DI、Document Scope、Tool singleton 和 Module 注册，很容易让 Standalone 与真实 Host
表现不同。

## 6. 推荐的极简预览工作台

Standalone 应扩展为贡献浏览器，而不是缩小版完整 Host：

```text
┌──────────────────────────────────────────────────────────┐
│ ExamplePlugin Standalone                                 │
├──────────────┬──────────────────────────┬────────────────┤
│ Documents    │ Open Documents           │ Tools          │
│              │                          │                │
│ 订单列表     │ [订单列表] [订单编辑 #1] │ 运行日志       │
│ 订单编辑     │ [订单编辑 #2]            │ 属性           │
│ 设置         │                          │                │
│              │      ContentControl      │ ContentControl │
│ [打开新实例] │                          │                │
└──────────────┴──────────────────────────┴────────────────┘
```

建议文件：

```text
src/ExamplePlugin.Standalone/Preview/
├─ PreviewPluginRegistration.cs
├─ PreviewDocumentRegistration.cs
├─ PreviewToolRegistration.cs
├─ PreviewDocumentLifetime.cs
├─ PreviewWorkspaceViewModel.cs
└─ PreviewHostPorts.cs
```

它只负责以下闭环：

1. 建立一个空 `ServiceCollection`；
2. 建立 preview `IPluginRegistration`；
3. 调用 `new ExamplePluginModule().Configure(registration)` 一次；
4. 追加 Standalone 专用 `IDocumentLifetime` 和必要 Host Port Stub；
5. 构建 preview `ServiceProvider`；
6. 从捕获的 Descriptor/工厂创建 Document 或 Tool View；
7. 关闭 Document 标签时取消 lifetime 并释放对应 Scope。

如果 Standalone 要构建真实 DI Provider，需要只在 Standalone 项目添加
`Microsoft.Extensions.DependencyInjection`；它不会进入插件 ZIP。

## 7. PreviewRegistration 如何收集泛型注册

下面是职责骨架，不是新的 SDK 契约：

```csharp
internal sealed class PreviewPluginRegistration : IPluginRegistration
{
    public PreviewPluginRegistration(PluginId pluginId)
    {
        PluginId = pluginId;
    }

    public PluginId PluginId { get; }
    public IServiceCollection Services { get; } = new ServiceCollection();
    public List<PreviewDocumentRegistration> Documents { get; } = [];
    public List<PreviewToolRegistration> Tools { get; } = [];

    public void AddDocument<TDocument, TView>(DocumentDescriptor descriptor)
        where TDocument : class, IPluginDocument
        where TView : Control, new()
    {
        Services.AddScoped<TDocument>();
        Documents.Add(new(
            descriptor,
            provider => provider.GetRequiredService<TDocument>(),
            () => new TView()));
    }

    public void AddTool<TTool, TView>(ToolDescriptor descriptor)
        where TTool : class
        where TView : Control, new()
    {
        Services.AddSingleton<TTool>();
        Tools.Add(new(
            descriptor,
            provider => provider.GetRequiredService<TTool>(),
            () => new TView()));
    }

    // AddPersistableDocument 与 AddDocument 使用同一 preview 生命周期；
    // UseLifecycle 可以显式记录为“不在 Standalone 自动启动”。
}
```

这里的 `AddScoped/AddSingleton` 只属于 Standalone 的 preview 容器。真实 Host 仍使用自己的注册事务和
所有权校验，插件 Module 不应手工登记贡献根。

`PreviewDocumentRegistration` 和 `PreviewToolRegistration` 只需保存 Descriptor、Model 工厂和 View 工厂。
不要保存已经创建的 scoped Document Model。

## 8. 打开和关闭 Document

点击左侧 Document 时：

```csharp
var scope = rootProvider.CreateAsyncScope();
var model = registration.CreateModel(scope.ServiceProvider);

await model.InitializeAsync(
    new NewDocumentActivation(registration.Descriptor.DisplayName),
    cancellationToken);

var view = registration.CreateView();
view.DataContext = model;
```

然后把 `view + model + scope` 放入一个 OpenDocument 项。关闭 Tab 时按顺序：

1. 取消该 Scope 中 `PreviewDocumentLifetime` 的关闭令牌；
2. 从 UI 移除 View；
3. 异步释放 Scope。

再次点击同一种 Document 必须创建新 Scope，这样才能发现意外 singleton 状态泄漏。

## 9. 显示 Tool

Tool 从根 Provider 获取 Model：

```csharp
var model = registration.CreateModel(rootProvider);
var view = registration.CreateView();
view.DataContext = model;
```

工作台可以按 `ToolDockSide` 把 Tool 粗略放在左、右、上、下区域，但这只是布局提示，不需要引入 Dock
库。`ToolCloseBehavior.Hide` 在 Standalone 中只切换可见性；再次显示时必须复用同一个 Model。

## 10. Host Port Stub

如果 Document 构造函数依赖 `IDocumentLifetime`、窗口交互或全屏 Host Port，Standalone 必须显式提供
preview 实现：

- `IDocumentLifetime`：每个 Document Scope 一份，可在关闭 Tab 时取消；
- 文件选择/窗口交互：可以使用 Standalone 自己的 Avalonia Window 实现；
- 全屏或只属于真实 Host 的能力：返回“不支持”并在工作台显示说明；
- 不要为了让预览运行而把依赖改成可空，也不要绕过 DI 直接 `new` Model。

Stub 只能存在于 Standalone，不得进入 Plugin 项目或插件 ZIP。

## 11. Standalone 与真实 Host 的验收边界

Standalone 可以验证：

- 多个 View、绑定、命令和插件私有对象图；
- 同一种 Document 多 Scope 隔离；
- Tool Model singleton；
- Document 初始化、关闭取消和 Scope 释放；
- 插件内部消息和多个贡献之间的协作。

它不能证明：

- manifest/入口和 AssemblyLoadContext 正确；
- 共享程序集没有误打包；
- 真实 Dock、布局保存恢复和 Tool 关闭策略正确；
- Host 文件信封、Host Port、生命周期或插件卸载正确。

因此开发顺序应是：Standalone 快速迭代 → 单元测试 → Release ZIP → 真实 Host 最终验收。

下一步：[编译、打包、真实 Host 验收与排错](./verification-and-troubleshooting.md)。
