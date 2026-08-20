# 创建 Managed 插件

本篇以 `QuickStartPlugin` 为示例。完成后，宿主能够读取清单、加载入口程序集、实例化唯一的 `IPluginModule`，并在根容器构建前完成受控注册。Document、Tool 和 View 贡献将在[下一篇](./add-document-and-tool.md)加入。

完整实现可对照 [`MyPlugTest.csproj`](../../Plugins/MyPlugTest/MyPlugTest/MyPlugTest.csproj) 和
[`MyPlugTestPluginModule`](../../Plugins/MyPlugTest/MyPlugTest/Plugin/MyPlugTestPluginModule.cs)。清单由构建生成，
独立发布规则见 [G12 统一插件构建、部署与独立发布](../plan-history/host-v1/g12-unified-plugin-build-and-deployment.md)。

## 1. 创建项目

在仓库根目录执行：

```powershell
dotnet new classlib -n QuickStartPlugin -o Plugins/QuickStartPlugin/QuickStartPlugin -f net10.0
```

将生成的项目文件调整为下面的最小形式。这里的路径假设插件与现有插件采用相同目录深度：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- 插件只声明自身事实；程序集版本、严格清单与部署动作由公共协议派生。 -->
    <ManagedPlugin>true</ManagedPlugin>
    <ManagedPluginId>myavalonia.plugin.quick-start</ManagedPluginId>
    <ManagedPluginDirectoryName>QuickStartPlugin</ManagedPluginDirectoryName>
    <PluginVersion>1.0.0</PluginVersion>
    <ManagedPluginHostApiMinInclusive>1.0.0</ManagedPluginHostApiMinInclusive>
    <ManagedPluginHostApiMaxExclusive>2.0.0</ManagedPluginHostApiMaxExclusive>
    <ManagedPluginCommonContractMinInclusive>1.0.0</ManagedPluginCommonContractMinInclusive>
    <ManagedPluginCommonContractMaxExclusive>2.0.0</ManagedPluginCommonContractMaxExclusive>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="1.0.0" />
  </ItemGroup>
</Project>
```

仓库内同时修改 SDK 和插件源码时，可以临时把 `PackageReference` 换成指向
`MyAvaloniaManagementCommon.csproj` 的 `ProjectReference`，缩短调试反馈；发布兼容测试必须重新使用
正式 nupkg。外部插件不要直接复制 Common DLL 作为裸引用。

不要复制任何现有插件的部署 Target。引入私有第三方运行时包时，用
`<ManagedPluginPrivatePackage Include="Package.Id" />` 声明资产所有权；显式文件使用
`ManagedPluginAsset` 的 `TargetPath`，构建期目录树使用 `ManagedPluginAssetDirectoryRelativePath`。
公共协议会拒绝不存在、越界、重复、非 win-x64 或宿主共享资产。

## 2. 添加严格清单

不在源码树创建 `plugin.manifest.json`。构建根据上一节属性生成以下严格内容到中间目录和输出目录：

```json
{
  "schemaVersion": 1,
  "pluginId": "myavalonia.plugin.quick-start",
  "pluginVersion": "1.0.0",
  "entryAssembly": "QuickStartPlugin.dll",
  "compatibility": {
    "hostApi": {
      "minInclusive": "1.0.0",
      "maxExclusive": "2.0.0"
    },
    "commonContract": {
      "minInclusive": "1.0.0",
      "maxExclusive": "2.0.0"
    }
  }
}
```

生成清单仍是加载插件代码之前的边界，不是宽松配置。必须同时满足：

- 文件不超过 64 KiB，不能包含注释或尾随逗号；
- 字段名称区分大小写，未知、重复或缺失字段都会被拒绝；
- `schemaVersion` 当前只能为 `1`；
- `pluginId` 必须是以 `myavalonia.plugin.` 开头的规范稳定 ID；
- 版本格式是 `major.minor.patch[.revision]`，区间是左闭右开；
- `entryAssembly` 只能是插件根目录里的一个 DLL 文件名，不能包含路径；
- `pluginVersion` 与入口程序集 `AssemblyVersion` 规范化后必须完全一致。

不要随意复制示例的兼容区间，也不要让区间跟随 Host 当前版本自动漂移。发布前应只声明实际验证过的
Host/Common 左闭右开范围。完整规则见[兼容约束](../../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md#31-严格插件清单)。

## 3. 定义稳定 ID

建立 `Constants/PluginIds.cs`：

```csharp
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.ToolCreation;

