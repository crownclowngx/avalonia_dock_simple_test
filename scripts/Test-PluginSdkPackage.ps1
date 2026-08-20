param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ("MyAvaloniaPluginSdkPackage-" + [Guid]::NewGuid().ToString("N"))))
$packageOutput = Join-Path $temporaryRoot "packages"
$isolatedPackageCache = Join-Path $temporaryRoot "global-packages"
$sdkVersion = ([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot "Directory.Version.props"))).Project.PropertyGroup.MyAvaloniaPluginSdkVersion

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-DotNet {
    param([string[]]$Arguments, [string]$WorkingDirectory = $repositoryRoot)
    Push-Location $WorkingDirectory
    try {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') 失败，退出码 $LASTEXITCODE。"
        }
    }
    finally {
        Pop-Location
    }
}

function Assert-DotNetBuildFails {
    param([string]$ProjectPath, [string]$WorkingDirectory, [string[]]$ExpectedFragments)
    Push-Location $WorkingDirectory
    try {
        $output = @(& dotnet build $ProjectPath -c Release --no-restore --nologo 2>&1)
        $exitCode = $LASTEXITCODE
        Assert-True ($exitCode -ne 0) "反向兼容夹具意外编译成功，预期删除的 SDK 契约仍然可用。"
        $text = $output -join [Environment]::NewLine
        foreach ($fragment in $ExpectedFragments) {
            Assert-True ($text.IndexOf($fragment, [StringComparison]::Ordinal) -ge 0) "旧候选夹具失败信息缺少 $fragment。"
        }
    }
    finally {
        Pop-Location
    }
}

