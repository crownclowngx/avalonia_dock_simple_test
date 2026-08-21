param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryParent ('MyAvaloniaPluginSdkPackage-' + [Guid]::NewGuid().ToString('N'))
$packageOutput = Join-Path $temporaryRoot 'packages'
$isolatedPackageCache = Join-Path $temporaryRoot 'global-packages'
[xml]$versionDocument = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props')
$properties = $versionDocument.Project.PropertyGroup
$sdkVersion = [string]$properties.MyAvaloniaPluginSdkVersion

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-ChildPath {
    param([string]$ChildPath, [string]$ParentPath)
    $child = [IO.Path]::GetFullPath($ChildPath)
    $parent = [IO.Path]::GetFullPath($ParentPath).TrimEnd([IO.Path]::DirectorySeparatorChar)
    Assert-True ($child.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) `
        "路径 $child 不在允许目录 $parent 内。"
}

function Remove-TemporaryTree {
    param([Parameter(Mandatory)] [string]$Path)

    # dotnet 结束后，杀毒软件或构建服务可能仍短暂持有隔离 NuGet 缓存中的 DLL。这里仅对已经通过
    # Assert-ChildPath 证明位于系统临时目录内的本轮目录做有限重试；超过期限仍失败就保留错误，
    # 既不吞掉真实清理故障，也绝不把重试扩大到仓库、用户目录或其他任务的临时树。
    $lastError = $null
    foreach ($attempt in 1..8) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            $lastError = $_
            if ($attempt -lt 8) {
                Start-Sleep -Milliseconds 250
            }
        }
    }

    throw $lastError
}

function Invoke-DotNet {
    param([string[]]$Arguments, [string]$WorkingDirectory = $repositoryRoot)
    Push-Location $WorkingDirectory
    try {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') 失败，退出码 $LASTEXITCODE。" }
    }
    finally { Pop-Location }
}

function Assert-DotNetBuildFails {
    param([string]$ProjectPath, [string]$WorkingDirectory, [string[]]$ExpectedFragments)
    Push-Location $WorkingDirectory
    try {
        $output = @(& dotnet build $ProjectPath -c Release --no-restore --nologo 2>&1)
        Assert-True ($LASTEXITCODE -ne 0) "反向夹具意外编译成功。"
        $text = $output -join [Environment]::NewLine
        foreach ($fragment in $ExpectedFragments) {
            Assert-True ($text.Contains($fragment, [StringComparison]::Ordinal)) `
                "反向夹具失败信息缺少 $fragment。"
        }
    }
    finally { Pop-Location }
}

