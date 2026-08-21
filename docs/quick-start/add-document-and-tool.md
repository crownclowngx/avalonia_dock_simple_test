# 添加 Document 与 Tool

本篇在同一个 `QuickStartPlugin` 中加入两个可见扩展：可多开的欢迎 Document 和宿主级单例状态 Tool。代码只保留理解契约所需的最小部分，生产实现请继续对照 [`MyPlugTest`](../../Plugins/MyPlugTest/MyPlugTest/)。

## 1. 添加两个 ViewModel

建立 `ViewModels/WelcomeDocumentViewModel.cs`：

```csharp
using Dock.Model.Mvvm.Controls;

namespace QuickStartPlugin.ViewModels;

public sealed class WelcomeDocumentViewModel : Document
{
    public string Message => "Quick Start Document 已加载";
}
```

建立 `ViewModels/StatusToolViewModel.cs`：

```csharp
using Dock.Model.Mvvm.Controls;

namespace QuickStartPlugin.ViewModels;

public sealed class StatusToolViewModel : Tool
{
    public string Message => "Quick Start Tool 已加载";
}
```

在模块的 `Configure` 中把 ViewModel 注册为插件私有服务：

```csharp
context.Services.AddScoped<WelcomeDocumentViewModel>();
context.Services.AddSingleton<StatusToolViewModel>();
```

这里的 `Services` 是当前模块调用独占的事务工作副本，只能追加插件私有服务。不要调用
`Remove`、`Replace`、`Clear` 或重排已有描述符，也不要为宿主已经注册的 ServiceType 追加实现；
违规会在根容器构建前以 `PLUGIN_HOST_SERVICE_MUTATION` 拒绝。

Document 表示一次独立工作会话，因此每个标签从新的 DI Scope 解析。Tool 表示宿主级面板，因此注册为 Singleton；隐藏再恢复时仍是同一个对象。

## 2. 添加 Document 创建策略

建立 `Create/WelcomeDocumentStrategy.cs`：

```csharp
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using QuickStartPlugin.Constants;
using QuickStartPlugin.ViewModels;

namespace QuickStartPlugin.Create;

public sealed class WelcomeDocumentStrategy : IDocumentCreationStrategy
{
    private readonly IDocumentScopeFactory _documentScopeFactory;

    public WelcomeDocumentStrategy(IDocumentScopeFactory documentScopeFactory)
    {
        _documentScopeFactory = documentScopeFactory;
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        var document =
            _documentScopeFactory.CreateDocument<WelcomeDocumentViewModel>();
        document.Title = string.IsNullOrWhiteSpace(@params.Title)
            ? "Quick Start"
            : @params.Title;
        return document;
    }

    public DocumentMetadata GetMetadata() => new(
        PluginIds.WelcomeDocument,
        "Quick Start Document")
    {
        Description = "打开一个最小插件工作会话",
        MenuCategory = "Quick Start"
    };
}
```

关键点不是手动 `new` ViewModel，而是调用 `IDocumentScopeFactory.CreateDocument<TDocument>()`。插件不得保存或释放 `IServiceScope`；宿主会在 Dock 确认关闭后释放作用域及其中的资源。需要响应关闭时，可在 ViewModel 中注入 `IDocumentLifetime` 并观察其 `ClosingToken`。

真实实现参见 [`TestWelcomeDocumentStrategy`](../../Plugins/MyPlugTest/MyPlugTest/Create/TestWelcomeDocumentStrategy.cs) 和 [`TestWelcomeViewModel`](../../Plugins/MyPlugTest/MyPlugTest/ViewModels/TestWelcomeViewModel.cs)。

## 3. 添加 Tool 创建策略

建立 `Create/StatusToolStrategy.cs`：