function Read-Nuspec {
    param([string]$PackagePath)
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entry = $archive.Entries | Where-Object FullName -Like "*.nuspec" | Select-Object -First 1
        Assert-True ($null -ne $entry) "包 $PackagePath 缺少 nuspec。"
        $reader = [IO.StreamReader]::new($entry.Open())
        try {
            return [xml]$reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-PackageEntries {
    param([string]$PackagePath)
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        return @($archive.Entries | ForEach-Object FullName)
    }
    finally {
        $archive.Dispose()
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null

try {
    Invoke-DotNet @(
        "pack", "Host/MyAvaloniaManagementCommon/MyAvaloniaManagementCommon.csproj",
        "-c", $Configuration, "--no-restore", "-o", $packageOutput, "--nologo"
    )
    Invoke-DotNet @(
        "pack", "Host/MyAvaloniaManagement.PluginSdk.UI/MyAvaloniaManagement.PluginSdk.UI.csproj",
        "-c", $Configuration, "--no-restore", "-o", $packageOutput, "--nologo"
    )

    $basePackage = Join-Path $packageOutput "MyAvaloniaManagement.PluginSdk.$sdkVersion.nupkg"
    $uiPackage = Join-Path $packageOutput "MyAvaloniaManagement.PluginSdk.UI.$sdkVersion.nupkg"
    Assert-True (Test-Path -LiteralPath $basePackage) "未生成基础 SDK 包。"
    Assert-True (Test-Path -LiteralPath $uiPackage) "未生成 UI Profile 包。"

    $baseEntries = Get-PackageEntries $basePackage
    Assert-True ($baseEntries -contains "lib/net10.0/MyAvaloniaManagementCommon.dll") "基础 SDK 包缺少契约程序集。"
    Assert-True ($baseEntries -contains "lib/net10.0/MyAvaloniaManagementCommon.xml") "基础 SDK 包缺少 XML 文档。"
    Assert-True ($baseEntries -contains "README.md") "基础 SDK 包缺少 README。"
    Assert-True (-not ($baseEntries -contains "lib/net10.0/MyAvaloniaManagement.dll")) "基础 SDK 包错误包含 Host 程序集。"

    $uiEntries = Get-PackageEntries $uiPackage
    Assert-True ($uiEntries -contains "lib/net10.0/_._") "UI Profile 缺少 NuGet 元包占位。"
    Assert-True (-not ($uiEntries | Where-Object { $_ -like "lib/*/*.dll" })) "UI Profile 不应包含运行时程序集。"

    $baseNuspec = Read-Nuspec $basePackage
    $uiNuspec = Read-Nuspec $uiPackage
    $baseDependencyIds = @($baseNuspec.package.metadata.dependencies.group.dependency | ForEach-Object id)
    $forbiddenBaseDependencies = @(
        "Avalonia.Desktop", "Avalonia.Fonts.Inter", "Avalonia.Themes.Fluent", "CommunityToolkit.Mvvm",
        "Dock.Avalonia", "Dock.Avalonia.Themes.Fluent",
        "Dock.Controls.ProportionalStackPanel", "Dock.Controls.Recycling",
        "Dock.Controls.Recycling.Model", "Irihi.Ursa", "Irihi.Ursa.Themes.Semi", "Semi.Avalonia",
        "Xaml.Behaviors", "Microsoft.CodeAnalysis.PublicApiAnalyzers"
    )
    foreach ($dependency in $forbiddenBaseDependencies) {
        Assert-True ($baseDependencyIds -notcontains $dependency) "基础 SDK 依赖图错误包含 $dependency。"
    }

    $uiDependencies = @{}
    foreach ($dependency in $uiNuspec.package.metadata.dependencies.group.dependency) {
        $uiDependencies[$dependency.id] = $dependency.version
    }
    Assert-True ($uiDependencies["MyAvaloniaManagement.PluginSdk"] -eq $sdkVersion) "UI Profile 未依赖同版本基础 SDK。"
    $expectedExactVersions = @{
        "Avalonia.Themes.Fluent" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaAvaloniaUiVersion)]"
        "Dock.Avalonia" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaDockUiVersion)]"
        "Dock.Avalonia.Themes.Fluent" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaDockUiVersion)]"
        "Dock.Controls.ProportionalStackPanel" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaDockUiVersion)]"
        "Dock.Controls.Recycling" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaDockUiVersion)]"
        "Dock.Controls.Recycling.Model" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaDockUiVersion)]"
        "Irihi.Ursa" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaUrsaUiVersion)]"
        "Irihi.Ursa.Themes.Semi" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaUrsaUiVersion)]"
        "Semi.Avalonia" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaSemiUiVersion)]"
    }
    foreach ($item in $expectedExactVersions.GetEnumerator()) {
        Assert-True ($uiDependencies[$item.Key] -eq $item.Value) "UI Profile 的 $($item.Key) 版本应为 $($item.Value)，实际为 $($uiDependencies[$item.Key])。"
    }

    $basicProject = Join-Path $temporaryRoot "BasicPlugin"
    New-Item -ItemType Directory -Path $basicProject | Out-Null
    $nugetConfig = Join-Path $temporaryRoot "NuGet.Config"
    $escapedPackageOutput = [Security.SecurityElement]::Escape($packageOutput)
    Set-Content -LiteralPath $nugetConfig -Encoding UTF8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="g3-local" value="$escapedPackageOutput" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
"@
    Set-Content -LiteralPath (Join-Path $basicProject "BasicPlugin.csproj") -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="$sdkVersion" /></ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $basicProject "BasicPluginModule.cs") -Encoding UTF8 -Value @'
using System;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Events;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.Save;

public sealed class BasicPluginModule : IPluginModule
{
    public void Configure(IPluginRegistrationContext context)
    {
        _ = context.PluginId;
        context.Services.AddSingleton<BasicPluginService>();
    }
}

public sealed class BasicPluginService;

public static class BasicDocumentCreation
{
    public static DocumentCreationParams Create(DocumentTypeId documentTypeId) =>
        new(documentTypeId)
        {
            Title = "示例文档",
            CreationIntentId = new CreationIntentId("myavalonia.plugin.basic.intent.default"),
        };
}

public sealed record BasicPluginEvent(string Value);

public sealed class BasicPluginEventConsumer(IHostEventBus eventBus) : IDisposable
{
    private readonly IDisposable _subscription = eventBus.Subscribe<BasicPluginEvent>(_ => { });