namespace QuickStartPlugin.Constants;

public static class PluginIds
{
    public static readonly PluginId Plugin =
        new("myavalonia.plugin.quick-start");
    public static readonly DocumentTypeId WelcomeDocument =
        new("myavalonia.plugin.quick-start.document.welcome");
    public static readonly ToolTypeId StatusTool =
        new("myavalonia.plugin.quick-start.tool.status");
}
```

这些 ID 会进入菜单、诊断和布局持久化。一经发布就应保持不变；重命名类、文件夹或显示文字不应改变稳定 ID。旧 ID 只能作为迁移别名输入，不能成为新保存数据的身份。

## 4. 添加唯一模块入口

建立 `Plugin/QuickStartPluginModule.cs`：

```csharp
using MyAvaloniaManagementCommon.Plugin;

namespace QuickStartPlugin.Plugin;

public sealed class QuickStartPluginModule : IPluginModule
{
    public void Configure(IPluginRegistrationContext context)
    {
        // 本章还没有业务服务或可见贡献；下一章会在这里显式登记。
    }
}
```

入口程序集必须只有一个可实例化的 `IPluginModule`，模块必须具有 public 无参构造。上面的隐式无参构造满足要求。模块不声明 `PluginId`：manifest 是身份唯一事实源，宿主把已验证身份作为只读 `context.PluginId` 注入。`Configure` 在根 `IServiceProvider` 构建前且每个进程只调用一次。

`context.Services` 只用于追加插件自己的业务服务，可以选择 singleton、scoped、transient、keyed、开放泛型或同一私有接口的多个实现。不要删除、替换、清空或重排已有描述符，也不要为宿主已经注册的 ServiceType 追加实现；宿主会在根容器构建前校验，违规时返回 `PLUGIN_HOST_SERVICE_MUTATION`。模块返回后保存并修改该集合不会产生注册效果。

Document、Tool、动态 View 和 Lifecycle 必须分别通过 `AddDocument`、`AddTool`、`AddView` 和 `AddLifecycle` 登记；直接向 DI 注册贡献接口会被宿主拒绝。未登记类型即使位于入口程序集也不会被发现。只有确实存在插件级后台资源时才登记 `IPluginLifecycle`，且其初始化和关闭必须幂等、不得依赖 Document 或 Tool 的视觉树生命周期。

## 5. 使用事件总线

插件若需要跨 Document、Tool 或根级协调器发送进程内事件，构造注入 `IHostEventBus`。事件使用插件
自有的强类型 class DTO；不要解析底层 messenger，也不要创建静态总线：

```csharp
using MyAvaloniaManagementCommon.Events;

public sealed class StatusViewModel : IDisposable
{
    private readonly IDisposable _subscription;

    public StatusViewModel(IHostEventBus eventBus)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        _subscription = eventBus.Subscribe<TaskCompletedEvent>(OnTaskCompleted);
    }

    private void OnTaskCompleted(TaskCompletedEvent @event)
    {
        // Publish 在发布线程同步调用这里；异常会原样返回给发布者。
    }

    public void Dispose() => _subscription.Dispose();
}