```csharp
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.ToolCreation;
using QuickStartPlugin.Constants;
using QuickStartPlugin.ViewModels;

namespace QuickStartPlugin.Create;

public sealed class StatusToolStrategy : IToolCreationStrategy
{
    private readonly StatusToolViewModel _viewModel;

    public StatusToolStrategy(StatusToolViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public Tool CreateTool()
    {
        _viewModel.Title = "Quick Start Status";
        _viewModel.CanClose = true;
        return _viewModel;
    }

    public ToolMetadata GetMetadata() => new(
        PluginIds.StatusTool,
        "Quick Start Status",
        ToolDockSide.Right)
    {
        Description = "显示最小插件状态"
    };
}
```

策略返回模块中注册的 Singleton，不应在每次调用时创建新 Tool。宿主会按元数据放置 Tool，并把关闭操作处理为隐藏；重新显示时仍返回原有实例。真实实现参见 [`MyCustomToolStrategy`](../../Plugins/MyPlugTest/MyPlugTest/Create/MyCustomToolStrategy.cs) 和 [`MyCustomToolViewModel`](../../Plugins/MyPlugTest/MyPlugTest/ViewModels/MyCustomToolViewModel.cs)。

## 4. 添加 View

View 必须是非抽象 Avalonia `Control`，并且能通过 public 无参构造创建。宿主不扫描程序集，也不再把 `ViewModel` 名称替换成 `View` 猜测类型；映射会在第 5 节由模块显式登记。

建立 `Views/WelcomeDocumentView.axaml`：

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:QuickStartPlugin.ViewModels"
             x:Class="QuickStartPlugin.Views.WelcomeDocumentView"
             x:DataType="vm:WelcomeDocumentViewModel">
  <TextBlock Margin="16" Text="{Binding Message}" />
</UserControl>
```

建立 `Views/StatusToolView.axaml`，仅替换类型名：

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:QuickStartPlugin.ViewModels"
             x:Class="QuickStartPlugin.Views.StatusToolView"
             x:DataType="vm:StatusToolViewModel">
  <TextBlock Margin="12" Text="{Binding Message}" />
</UserControl>
```

两个 View 都需要相同形式的 code-behind；下面以 Document 为例：

```csharp
using Avalonia.Controls;

namespace QuickStartPlugin.Views;

public partial class WelcomeDocumentView : UserControl
{
    public WelcomeDocumentView()
    {
        InitializeComponent();
    }
}
```

Tool 的 code-behind 类名改为 `StatusToolView`。不要给 View 注入构造参数；业务依赖应进入由 DI 创建的 ViewModel。可对照 [`TestWelcomeView.axaml`](../../Plugins/MyPlugTest/MyPlugTest/Views/TestWelcomeView.axaml)、[`TestWelcomeView.axaml.cs`](../../Plugins/MyPlugTest/MyPlugTest/Views/TestWelcomeView.axaml.cs) 和 [`MyCustomToolView.axaml`](../../Plugins/MyPlugTest/MyPlugTest/Views/MyCustomToolView.axaml)。

## 5. 显式登记全部贡献

把 manifest 精确声明的模块更新为完整版本：

```csharp
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;
using QuickStartPlugin.Create;
using QuickStartPlugin.ViewModels;
using QuickStartPlugin.Views;

namespace QuickStartPlugin.Plugin;

public sealed class QuickStartPluginModule : IPluginModule
{
    public void Configure(IPluginRegistrationContext context)
    {
        // Services 只承载插件的私有业务对象和 ViewModel 生命周期。
        context.Services.AddScoped<WelcomeDocumentViewModel>();
        context.Services.AddSingleton<StatusToolViewModel>();

        // 四类宿主可见贡献必须逐项登记；未登记类型不会进入菜单、Dock 或 ViewLocator。
        context.AddDocument<WelcomeDocumentStrategy>();
        context.AddTool<StatusToolStrategy>();
        context.AddView<WelcomeDocumentViewModel, WelcomeDocumentView>();
        context.AddView<StatusToolViewModel, StatusToolView>();
    }
}
```