    public void Publish() => eventBus.Publish(new BasicPluginEvent("value"));

    public void Dispose() => _subscription.Dispose();
}

public sealed class BasicDocumentContentFactory
{
    public DocumentContentSnapshot Create() => new(1, "{\"value\":42}");
}
'@
    Invoke-DotNet @("restore", "BasicPlugin.csproj", "--configfile", $nugetConfig, "--packages", $isolatedPackageCache, "--nologo") $basicProject
    Invoke-DotNet @("build", "BasicPlugin.csproj", "-c", "Release", "--no-restore", "--nologo") $basicProject
    $basicAssets = Get-Content -Raw -LiteralPath (Join-Path $basicProject "obj/project.assets.json")
    # Dock.Model.Mvvm 12.0.0.2 自身传递依赖 Dock.Controls.Recycling.Model 与
    # CommunityToolkit.Mvvm 8.4.0；它们不在基础包 nuspec 的直接依赖中，但会出现在最终还原图。
    # G9 删除的是 SDK 自有直接依赖和 public 消息类型，不能改写上游 Dock 的依赖图。
    $forbiddenResolvedDependencies = $forbiddenBaseDependencies |
        Where-Object { $_ -notin @("Dock.Controls.Recycling.Model", "CommunityToolkit.Mvvm") }
    foreach ($dependency in $forbiddenResolvedDependencies) {
        Assert-True (-not $basicAssets.Contains('"' + $dependency + '/')) "基础插件还原图错误包含 $dependency。"
    }

    # 旧候选接口必须失败，防止未来为了偶然二进制兼容重新引入模块自报身份或 ConfigureServices。
    $legacyProject = Join-Path $temporaryRoot "LegacyCandidatePlugin"
    New-Item -ItemType Directory -Path $legacyProject | Out-Null
    Set-Content -LiteralPath (Join-Path $legacyProject "LegacyCandidatePlugin.csproj") -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="$sdkVersion" /></ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $legacyProject "LegacyCandidatePluginModule.cs") -Encoding UTF8 -Value @'
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;

public sealed class LegacyCandidatePluginModule : IPluginModule
{
    public PluginId PluginId { get; } = new("myavalonia.plugin.legacy-candidate");
    public void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<LegacyCandidatePluginModule>();
}
'@
    Invoke-DotNet @("restore", "LegacyCandidatePlugin.csproj", "--configfile", $nugetConfig, "--packages", $isolatedPackageCache, "--nologo") $legacyProject
    Assert-DotNetBuildFails "LegacyCandidatePlugin.csproj" $legacyProject @("CS0535", "Configure")

    # G8 破坏式删除旧候选 DTO。该夹具固定“旧类型本身不存在”，避免以后用别名或
    # Obsolete 适配器重新形成第二套内容契约。
    $legacyEnvelopeProject = Join-Path $temporaryRoot "LegacyDocumentEnvelope"
    New-Item -ItemType Directory -Path $legacyEnvelopeProject | Out-Null
    Set-Content -LiteralPath (Join-Path $legacyEnvelopeProject "LegacyDocumentEnvelope.csproj") -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="$sdkVersion" /></ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $legacyEnvelopeProject "LegacyEnvelopeConsumer.cs") -Encoding UTF8 -Value @'
using MyAvaloniaManagementCommon.Save;

public static class LegacyEnvelopeConsumer
{
    public static object CreateRemovedType() => new DocumentSaveData(1, "{}");
}
'@
    Invoke-DotNet @("restore", "LegacyDocumentEnvelope.csproj", "--configfile", $nugetConfig, "--packages", $isolatedPackageCache, "--nologo") $legacyEnvelopeProject
    Assert-DotNetBuildFails "LegacyDocumentEnvelope.csproj" $legacyEnvelopeProject @("CS0246", "DocumentSaveData")

    # DTO 之外，G8 也删除插件侧路径、类型身份和旧方法名。使用最终接口编译这些成员必须
    # 同时失败，证明 SDK 包没有保留任何可绕过宿主状态存储的入口。
    $legacySaveProject = Join-Path $temporaryRoot "LegacyDocumentSaveContract"
    New-Item -ItemType Directory -Path $legacySaveProject | Out-Null
    Set-Content -LiteralPath (Join-Path $legacySaveProject "LegacyDocumentSaveContract.csproj") -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="$sdkVersion" /></ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $legacySaveProject "LegacySaveConsumer.cs") -Encoding UTF8 -Value @'