function Read-Nuspec {
    param([string]$PackagePath)
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entry = $archive.Entries | Where-Object FullName -Like '*.nuspec' | Select-Object -First 1
        Assert-True ($null -ne $entry) "包 $PackagePath 缺少 nuspec。"
        $reader = [IO.StreamReader]::new($entry.Open())
        try { return [xml]$reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
}

function Get-PackageEntries {
    param([string]$PackagePath)
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try { return @($archive.Entries | ForEach-Object FullName) }
    finally { $archive.Dispose() }
}

function New-ConsumerProject {
    param([string]$Name, [string]$PackageId, [string]$Source, [bool]$ExpectSuccess, [string[]]$ExpectedFragments = @())
    $directory = Join-Path $temporaryRoot $Name
    New-Item -ItemType Directory -Path $directory | Out-Null
    $projectPath = Join-Path $directory "$Name.csproj"
    [IO.File]::WriteAllText($projectPath, @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><PackageReference Include="$PackageId" Version="$sdkVersion" /></ItemGroup>
</Project>
"@, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $directory 'Consumer.cs'), $Source, [Text.UTF8Encoding]::new($false))
    Invoke-DotNet @(
        'restore', "$Name.csproj", '--configfile', $script:nugetConfig,
        '--packages', $isolatedPackageCache, '--nologo') $directory | Out-Host
    if ($ExpectSuccess) {
        Invoke-DotNet @('build', "$Name.csproj", '-c', 'Release', '--no-restore', '--nologo', '-warnaserror') $directory | Out-Host
    }
    else {
        Assert-DotNetBuildFails "$Name.csproj" $directory $ExpectedFragments
    }
    return $directory
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
Assert-ChildPath $temporaryRoot $temporaryParent
New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null

try {
    Invoke-DotNet @(
        'pack', 'Host/MyAvaloniaManagement.PluginSdk/MyAvaloniaManagement.PluginSdk.csproj',
        '-c', $Configuration, '--no-restore', '-o', $packageOutput, '--nologo')
    Invoke-DotNet @(
        'pack', 'Host/MyAvaloniaManagement.PluginSdk.UI/MyAvaloniaManagement.PluginSdk.UI.csproj',
        '-c', $Configuration, '--no-restore', '-o', $packageOutput, '--nologo')

    $corePackage = Join-Path $packageOutput "MyAvaloniaManagement.PluginSdk.$sdkVersion.nupkg"
    $uiPackage = Join-Path $packageOutput "MyAvaloniaManagement.PluginSdk.UI.$sdkVersion.nupkg"
    Assert-True (Test-Path -LiteralPath $corePackage) '未生成 Core SDK 包。'
    Assert-True (Test-Path -LiteralPath $uiPackage) '未生成 UI SDK 包。'

    $coreEntries = Get-PackageEntries $corePackage
    $uiEntries = Get-PackageEntries $uiPackage
    Assert-True ($coreEntries -contains 'lib/net10.0/MyAvaloniaManagement.PluginSdk.dll') 'Core 包缺少契约程序集。'
    Assert-True ($coreEntries -contains 'lib/net10.0/MyAvaloniaManagement.PluginSdk.xml') 'Core 包缺少中文 XML 文档。'
    Assert-True ($uiEntries -contains 'lib/net10.0/MyAvaloniaManagement.PluginSdk.UI.dll') 'UI 包缺少真实契约程序集。'
    Assert-True ($uiEntries -contains 'lib/net10.0/MyAvaloniaManagement.PluginSdk.UI.xml') 'UI 包缺少中文 XML 文档。'
    Assert-True ($coreEntries -contains 'README.md' -and $uiEntries -contains 'README.md') 'SDK 包缺少 README。'
    Assert-True (-not ($coreEntries + $uiEntries | Where-Object { $_ -like '*MyAvaloniaManagementCommon*' })) `
        '最终 SDK 包不得包含 Legacy Common。'
    Assert-True (-not ($coreEntries + $uiEntries | Where-Object { $_ -like '*MyAvaloniaManagement.dll' })) `
        '最终 SDK 包不得包含 Host 实现。'

    $coreNuspec = Read-Nuspec $corePackage
    $uiNuspec = Read-Nuspec $uiPackage
    $coreDependencies = @(
        $coreNuspec.package.metadata.dependencies.group.dependency |
            ForEach-Object id |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-True ($coreDependencies.Count -eq 0) "Core 包必须零 NuGet 依赖，实际为：$($coreDependencies -join ', ')"

    $uiDependencies = @{}
    foreach ($dependency in $uiNuspec.package.metadata.dependencies.group.dependency) {
        $uiDependencies[$dependency.id] = $dependency.version
    }
    $expectedUiIds = @(
        'Avalonia', 'Avalonia.Themes.Fluent', 'Irihi.Ursa', 'Irihi.Ursa.Themes.Semi',
        'Microsoft.Extensions.DependencyInjection.Abstractions', 'MyAvaloniaManagement.PluginSdk', 'Semi.Avalonia')
    Assert-True ($uiDependencies.Count -eq $expectedUiIds.Count) `
        "UI 包依赖数量异常：$($uiDependencies.Keys -join ', ')"
    foreach ($id in $expectedUiIds) { Assert-True ($uiDependencies.ContainsKey($id)) "UI 包缺少依赖 $id。" }
    Assert-True ($uiDependencies['MyAvaloniaManagement.PluginSdk'] -eq $sdkVersion) 'UI 包必须依赖同版本 Core。'
    $expectedExactVersions = @{
        'Avalonia' = "[$([string]$properties.MyAvaloniaAvaloniaUiVersion)]"
        'Avalonia.Themes.Fluent' = "[$([string]$properties.MyAvaloniaAvaloniaUiVersion)]"
        'Irihi.Ursa' = "[$([string]$properties.MyAvaloniaUrsaUiVersion)]"
        'Irihi.Ursa.Themes.Semi' = "[$([string]$properties.MyAvaloniaUrsaUiVersion)]"
        'Microsoft.Extensions.DependencyInjection.Abstractions' = "[$([string]$properties.MyAvaloniaDependencyInjectionUiVersion)]"
        'Semi.Avalonia' = "[$([string]$properties.MyAvaloniaSemiUiVersion)]"
    }
    foreach ($item in $expectedExactVersions.GetEnumerator()) {
        Assert-True ($uiDependencies[$item.Key] -eq $item.Value) `
            "UI 包 $($item.Key) 应为 $($item.Value)，实际为 $($uiDependencies[$item.Key])。"
    }
    Assert-True (-not ($uiDependencies.Keys | Where-Object { $_ -like 'Dock.*' -or $_ -eq 'Newtonsoft.Json' })) `
        'UI 包不得依赖 Dock 或 Newtonsoft。'

    $script:nugetConfig = Join-Path $temporaryRoot 'NuGet.Config'
    $escapedPackageOutput = [Security.SecurityElement]::Escape($packageOutput)
    [IO.File]::WriteAllText($script:nugetConfig, @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="g2-local" value="$escapedPackageOutput" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
"@, [Text.UTF8Encoding]::new($false))

    $coreConsumer = New-ConsumerProject 'CoreConsumer' 'MyAvaloniaManagement.PluginSdk' @'
using System.Text.Json;
using MyAvaloniaManagement.PluginSdk;

public sealed class SampleDocument : IPersistablePluginDocument
{
    public DocumentPresentationState Presentation { get; } = new("示例");
    public event EventHandler? PresentationChanged { add { } remove { } }
    public bool IsDirty => false;
    public ValueTask InitializeAsync(DocumentActivationContext context, CancellationToken token) => ValueTask.CompletedTask;
    public ValueTask<DocumentContent> CaptureContentAsync(CancellationToken token)
    {
        using var json = JsonDocument.Parse("{\"value\":1}");
        return ValueTask.FromResult(new DocumentContent(1, json.RootElement));
    }
    public void AcceptChanges() { }
}

public sealed class SampleLifecycle : IPluginLifecycle
{
    public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
    public Task ShutdownAsync(CancellationToken token) => Task.CompletedTask;
}

public sealed record SampleEvent(string Value);
public sealed class EventConsumer(IHostEventBus bus) : IDisposable
{
    private readonly IDisposable subscription = bus.Subscribe<SampleEvent>(_ => { });
    public void Publish() => bus.Publish(new SampleEvent("ok"));
    public void Dispose() => subscription.Dispose();
}
'@ $true
    $coreAssets = Get-Content -Raw -LiteralPath (Join-Path $coreConsumer 'obj/project.assets.json')
    foreach ($forbidden in @('Avalonia', 'Dock.', 'Newtonsoft.Json', 'Microsoft.Extensions.DependencyInjection')) {
        Assert-True (-not $coreAssets.Contains('"' + $forbidden, [StringComparison]::Ordinal)) `
            "Core 消费者还原图错误包含 $forbidden。"
    }

    New-ConsumerProject 'UiConsumer' 'MyAvaloniaManagement.PluginSdk.UI' @'
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

public sealed class SampleModule : IPluginModule
{
    public void Configure(IPluginRegistration registration)
    {
        registration.Services.AddSingleton<SampleTool>();
        registration.UseLifecycle<SampleLifecycle>();
        registration.AddPersistableDocument<SampleDocument, SampleView>(new(
            new DocumentTypeId("myavalonia.plugin.sample.document.main"), "示例", "说明", "示例"));
        registration.AddTool<SampleTool, SampleView>(new(
            new ToolTypeId("myavalonia.plugin.sample.tool.main"), "工具", "说明",
            ToolDockSide.Right, ToolCloseBehavior.Hide));
    }
}
public sealed class SampleView : UserControl;
public sealed class SampleTool;
public sealed class SampleLifecycle : IPluginLifecycle
{
    public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
    public Task ShutdownAsync(CancellationToken token) => Task.CompletedTask;
}
public sealed class SampleDocument : IPersistablePluginDocument
{
    public DocumentPresentationState Presentation { get; } = new("示例");
    public event EventHandler? PresentationChanged { add { } remove { } }
    public bool IsDirty => false;
    public ValueTask InitializeAsync(DocumentActivationContext context, CancellationToken token) => ValueTask.CompletedTask;
    public ValueTask<DocumentContent> CaptureContentAsync(CancellationToken token) => throw new NotSupportedException();
    public void AcceptChanges() { }
}
'@ $true | Out-Null

    New-ConsumerProject 'CoreAvaloniaNegative' 'MyAvaloniaManagement.PluginSdk' `
        "using Avalonia.Controls; public sealed class Removed : Control;" $false @('Avalonia') | Out-Null
    New-ConsumerProject 'CoreDiNegative' 'MyAvaloniaManagement.PluginSdk' `
        "using Microsoft.Extensions.DependencyInjection; public sealed class Removed(IServiceCollection services) { }" $false @('IServiceCollection') | Out-Null
    New-ConsumerProject 'CoreDockNegative' 'MyAvaloniaManagement.PluginSdk' `
        "using Dock.Model.Core; public sealed class Removed(IDock dock) { }" $false @('Dock') | Out-Null
    New-ConsumerProject 'CoreNewtonsoftNegative' 'MyAvaloniaManagement.PluginSdk' `
        "using Newtonsoft.Json; public sealed class Removed(JsonSerializer serializer) { }" $false @('Newtonsoft') | Out-Null
    New-ConsumerProject 'UiDockNegative' 'MyAvaloniaManagement.PluginSdk.UI' `
        "using Dock.Model.Core; public sealed class Removed(IDock dock) { }" $false @('Dock') | Out-Null
    New-ConsumerProject 'UiNewtonsoftNegative' 'MyAvaloniaManagement.PluginSdk.UI' `
        "using Newtonsoft.Json; public sealed class Removed(JsonSerializer serializer) { }" $false @('Newtonsoft') | Out-Null
    New-ConsumerProject 'LegacyNamespaceNegative' 'MyAvaloniaManagement.PluginSdk.UI' @'
using MyAvaloniaManagementCommon.Plugin;
public sealed class Removed(IPluginRegistrationContext context) { }
'@ $false @('MyAvaloniaManagementCommon') | Out-Null
    New-ConsumerProject 'RemovedContributionNegative' 'MyAvaloniaManagement.PluginSdk.UI' @'
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
public static class Removed
{
    public static void Use(IPluginRegistration registration)
    {
        registration.AddView<object, Avalonia.Controls.UserControl>();
        _ = typeof(IDocumentCreationStrategy);
        _ = typeof(IToolCreationStrategy);
    }
}
'@ $false @('AddView', 'IDocumentCreationStrategy', 'IToolCreationStrategy') | Out-Null
    New-ConsumerProject 'RemovedLifecycleNegative' 'MyAvaloniaManagement.PluginSdk' @'
using MyAvaloniaManagement.PluginSdk;
public static class Removed
{
    public static object Create() => new PluginLifecycleManager();
}
'@ $false @('PluginLifecycleManager') | Out-Null
    New-ConsumerProject 'RemovedPersistenceNegative' 'MyAvaloniaManagement.PluginSdk' @'
using MyAvaloniaManagement.PluginSdk;
public static class Removed
{
    public static object Snapshot() => new DocumentContentSnapshot(1, "{}");
    public static Type Converter() => typeof(DocumentTypeIdSystemTextJsonConverter);
}
'@ $false @('DocumentContentSnapshot', 'DocumentTypeIdSystemTextJsonConverter') | Out-Null

    Write-Host '[G2 SDK Package] 通过：Core/UI 内容、依赖白名单、两个正例和十个反向消费夹具符合预期。'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Assert-ChildPath $temporaryRoot $temporaryParent
        Remove-TemporaryTree -Path $temporaryRoot
    }
}