`AddDocument`、`AddTool` 会把策略登记为根级单例并在 Registry 发布前激活、读取一次元数据。
`AddView` 保存 ViewModel 类型到无参 View 工厂的映射，控件只在 DataTemplate 实际请求时创建。
只有全局 DataTemplate 动态解析的根 View 需要登记；由 XAML 直接嵌套创建的内部控件不进入 Registry。

## 6. 构建并观察结果

按上一章的顺序重新构建 Host 和插件，再以 `--no-build` 启动 Host：

```powershell
dotnet build Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug
dotnet build Plugins/QuickStartPlugin/QuickStartPlugin/QuickStartPlugin.csproj -c Debug
dotnet run --project Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug --no-build
```

在插件菜单中选择 `Quick Start / Quick Start Document` 应创建新标签；重复选择应得到不同的 Document 实例。右侧应出现 `Quick Start Status`，关闭后可从工具管理入口恢复，内容状态不会因隐藏而重新创建。

## 保存和后台生命周期是按需能力

- 只有 Document 需要写入 `.mamdoc` 时才实现保存能力；此时必须同时实现 `ISavableDocument` 和 `IDocumentSaveState`。`IsDirty` 通常映射 Dock 的 `IsModified`，持久字段变化时置为 `true`，宿主主文件写入成功后通过 `AcceptChanges()` 清除。
- `CreateContentSnapshot()` 必须只生成 `new DocumentContentSnapshot(内容版本, payload)`，不得修改标题或脏状态。路径与 Document 类型不在插件契约中，只由宿主持有。内容版本必须为正整数，payload 不得为 `null`。
- `RestoreContent(snapshot)` 先精确检查 `ContentSchemaVersion`，再校验 `Payload` 的业务结构。恢复空白、损坏 JSON、缺失必填字段和未知版本时抛出脱敏的 `DocumentLoadException`，不要静默返回或输出原始正文。
- Host 独占信封中的 `schemaVersion`、`pluginId`、`documentTypeId`、`title` 和 `savedAtUtc`；插件不要复制或依赖这些宿主字段。示例实现如下：

```csharp
private const int CurrentContentSchemaVersion = 1;

public DocumentContentSnapshot CreateContentSnapshot()
{
    // 创建快照只读取当前业务状态。路径、身份、标题、时间和保存提交点均由宿主负责。
    var payload = JsonSerializer.Serialize(new WelcomeState(Message));
    return new DocumentContentSnapshot(CurrentContentSchemaVersion, payload);
}

public void RestoreContent(DocumentContentSnapshot snapshot)
{
    // 不猜测未知版本，防止把未来格式误当成当前格式并产生静默数据损坏。
    if (snapshot.ContentSchemaVersion != CurrentContentSchemaVersion)
    {
        throw new DocumentLoadException("不支持该文档内容版本。");
    }

    try
    {
        var state = JsonSerializer.Deserialize<WelcomeState>(snapshot.Payload);
        Message = state?.Message
            ?? throw new DocumentLoadException("文档内容缺少必填字段。");
    }
    catch (JsonException exception)
    {
        // 稳定消息不能包含原始 payload；内部异常只用于保留诊断原因。
        throw new DocumentLoadException("文档内容已损坏。", exception);
    }
}
```

- v1 是项目第一个且唯一受支持的 Document 信封。新插件不添加旧字段猜测、默认回退或迁移链；任何非 v1 结构由宿主直接拒绝。宿主会为成功保存的主文件维护 `.recovery.bak`，插件不应自行操作该文件。
- 完整事务、关闭与恢复规则参见 [Document 保存 V1 设计](../design/document-persistence-v1-design.md)。
- 只有插件级后台服务确实需要随宿主启动、停止时才实现并注册 `IPluginLifecycle`。初始化必须幂等，关闭返回前必须停止后台工作；不要用它代替 Document Scope 或 Tool 的视觉生命周期。

完成首次接入后，继续执行[验证与排错](./verification-and-troubleshooting.md)中的清单。
