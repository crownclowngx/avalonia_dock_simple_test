# 创建 Managed Plugin

本篇按当前未发布 V3 G2 创建 `QuickStartPlugin` 的项目、稳定身份与模块入口。Document 保存契约已采用
修订快照，其他 API 仍沿用 V2 G14；
可运行事实源是 [`MyPlugTest.csproj`](../../Plugins/MyPlugTest/MyPlugTest/MyPlugTest.csproj) 和 [`MyPlugTestPluginModule`](../../Plugins/MyPlugTest/MyPlugTest/Plugin/MyPlugTestPluginModule.cs)。

## 1. 创建项目

在仓库根目录执行：

```powershell
dotnet new classlib -n QuickStartPlugin -o Plugins/QuickStartPlugin/QuickStartPlugin -f net10.0
```

将项目文件调整为：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ManagedPlugin>true</ManagedPlugin>
    <ManagedPluginId>myavalonia.plugin.quick-start</ManagedPluginId>
    <ManagedPluginDirectoryName>QuickStartPlugin</ManagedPluginDirectoryName>
    <PluginVersion>3.0.0</PluginVersion>
    <ManagedPluginEntryType>QuickStartPlugin.Plugin.QuickStartPluginModule</ManagedPluginEntryType>
    <ManagedPluginSdkMinInclusive>$(MyAvaloniaPluginSdkVersion)</ManagedPluginSdkMinInclusive>
    <ManagedPluginSdkMaxExclusive>$(MyAvaloniaPluginSdkNextMajorVersion)</ManagedPluginSdkMaxExclusive>
  </PropertyGroup>

  <ItemGroup>
    <!-- Core 与 UI SDK 都由 Host 提供；插件包不得复制这些程序集。 -->
    <ProjectReference Include="../../../Host/MyAvaloniaManagement.PluginSdk/MyAvaloniaManagement.PluginSdk.csproj"
                      Private="false" />
    <ProjectReference Include="../../../Host/MyAvaloniaManagement.PluginSdk.UI/MyAvaloniaManagement.PluginSdk.UI.csproj"
                      Private="false" />
    <PackageReference Include="CommunityToolkit.Mvvm" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  </ItemGroup>
</Project>
```

外部项目使用宿主发布方提供的同版本 NuGet 包，并同样排除运行时复制。G14 正式基线的构建探针无条件验证
最终 UI SDK `IPluginModule`，不存在入口契约选择开关；插件不得引用已删除的 Legacy、Dock 或 Host 生产项目。

## 2. 理解生成清单

不要在源码目录手写清单。公共构建协议根据项目属性生成严格 manifest v2：

```json
{
  "schemaVersion": 2,
  "pluginId": "myavalonia.plugin.quick-start",
  "pluginVersion": "3.0.0",
  "entryPoint": {
    "assembly": "QuickStartPlugin.dll",
    "type": "QuickStartPlugin.Plugin.QuickStartPluginModule"
  },
  "sdk": {
    "minInclusive": "3.0.0",
    "maxExclusive": "4.0.0"
  }
}
```

字段区分大小写；未知、重复、缺失字段以及 v1 schema 都会被拒绝。入口类型必须 public、非抽象、非泛型，实现最终 UI SDK `IPluginModule`，并具有 public 无参构造。

## 3. 定义稳定 ID

建立 `Constants/PluginIds.cs`：

```csharp
using MyAvaloniaManagement.PluginSdk;

namespace QuickStartPlugin.Constants;

public static class PluginIds
{
    public static readonly PluginId Plugin = new("myavalonia.plugin.quick-start");
    public static readonly DocumentTypeId WelcomeDocument =
        new("myavalonia.plugin.quick-start.document.welcome");
    public static readonly ToolTypeId StatusTool =
        new("myavalonia.plugin.quick-start.tool.status");
}
```

当前生产语义只接受规范主 ID，不提供 `LegacyIds`。类名和显示文字可以变化，已发布的稳定 ID 不应变化。

## 4. 建立组合根

建立 `Plugin/QuickStartPluginModule.cs`：

```csharp
using MyAvaloniaManagement.PluginSdk.UI;

namespace QuickStartPlugin.Plugin;

public sealed class QuickStartPluginModule : IPluginModule
{
    public void Configure(IPluginRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        // 下一篇在这里一次声明模型、View 和 Descriptor。
    }
}
```

manifest 是插件身份唯一事实源，模块不重复声明 `PluginId`。`registration.Services` 只属于当前插件；普通业务服务使用标准 DI 注册，宿主可见贡献必须使用 `AddDocument`、`AddPersistableDocument`、`AddTool` 或 `UseLifecycle`。

## 5. 构建与运行

完成[下一篇](./add-document-and-tool.md)的代码后执行：

```powershell
dotnet build Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug
dotnet build Plugins/QuickStartPlugin/QuickStartPlugin/QuickStartPlugin.csproj -c Debug
dotnet run --project Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug --no-build
```

构建产物位于 `Host/MyAvaloniaManagement/bin/Debug/net10.0/Controls/QuickStartPlugin/`。替换插件文件后必须完整重启 Host，因为发现结果在单个进程内缓存。

生成独立测试 ZIP：

```powershell
.\scripts\Build-ManagedPluginPackage.ps1 `
  -Project Plugins/QuickStartPlugin/QuickStartPlugin/QuickStartPlugin.csproj `
  -Configuration Release
```

ZIP 只应包含清单、入口程序集、deps、PDB 及插件私有资产；不得包含 Core/UI SDK、Avalonia、Semi、Ursa、Dock、Host 或 `Microsoft.Extensions.*` 共享程序集。