using MyAvaloniaManagementCommon.Save;

public static class LegacySaveConsumer
{
    public static void UseRemovedMembers(ISavableDocument document)
    {
        _ = document.FilePath;
        _ = document.SaveDocumentTypeId;
        _ = document.CreateSaveDocumentMetaData("legacy.mamdoc");
        document.LoadDocumentByMetaData(null!);
    }
}
'@
    Invoke-DotNet @("restore", "LegacyDocumentSaveContract.csproj", "--configfile", $nugetConfig, "--packages", $isolatedPackageCache, "--nologo") $legacySaveProject
    Assert-DotNetBuildFails "LegacyDocumentSaveContract.csproj" $legacySaveProject @(
        "CS1061", "FilePath", "SaveDocumentTypeId", "CreateSaveDocumentMetaData", "LoadDocumentByMetaData")

    # G9 删除旧消息器、具体实现、处理委托和底层 Messenger 入口，不保留 Obsolete 适配层。
    $legacyEventBusProject = Join-Path $temporaryRoot "LegacyMessengerContract"
    New-Item -ItemType Directory -Path $legacyEventBusProject | Out-Null
    Set-Content -LiteralPath (Join-Path $legacyEventBusProject "LegacyMessengerContract.csproj") -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="$sdkVersion" /></ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $legacyEventBusProject "LegacyMessengerConsumer.cs") -Encoding UTF8 -Value @'
using MyAvaloniaManagementCommon.Message;

public static class LegacyMessengerConsumer
{
    public static object UseRemovedContract(IMessengerService service)
    {
        _ = service.Messenger;
        MessageHandler<object, object> handler = static (_, _) => { };
        return new MessengerService();
    }
}
'@
    Invoke-DotNet @("restore", "LegacyMessengerContract.csproj", "--configfile", $nugetConfig, "--packages", $isolatedPackageCache, "--nologo") $legacyEventBusProject
    Assert-DotNetBuildFails "LegacyMessengerContract.csproj" $legacyEventBusProject @(
        "CS0234", "Message")

    # G11 删除无来源初始化文本和 object 参数包。最终 SDK 只允许稳定类型身份、标题和
    # CreationIntent；旧成员必须在真实 nupkg 消费场景中产生明确编译错误。
    $removedCreationProject = Join-Path $temporaryRoot "G11RemovedCreationMembers"
    New-Item -ItemType Directory -Path $removedCreationProject | Out-Null
    Set-Content -LiteralPath (Join-Path $removedCreationProject "G11RemovedCreationMembers.csproj") -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="$sdkVersion" /></ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $removedCreationProject "RemovedCreationMembers.cs") -Encoding UTF8 -Value @'
using MyAvaloniaManagementCommon.DocumentCreation;

public static class RemovedCreationMembers
{
    public static DocumentCreationParams Create(DocumentTypeId id) => new(id)
    {
        InitializationData = "removed",
        AdditionalData = new object(),
    };
}
'@
    Invoke-DotNet @("restore", "G11RemovedCreationMembers.csproj", "--configfile", $nugetConfig, "--packages", $isolatedPackageCache, "--nologo") $removedCreationProject
    Assert-DotNetBuildFails "G11RemovedCreationMembers.csproj" $removedCreationProject @(
        "CS0117", "InitializationData", "AdditionalData")

    # G11 删除无生产实现的保存路径策略，不提供 Obsolete 别名或空接口。
    $removedTypesProject = Join-Path $temporaryRoot "G11RemovedSavePathPolicy"
    New-Item -ItemType Directory -Path $removedTypesProject | Out-Null
    Set-Content -LiteralPath (Join-Path $removedTypesProject "G11RemovedSavePathPolicy.csproj") -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="$sdkVersion" /></ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $removedTypesProject "RemovedSavePathPolicy.cs") -Encoding UTF8 -Value @'
