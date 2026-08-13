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

上一章的模块注册必须保持：

```csharp
services.AddScoped<WelcomeDocumentViewModel>();
services.AddSingleton<StatusToolViewModel>();
```

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

## 4. 按命名约定添加 View

宿主把 `QuickStartPlugin.ViewModels.WelcomeDocumentViewModel` 映射为 `QuickStartPlugin.Views.WelcomeDocumentView`，Tool 同理。View 必须是非抽象 Avalonia `Control`，并且能通过 public 无参构造创建。

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

## 5. 构建并观察结果

按上一章的顺序重新构建 Host 和插件，再以 `--no-build` 启动 Host：

```powershell
dotnet build Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug
dotnet build Plugins/QuickStartPlugin/QuickStartPlugin/QuickStartPlugin.csproj -c Debug
dotnet run --project Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug --no-build
```

在插件菜单中选择 `Quick Start / Quick Start Document` 应创建新标签；重复选择应得到不同的 Document 实例。右侧应出现 `Quick Start Status`，关闭后可从工具管理入口恢复，内容状态不会因隐藏而重新创建。

## 保存和后台生命周期是按需能力

- 只有 Document 需要写入 `.mamdoc` 时才实现保存能力；此时必须同时实现 `ISavableDocument` 和 `IDocumentSaveState`。`IsDirty` 通常映射 Dock 的 `IsModified`，持久字段变化时置为 `true`，宿主主文件写入成功后通过 `AcceptChanges()` 清除。
- `CreateSaveDocumentMetaData` 必须只生成快照，不得修改 `FilePath`、标题或脏状态。加载空值、损坏 JSON、缺失必填字段和不支持的格式时抛出脱敏的 `DocumentLoadException`，不要静默返回或输出原始正文。
- 当前项目没有历史 Document 文件兼容要求。新插件只读取自己当前声明的内容格式，不添加旧字段猜测、默认回退或迁移链。宿主会为成功保存的主文件维护 `.recovery.bak`，插件不应自行操作该文件。
- 完整事务、关闭与恢复规则参见 [Document 保存 V1 设计](../design/document-persistence-v1-design.md)。
- 只有插件级后台服务确实需要随宿主启动、停止时才实现并注册 `IPluginLifecycle`。初始化必须幂等，关闭返回前必须停止后台工作；不要用它代替 Document Scope 或 Tool 的视觉生命周期。

完成首次接入后，继续执行[验证与排错](./verification-and-troubleshooting.md)中的清单。