public sealed record TaskCompletedEvent(string TaskId);
```

`Subscribe` 返回的令牌由订阅者所有，Document 应让自身 Scope 在关闭时释放它；根级 Coordinator
应在插件关闭流程释放。令牌可以重复释放。发布只匹配精确事件类型，按订阅顺序同步执行；处理器抛错
会停止后续处理器。事件不落盘时不要添加无行为的 `Version` 字段，语义破坏时创建新类型。

## 6. 使用宿主样式

普通插件不需要引用 Semi、Ursa 或 Dock Theme。标准 Avalonia 控件会继承宿主主题；主题相关颜色
使用宿主稳定语义资源，例如：

```xml
<Border Background="{DynamicResource AppPanelBrush}"
        BorderBrush="{DynamicResource AppBorderBrush}"
        BorderThickness="1">
  <TextBlock Text="任务失败"
             Foreground="{DynamicResource AppErrorBrush}" />
</Border>
```

必须使用 `DynamicResource`，这样用户切换浅色/深色主题后资源会自动更新。当前正式资源键及兼容
规则见[兼容约束](../../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md#23-插件样式与-ui-profile)。

插件可以把自己的局部样式放在 `Styles/QuickStartPluginStyles.axaml`，再从 View 引用：

```xml
<UserControl.Styles>
  <StyleInclude Source="avares://QuickStartPlugin/Styles/QuickStartPluginStyles.axaml" />
</UserControl.Styles>
```

局部资源键和 Style Class 使用插件 ID 前缀；不要向 `Application.Current.Styles` 注入全局主题。

如果插件需要直接使用 Semi、Ursa 或 Dock UI 控件，把基础包替换为同版本 UI Profile：

```xml
<PackageReference Include="MyAvaloniaManagement.PluginSdk.UI" Version="1.0.0" />
```

UI Profile 已传递基础 SDK，并把第三方 UI 包限制为宿主验证版本。第三方主题自己的资源键只在该
Profile 版本内受支持，不属于基础 `App*` 语义资源契约。

## 7. 构建、部署并启动

在仓库根目录按以下顺序执行：

```powershell
dotnet build Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug
dotnet build Plugins/QuickStartPlugin/QuickStartPlugin/QuickStartPlugin.csproj -c Debug
dotnet run --project Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug --no-build
```

先构建 Host，再构建插件，最后使用 `--no-build` 启动，可以确保插件构建目标最后写入正确的 Host 输出目录。部署结果应为：

```text
Host/MyAvaloniaManagement/bin/Debug/net10.0/
└── Controls/
    └── QuickStartPlugin/
        ├── plugin.manifest.json
        ├── QuickStartPlugin.dll
        ├── QuickStartPlugin.deps.json（必需）
        └── QuickStartPlugin.pdb（必需）
```

一个插件独占一个目录，入口只由清单声明；宿主不扫描其他 DLL 猜测入口或依赖。宿主对插件目录的发现结果在单个进程内缓存；替换 DLL、deps 或清单后必须完整退出并重新启动宿主。

独立生成正式候选包：

```powershell
.\scripts\Build-ManagedPluginPackage.ps1 `
  -Project Plugins/QuickStartPlugin/QuickStartPlugin/QuickStartPlugin.csproj `
  -Configuration Release
```

结果为 `QuickStartPlugin-1.0.0-win-x64.zip` 及同名外置 `.manifest.json`。ZIP 内只有
`Controls/QuickStartPlugin/`，不会与其他插件合并发布。

## 外部作者的编译与交付边界

外部项目使用宿主发布方提供的 `MyAvaloniaManagement.PluginSdk`，需要第三方 UI 控件时改用同版本
`MyAvaloniaManagement.PluginSdk.UI`。当前仓库只生成本地/发布制品，不自动推送公共 NuGet；
宿主发布包必须同时提供对应 nupkg。交付目录只包含：

- `plugin.manifest.json`；
- 入口程序集及其 `.deps.json`；
- 插件自己拥有的托管、卫星和 RID 原生依赖。

不得交付 `MyAvaloniaManagementCommon.dll`，也不得私带 Avalonia、Semi、Ursa、Dock 或其他由宿主
默认加载上下文拥有的共享依赖；否则类型身份或版本不一致会触发
`PLUGIN_SHARED_ASSEMBLY_MISMATCH`。外部插件必须针对明确的 Host/SDK 版本组合完成本篇和
[验证与排错](./verification-and-troubleshooting.md)中的检查。
