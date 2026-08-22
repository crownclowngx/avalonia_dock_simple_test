# 添加 Document 与 Tool

本篇在 `QuickStartPlugin` 中加入一个普通 Document 和一个普通 Tool。声明式注册是模型、View 与元数据的唯一事实源：不创建 Strategy，也不单独注册 View。

## 1. Document 模型

建立 `ViewModels/WelcomeDocumentViewModel.cs`：

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using MyAvaloniaManagement.PluginSdk;

namespace QuickStartPlugin.ViewModels;

public sealed partial class WelcomeDocumentViewModel : ObservableObject, IPluginDocument
{
    private const string DefaultTitle = "欢迎";
    private readonly IDocumentLifetime _lifetime;
    private string _title = DefaultTitle;

    [ObservableProperty]
    private string message = "Hello from V3 G1";

    public WelcomeDocumentViewModel(IDocumentLifetime lifetime) =>
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));

    public DocumentPresentationState Presentation => new(_title);

    public event EventHandler? PresentationChanged;

    public ValueTask InitializeAsync(
        DocumentActivationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        _lifetime.ClosingToken.ThrowIfCancellationRequested();

        _title = string.IsNullOrWhiteSpace(context.Title) ? DefaultTitle : context.Title;
        PresentationChanged?.Invoke(this, EventArgs.Empty);
        return ValueTask.CompletedTask;
    }
}
```

`IDocumentLifetime` 是必需构造依赖。模型只能观察关闭令牌并协作取消自身工作，不能关闭自己或其他 Document。每次激活创建独立 Scope，因此可变 Document 状态不能放入 singleton。

建立 `Views/WelcomeDocumentView.axaml` 与 code-behind：

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="QuickStartPlugin.Views.WelcomeDocumentView">
  <StackPanel Margin="16" Spacing="8">
    <TextBlock Text="V3 G1 Document" FontWeight="Bold" />
    <TextBox Text="{Binding Message}" />
  </StackPanel>
</UserControl>
```

```csharp
using Avalonia.Controls;

namespace QuickStartPlugin.Views;

public sealed partial class WelcomeDocumentView : UserControl
{
    public WelcomeDocumentView() => InitializeComponent();
}
```

View 必须有 public 无参构造。Host 创建 View、设置 `DataContext` 并包装为 internal Dock Adapter；插件不引用 Dock。

## 2. Tool 模型与 View

建立 `ViewModels/StatusToolViewModel.cs`：

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace QuickStartPlugin.ViewModels;

public sealed partial class StatusToolViewModel : ObservableObject
{
    [ObservableProperty]
    private int activationCount;
}
```

Tool 不实现 Dock 类型，也不拥有标题、位置、关闭或浮动状态。创建 `StatusToolView` 的方式与 Document View 相同，并绑定 `ActivationCount`。

## 3. 一次声明贡献

补全模块：

```csharp
using MyAvaloniaManagement.PluginSdk.UI;
using QuickStartPlugin.Constants;
using QuickStartPlugin.ViewModels;
using QuickStartPlugin.Views;

namespace QuickStartPlugin.Plugin;

public sealed class QuickStartPluginModule : IPluginModule
{
    public void Configure(IPluginRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.AddDocument<WelcomeDocumentViewModel, WelcomeDocumentView>(
            new DocumentDescriptor(
                PluginIds.WelcomeDocument,
                "欢迎",
                "演示独立 Document Scope",
                "快速开始"));

        registration.AddTool<StatusToolViewModel, StatusToolView>(
            new ToolDescriptor(
                PluginIds.StatusTool,
                "状态",
                "演示插件级 Tool singleton",
                ToolDockSide.Right,
                ToolCloseBehavior.Hide));
    }
}
```

注册方法自动把 Document 模型注册为 scoped、Tool 模型注册为插件级 singleton。不要再对这两个模型执行 `AddScoped`/`AddSingleton`，也不要建立独立 View 映射。

## 4. 可选：持久化 Document

需要保存业务内容时，把模型改为 `IPersistablePluginDocument`，注册改为 `AddPersistableDocument`。插件只拥有 content schema 和 JSON payload；Host 独占文件路径、外层信封、原子写入与成功提交时机。

实现应遵循：

- 每次持久内容真正变化都推进插件自己的 `DocumentRevision`，即使模型已经 Dirty 也不能停止计数；
- `CaptureSaveSnapshotAsync` 返回不可变的 `DocumentSaveSnapshot(revision, content)`，不直接写文件；
- 恢复时先严格验证 schema、字段、类型和完整临时状态，再一次提交到模型；
- `IsDirty` 实际变化时发出 `IsDirtyChanged`，让 Host 同步 Tab 的修改标记；
- Host 原子保存成功后只把捕获修订传给 `AcceptChanges(savedRevision)`；捕获后若又有编辑，旧修订确认
  必须是幂等无操作，模型继续保持 Dirty；
- 不兼容内容直接失败，不做猜测式修复或 V1 兼容读取。

可直接参考 [`TestWelcomeViewModel`](../../Plugins/MyPlugTest/MyPlugTest/ViewModels/TestWelcomeViewModel.cs) 和独立 [`TestWelcomeDocumentContentCodec`](../../Plugins/MyPlugTest/MyPlugTest/Persistence/TestWelcomeDocumentContentCodec.cs)。

## 5. 可选：进程内事件

构造注入 Core SDK `IHostEventBus`，事件 DTO 使用插件自有普通密封类型。发布方只发布；订阅方保存 `Subscribe<T>` 返回的令牌，并在自身 Scope 释放时解绑。总线按精确类型在发布线程同步投递，因此 UI 订阅者需要自行切回 Dispatcher。

```csharp
public sealed record WorkCompletedEvent(string Message);

public sealed class Receiver : IDisposable
{
    private readonly IDisposable _subscription;

    public Receiver(IHostEventBus eventBus) =>
        _subscription = eventBus.Subscribe<WorkCompletedEvent>(OnCompleted);

    private void OnCompleted(WorkCompletedEvent message)
    {
        // 若修改 Avalonia 状态，应在这里显式切换到 UI Dispatcher。
    }

    public void Dispose() => _subscription.Dispose();
}
```

## 6. 验收行为

- 连续打开两个 Document：模型、Scope 与局部状态彼此独立；
- 关闭一个 Document：只取消并释放自己的 Scope；
- 多次显示 Tool：模型引用相同；关闭后恢复不丢状态；
- View 的 `DataContext` 是对应模型；
- 插件源码不含 Strategy、Dock 基类、Legacy 契约或独立 View 注册。

下一步按[验证与排错](./verification-and-troubleshooting.md)检查真实加载和独立 ZIP。