using MyAvaloniaManagementCommon.Save;

public interface RemovedSavePathPolicy : IDocumentSavePathPolicy;
'@
    Invoke-DotNet @("restore", "G11RemovedSavePathPolicy.csproj", "--configfile", $nugetConfig, "--packages", $isolatedPackageCache, "--nologo") $removedTypesProject
    Assert-DotNetBuildFails "G11RemovedSavePathPolicy.csproj" $removedTypesProject @(
        "CS0246", "IDocumentSavePathPolicy")

    # 反射型公共 Behavior 也必须从最终包消失；播放器已改为插件 View 内的定向事件适配。
    $removedBehaviorProject = Join-Path $temporaryRoot "G11RemovedBehavior"
    New-Item -ItemType Directory -Path $removedBehaviorProject | Out-Null
    Set-Content -LiteralPath (Join-Path $removedBehaviorProject "G11RemovedBehavior.csproj") -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="$sdkVersion" /></ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $removedBehaviorProject "RemovedBehavior.cs") -Encoding UTF8 -Value @'
using MyAvaloniaManagementCommon.Behaviors;

public static class RemovedBehavior
{
    public static object Create() => new HandledEventsAwareBehavior();
}
'@
    Invoke-DotNet @("restore", "G11RemovedBehavior.csproj", "--configfile", $nugetConfig, "--packages", $isolatedPackageCache, "--nologo") $removedBehaviorProject
    Assert-DotNetBuildFails "G11RemovedBehavior.csproj" $removedBehaviorProject @(
        "CS0234", "Behaviors")

    $uiProject = Join-Path $temporaryRoot "UiPlugin"
    New-Item -ItemType Directory -Path $uiProject | Out-Null
    Set-Content -LiteralPath (Join-Path $uiProject "UiPlugin.csproj") -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="MyAvaloniaManagement.PluginSdk.UI" Version="$sdkVersion" /></ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $uiProject "ProfileView.axaml") -Encoding UTF8 -Value @'
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ursa="clr-namespace:Ursa.Controls;assembly=Ursa"
             xmlns:dock="clr-namespace:Dock.Avalonia.Controls;assembly=Dock.Avalonia"
             x:Class="UiPlugin.ProfileView">
  <StackPanel Background="{DynamicResource AppPanelBrush}">
    <ursa:IconButton />
    <dock:DockControl />
  </StackPanel>
</UserControl>
'@
    Set-Content -LiteralPath (Join-Path $uiProject "ProfileView.axaml.cs") -Encoding UTF8 -Value @'
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace UiPlugin;
public sealed partial class ProfileView : UserControl
{
    public ProfileView() => AvaloniaXamlLoader.Load(this);
}
'@
    Invoke-DotNet @("restore", "UiPlugin.csproj", "--configfile", $nugetConfig, "--packages", $isolatedPackageCache, "--nologo") $uiProject
    Invoke-DotNet @("build", "UiPlugin.csproj", "-c", "Release", "--no-restore", "--nologo") $uiProject

    Write-Host "Plugin SDK package acceptance passed. SDK=$sdkVersion; analyzer did not leak into nuspec; minimal creation/content/event samples compiled; removed G5/G8/G9/G11 contracts rejected."
}
finally {
    $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($temporaryRoot.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $temporaryRoot)) {
        # Windows 上 dotnet/MSBuild 偶尔会在子进程退出后短暂持有生成目录句柄。
        # 这里只对已验证位于系统临时根下的本次唯一目录做有界重试；
        # 若句柄持续不释放仍让门禁失败，避免静默遗留大量 SDK 消费制品。
        for ($attempt = 1; $attempt -le 10; $attempt++) {
            try {
                Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction Stop
                break
            }
            catch {
                if ($attempt -ge 10) {
                    throw
                }

                Start-Sleep -Milliseconds 250
            }
        }
    }
}
