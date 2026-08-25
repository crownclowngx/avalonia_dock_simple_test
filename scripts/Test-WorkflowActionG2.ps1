[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = Join-Path $repositoryRoot 'artifacts\test-results\WorkflowActionG2'
$candidateFeed = Join-Path $resultRoot 'candidate-feed'
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryParent ("myavalonia-workflow-g2-" + [Guid]::NewGuid().ToString('N'))
$isolatedPackages = Join-Path $temporaryRoot 'packages'
$templateHive = Join-Path $temporaryRoot 'template-hive'
$oldTemplateHive = Join-Path $temporaryRoot 'old-template-hive'
$generatedRoot = Join-Path $temporaryRoot 'generated'
$hostPackageRoot = Join-Path $temporaryRoot 'host-package'
$templateProject = Join-Path $repositoryRoot (
    'Packaging\MyAvaloniaManagement.Plugin.Templates\MyAvaloniaManagement.Plugin.Templates.csproj')
$templateContentRoot = Join-Path $repositoryRoot (
    'Packaging\MyAvaloniaManagement.Plugin.Templates\content\myavalonia-plugin')
$fixedPackageTimestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-ChildPath {
    param([string]$ChildPath, [string]$ParentPath, [string]$Description)
    $child = [IO.Path]::GetFullPath($ChildPath)
    $parent = [IO.Path]::GetFullPath($ParentPath).TrimEnd(
        [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    Assert-True ($child.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase)) `
        "$Description 路径越界：$child；允许根：$parent"
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)] [string[]]$Arguments,
        [string]$WorkingDirectory = $repositoryRoot
    )
    Push-Location $WorkingDirectory
    try {
        & dotnet @Arguments | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
        }
    }
    finally { Pop-Location }
}

function Invoke-Pwsh {
    param([Parameter(Mandatory)] [string]$Script, [string[]]$Arguments = @())
    & pwsh -NoProfile -File $Script @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$Script 失败，退出码：$LASTEXITCODE。"
    }
}

function Get-TrxPassed {
    param([Parameter(Mandatory)] [string]$Path)
    [xml]$trx = Get-Content -LiteralPath $Path
    $counters = $trx.TestRun.ResultSummary.Counters
    Assert-True (
        [int]$counters.failed -eq 0 -and
        [int]$counters.notExecuted -eq 0 -and
        [int]$counters.executed -eq [int]$counters.passed) `
        "TRX 未做到全部执行、零失败、零跳过：$Path"
    return [int]$counters.passed
}

function Get-Sha512Base64 {
    param([Parameter(Mandatory)] [string]$Path)
    $algorithm = [Security.Cryptography.SHA512]::Create()
    try {
        return [Convert]::ToBase64String(
            $algorithm.ComputeHash([IO.File]::ReadAllBytes($Path)))
    }
    finally { $algorithm.Dispose() }
}

function Read-ZipEntries {
    param([Parameter(Mandatory)] [string]$Path)
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try { return @($archive.Entries | ForEach-Object FullName) }
    finally { $archive.Dispose() }
}

function Read-ZipText {
    param([Parameter(Mandatory)] [string]$Path, [Parameter(Mandatory)] [string]$EntryName)
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry($EntryName)
        Assert-True ($null -ne $entry) "$Path 缺少 ZIP 条目 $EntryName。"
        $reader = [IO.StreamReader]::new($entry.Open(), [Text.Encoding]::UTF8, $true)
        try { return $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
}

function ConvertTo-DeterministicNupkg {
    param([Parameter(Mandatory)] [string]$Path)

    # .NET 10 所带 NuGet.Packaging 会为 OPC 核心属性创建随机路径和关系 ID，并把打包时刻写入
    # ZIP 条目。候选 SDK 尚未发布，模板却必须携带可重复的 lock file；因此这里只规范化本轮
    # 临时候选包的容器元数据。程序集、XML 文档、README 和 nuspec 字节保持原样。
    $source = [IO.Compression.ZipFile]::OpenRead($Path)
    $payload = [Collections.Generic.List[object]]::new()
    try {
        foreach ($entry in $source.Entries) {
            $stream = [IO.MemoryStream]::new()
            $input = $entry.Open()
            try { $input.CopyTo($stream) }
            finally { $input.Dispose() }
            $payload.Add([pscustomobject]@{
                    Name = $entry.FullName
                    Bytes = $stream.ToArray()
                })
            $stream.Dispose()
        }
    }
    finally { $source.Dispose() }

    $coreEntry = @($payload | Where-Object Name -Like (
            'package/services/metadata/core-properties/*.psmdcp'))
    Assert-True ($coreEntry.Count -eq 1) "$Path 的 OPC 核心属性条目数量不是 1。"
    $canonicalCorePath = 'package/services/metadata/core-properties/core-properties.psmdcp'
    $coreEntry[0].Name = $canonicalCorePath

    $relationships = @($payload | Where-Object Name -CEQ '_rels/.rels')
    Assert-True ($relationships.Count -eq 1) "$Path 缺少唯一 OPC 关系文件。"
    [xml]$relationshipDocument = [Text.Encoding]::UTF8.GetString($relationships[0].Bytes)
    $namespace = [Xml.XmlNamespaceManager]::new($relationshipDocument.NameTable)
    $namespace.AddNamespace('r', 'http://schemas.openxmlformats.org/package/2006/relationships')
    $manifestRelationship = $relationshipDocument.SelectSingleNode(
        "/r:Relationships/r:Relationship[contains(@Type, '/manifest')]", $namespace)
    $coreRelationship = $relationshipDocument.SelectSingleNode(
        "/r:Relationships/r:Relationship[contains(@Type, '/metadata/core-properties')]", $namespace)
    Assert-True ($null -ne $manifestRelationship -and $null -ne $coreRelationship) `
        "$Path 的 OPC 关系不完整。"
    $manifestRelationship.SetAttribute('Id', 'RManifest')
    $coreRelationship.SetAttribute('Id', 'RCoreProperties')
    $coreRelationship.SetAttribute('Target', '/' + $canonicalCorePath)
    $settings = [Xml.XmlWriterSettings]::new()
    $settings.Encoding = [Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $relationshipStream = [IO.MemoryStream]::new()
    $writer = [Xml.XmlWriter]::Create($relationshipStream, $settings)
    try { $relationshipDocument.Save($writer) }
    finally { $writer.Dispose() }
    $relationships[0].Bytes = $relationshipStream.ToArray()
    $relationshipStream.Dispose()

    $normalizedPath = $Path + '.normalized'
    $output = [IO.File]::Open(
        $normalizedPath, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $archive = [IO.Compression.ZipArchive]::new(
        $output, [IO.Compression.ZipArchiveMode]::Create, $false, [Text.Encoding]::UTF8)
    try {
        foreach ($item in @($payload | Sort-Object Name)) {
            $entry = $archive.CreateEntry($item.Name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixedPackageTimestamp
            $entryStream = $entry.Open()
            try { $entryStream.Write($item.Bytes, 0, $item.Bytes.Length) }
            finally { $entryStream.Dispose() }
        }
    }
    finally {
        $archive.Dispose()
        $output.Dispose()
    }
    Move-Item -LiteralPath $normalizedPath -Destination $Path -Force
}

function Write-NuGetConfig {
    param([Parameter(Mandatory)] [string]$Root)
    $path = Join-Path $Root 'NuGet.Config'
    $escapedFeed = [Security.SecurityElement]::Escape($candidateFeed)
    $text = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="g2-local" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="g2-local">
      <package pattern="MyAvaloniaManagement.PluginSdk" />
      <package pattern="MyAvaloniaManagement.PluginSdk.UI" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="Avalonia*" />
      <package pattern="CommunityToolkit.*" />
      <package pattern="HarfBuzzSharp*" />
      <package pattern="Irihi.*" />
      <package pattern="MicroCom.*" />
      <package pattern="Microsoft.*" />
      <package pattern="MyAvaloniaManagement.Plugin.Build" />
      <package pattern="Semi.*" />
      <package pattern="SkiaSharp*" />
      <package pattern="Tmds.*" />
      <package pattern="xunit*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
    return $path
}

function New-TemplateProject {
    param([Parameter(Mandatory)] [string]$Name, [Parameter(Mandatory)] [string]$PluginId)
    $root = Join-Path $generatedRoot $Name.Replace('.', '-')
    Invoke-DotNet @(
        'new', 'myavalonia-plugin', '-n', $Name, '--plugin-id', $PluginId,
        '-o', $root, '--debug:custom-hive', $templateHive, '--no-update-check')
    $config = Write-NuGetConfig $root
    return [pscustomobject]@{ Name = $Name; PluginId = $PluginId; Root = $root; Config = $config }
}

function Assert-GeneratedBoundary {
    param([Parameter(Mandatory)] $Project)
    $files = @(Get-ChildItem -LiteralPath $Project.Root -Recurse -File |
        Where-Object {
            $_.Name -ne 'NuGet.Config' -and
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        })
    $text = $files | Where-Object Extension -In @('.cs', '.csproj', '.props', '.targets', '.md', '.json') |
        ForEach-Object { [IO.File]::ReadAllText($_.FullName) }
    $combined = $text -join "`n"
    Assert-True (-not $combined.Contains($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) `
        "$($Project.Name) 生成物泄漏仓库绝对路径。"
    Assert-True ($combined -notmatch '<ProjectReference[^>]+Host[\\/]') `
        "$($Project.Name) 生成物不得引用 Host 源码项目。"
    $locks = @(Get-ChildItem -LiteralPath $Project.Root -Filter packages.lock.json -Recurse -File)
    Assert-True ($locks.Count -eq 3) "$($Project.Name) 必须生成 Plugin/Standalone/Tests 三份 lock file。"
}

function Assert-TemplateLocks {
    param([Parameter(Mandatory)] [string]$CoreHash, [Parameter(Mandatory)] [string]$UiHash)
    $locks = @(Get-ChildItem -LiteralPath $templateContentRoot -Filter packages.lock.json -Recurse -File)
    Assert-True ($locks.Count -eq 3) '模板源码必须提交三份项目 lock file。'
    foreach ($lock in $locks) {
        $document = Get-Content -Raw -LiteralPath $lock.FullName | ConvertFrom-Json
        $dependencies = $document.dependencies.'net10.0'
        if ($dependencies.PSObject.Properties.Name -contains 'MyAvaloniaManagement.PluginSdk') {
            Assert-True (
                $dependencies.'MyAvaloniaManagement.PluginSdk'.resolved -ceq '3.1.0' -and
                $dependencies.'MyAvaloniaManagement.PluginSdk'.contentHash -ceq $CoreHash) `
                "$($lock.FullName) 的 Core SDK 锁定事实与规范候选包不一致。"
        }
        if ($dependencies.PSObject.Properties.Name -contains 'MyAvaloniaManagement.PluginSdk.UI') {
            Assert-True (
                $dependencies.'MyAvaloniaManagement.PluginSdk.UI'.resolved -ceq '3.1.0' -and
                $dependencies.'MyAvaloniaManagement.PluginSdk.UI'.contentHash -ceq $UiHash) `
                "$($lock.FullName) 的 UI SDK 锁定事实与规范候选包不一致。"
        }
        if ($dependencies.PSObject.Properties.Name -contains 'MyAvaloniaManagement.Plugin.Build') {
            Assert-True (
                $dependencies.'MyAvaloniaManagement.Plugin.Build'.resolved -ceq '1.1.2') `
                "$($lock.FullName) 必须继续精确锁定已发布 Build 1.1.2。"
        }
    }
}

function Invoke-GeneratedBuildAndTest {
    param([Parameter(Mandatory)] $Project)
    $solution = Join-Path $Project.Root "$($Project.Name).slnx"
    Invoke-DotNet @(
        'restore', $solution, '--locked-mode', '--configfile', $Project.Config,
        '--packages', $isolatedPackages, '--nologo') $Project.Root
    Invoke-DotNet @(
        'build', $solution, '-c', $Configuration, '--no-restore', '--nologo',
        '-warnaserror', '-p:SkipPluginDeploy=true') $Project.Root
    Invoke-DotNet @(
        'test', $solution, '-c', $Configuration, '--no-build', '--no-restore', '--nologo') `
        $Project.Root
}

function Write-ProviderProbe {
    param([Parameter(Mandatory)] $Project)
    $modulePath = Join-Path $Project.Root (
        'src\WorkflowProviderProbe.Plugin\Plugin\WorkflowProviderProbeModule.cs')
    $handlerPath = Join-Path $Project.Root (
        'src\WorkflowProviderProbe.Plugin\Plugin\EchoHandler.cs')
    $module = @'
using System.Text.Json;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using WorkflowProviderProbe.Constants;
using WorkflowProviderProbe.Features.Main;

namespace WorkflowProviderProbe.Plugin;

/// <summary>G2 外部模板 Provider 探针；只声明无风险回显动作和模板原有 Document。</summary>
public sealed class WorkflowProviderProbeModule : IPluginModule
{
    /// <inheritdoc />
    public void Configure(IPluginRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Services.AddWorkflowProviderProbeServices();
        registration.AddDocument<MainDocument, MainView>(new DocumentDescriptor(
            PluginIds.MainDocument, "Provider 探针", "验证模板默认贡献仍可用。", "G2"));

        using var input = JsonDocument.Parse("""
            {"type":"object","properties":{"value":{"type":"string","maxLength":128}},"required":["value"],"additionalProperties":false}
            """);
        using var output = JsonDocument.Parse("""
            {"type":"object","properties":{"echoed":{"type":"string","maxLength":128},"caller":{"type":"string","maxLength":128}},"required":["echoed","caller"],"additionalProperties":false}
            """);
        registration.AddWorkflowAction<EchoHandler>(new WorkflowActionDescriptor(
            new WorkflowActionId("myavalonia.plugin.workflow-g2-provider.workflow.echo"),
            "G2 回显", "验证外部 NuGet、双 ALC 和调用 Scope。",
            input.RootElement, output.RootElement,
            WorkflowActionRiskFlags.None, WorkflowActionConfirmationPolicy.Never));
    }
}
'@
    $handler = @'
using System.Text.Json;
using MyAvaloniaManagement.PluginSdk;

namespace WorkflowProviderProbe.Plugin;

/// <summary>记录活动/释放计数的 scoped Handler；计数仅供 G2 外部包测试通过反射读取。</summary>
public sealed class EchoHandler : IWorkflowActionHandler, IAsyncDisposable
{
    private static int _activeInstances;
    private static int _createdInstances;
    private static int _disposedInstances;

    /// <summary>获取当前仍由 invocation scope 拥有的实例数。</summary>
    public static int ActiveInstances => Volatile.Read(ref _activeInstances);

    /// <summary>获取累计创建数，用于和累计释放数核对所有权是否闭合。</summary>
    public static int CreatedInstances => Volatile.Read(ref _createdInstances);

    /// <summary>获取已经异步释放的实例数。</summary>
    public static int DisposedInstances => Volatile.Read(ref _disposedInstances);

    /// <summary>创建本次调用独占的 Handler。</summary>
    public EchoHandler()
    {
        Interlocked.Increment(ref _createdInstances);
        Interlocked.Increment(ref _activeInstances);
    }

    /// <inheritdoc />
    public ValueTask<JsonElement> InvokeAsync(
        JsonElement arguments,
        WorkflowActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new
        {
            echoed = arguments.GetProperty("value").GetString(),
            caller = context.CallerId.Value,
        }));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Interlocked.Decrement(ref _activeInstances);
        Interlocked.Increment(ref _disposedInstances);
        return ValueTask.CompletedTask;
    }
}
'@
    [IO.File]::WriteAllText($modulePath, $module, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($handlerPath, $handler, [Text.UTF8Encoding]::new($false))
}

function Write-ConsumerProbe {
    param([Parameter(Mandatory)] $Project)
    $modulePath = Join-Path $Project.Root (
        'src\WorkflowConsumerProbe.Plugin\Plugin\WorkflowConsumerProbeModule.cs')
    $module = @'
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using WorkflowConsumerProbe.Constants;
using WorkflowConsumerProbe.Features.Main;

namespace WorkflowConsumerProbe.Plugin;

/// <summary>G2 外部模板 Consumer 探针；只请求 caller-bound Gateway，不引用 Provider 程序集。</summary>
public sealed class WorkflowConsumerProbeModule : IPluginModule
{
    /// <inheritdoc />
    public void Configure(IPluginRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Services.AddWorkflowConsumerProbeServices();
        registration.AddDocument<MainDocument, MainView>(new DocumentDescriptor(
            PluginIds.MainDocument, "Consumer 探针", "验证模板默认贡献仍可用。", "G2"));
        registration.UseWorkflowActionGateway();
    }
}
'@
    [IO.File]::WriteAllText($modulePath, $module, [Text.UTF8Encoding]::new($false))
}

function Test-StandaloneStartup {
    param([Parameter(Mandatory)] $Project)
    $standaloneProject = Join-Path $Project.Root (
        "src\$($Project.Name).Standalone\$($Project.Name).Standalone.csproj")
    $process = Start-Process -FilePath 'dotnet' -WindowStyle Hidden -PassThru `
        -WorkingDirectory $Project.Root -ArgumentList @(
            'run', '--project', $standaloneProject, '-c', $Configuration,
            '--no-build', '--no-restore')
    try {
        # 不用 Sleep 猜启动顺序；有界 WaitForExit 只判断进程是否在窗口创建阶段立即崩溃。
        # 三秒后仍存活即完成本地启动检查，随后只终止本轮临时 Standalone 进程树。
        Assert-True (-not $process.WaitForExit(3000)) `
            "Standalone 启动后立即退出，退出码：$($process.ExitCode)。"
    }
    finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            [void]$process.WaitForExit(10000)
        }
        $process.Dispose()
    }
}

function Build-PluginPackageTwice {
    param([Parameter(Mandatory)] $Project)
    $pluginProject = Join-Path $Project.Root (
        "src\$($Project.Name).Plugin\$($Project.Name).Plugin.csproj")
    $runs = @()
    foreach ($run in 1..2) {
        $output = Join-Path $resultRoot "external-packages\$($Project.Name)\run$run"
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        Invoke-DotNet @(
            'msbuild', $pluginProject, '-t:BuildManagedPluginPackage',
            "-p:Configuration=$Configuration", "-p:ManagedPluginPackageOutput=$output",
            '--nologo') $Project.Root
        $zip = @(Get-ChildItem -LiteralPath $output -Filter '*.zip' -File)
        $manifest = @(Get-ChildItem -LiteralPath $output -Filter '*.manifest.json' -File)
        Assert-True ($zip.Count -eq 1 -and $manifest.Count -eq 1) `
            "$($Project.Name) 第 $run 轮没有生成唯一 ZIP/manifest。"
        $runs += [pscustomobject]@{
            Zip = $zip[0].FullName
            Manifest = $manifest[0].FullName
            ZipHash = (Get-FileHash $zip[0].FullName -Algorithm SHA256).Hash
            ManifestHash = (Get-FileHash $manifest[0].FullName -Algorithm SHA256).Hash
        }
    }
    Assert-True ($runs[0].ZipHash -ceq $runs[1].ZipHash) `
        "$($Project.Name) 两轮 ZIP 不确定。"
    Assert-True ($runs[0].ManifestHash -ceq $runs[1].ManifestHash) `
        "$($Project.Name) 两轮外置 manifest 不确定。"

    $extractRoot = Join-Path $hostPackageRoot $Project.Name
    Expand-Archive -LiteralPath $runs[0].Zip -DestinationPath $extractRoot -Force
    $pluginManifest = @(Get-ChildItem -LiteralPath $extractRoot -Filter plugin.manifest.json -Recurse -File)
    Assert-True ($pluginManifest.Count -eq 1) "$($Project.Name) ZIP 缺少唯一 plugin.manifest.json。"
    $manifestObject = Get-Content -Raw -LiteralPath $pluginManifest[0].FullName | ConvertFrom-Json
    Assert-True (
        [int]$manifestObject.schemaVersion -eq 2 -and
        [string]$manifestObject.sdk.minInclusive -ceq '3.1.0' -and
        [string]$manifestObject.sdk.maxExclusive -ceq '4.0.0') `
        "$($Project.Name) manifest schema 或 SDK 区间错误。"
    return [pscustomobject]@{
        Project = $Project.Name
        PluginId = $Project.PluginId
        ZipSha256 = $runs[0].ZipHash
        ManifestSha256 = $runs[0].ManifestHash
        Files = (Read-ZipEntries $runs[0].Zip).Count
        ExtractRoot = $extractRoot
    }
}

function Test-LegacyTemplateNegative {
    $legacyRoot = Join-Path $generatedRoot 'LegacyTemplate'
    Invoke-DotNet @(
        'new', 'install', 'MyAvaloniaManagement.Plugin.Templates::1.0.4',
        '--debug:custom-hive', $oldTemplateHive, '--force')
    Invoke-DotNet @(
        'new', 'myavalonia-plugin', '-n', 'LegacyProbe',
        '--plugin-id', 'myavalonia.plugin.legacy-probe', '-o', $legacyRoot,
        '--debug:custom-hive', $oldTemplateHive, '--no-update-check')
    $versions = [IO.File]::ReadAllText((Join-Path $legacyRoot 'Directory.Packages.props'))
    Assert-True ($versions.Contains(
            'MyAvaloniaManagement.PluginSdk" Version="[3.0.0]"',
            [StringComparison]::Ordinal)) `
        '公开模板 1.0.4 不再精确引用 SDK 3.0.0。'

    $source = Join-Path $legacyRoot 'src\LegacyProbe.Plugin\WorkflowActionNegative.cs'
    [IO.File]::WriteAllText($source, @'
using System.Text.Json;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

public sealed class MissingHandler : IWorkflowActionHandler
{
    public ValueTask<JsonElement> InvokeAsync(
        JsonElement arguments,
        WorkflowActionContext context,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

public static class MissingRegistration
{
    public static void Register(IPluginRegistration registration)
    {
        registration.UseWorkflowActionGateway();
        registration.AddWorkflowAction<MissingHandler>(null!);
    }
}
'@, [Text.UTF8Encoding]::new($false))
    Invoke-DotNet @('restore', 'LegacyProbe.slnx', '--nologo') $legacyRoot
    Push-Location $legacyRoot
    try {
        $output = @(& dotnet build LegacyProbe.slnx -c $Configuration --no-restore --nologo 2>&1)
        Assert-True ($LASTEXITCODE -ne 0) '公开模板 1.0.4 + SDK 3.0.0 意外编译了 Workflow Action。'
        $joined = $output -join [Environment]::NewLine
        # 编译器不保证在类型声明已经失败后继续绑定方法体里的每一个扩展方法。
        # 因此强制核对两个最先出现的 3.1 类型缺失即可；上一步 restore 已单独成功，
        # 这足以证明失败来自公开 SDK 3.0 的 API 边界，而不是 NuGet 源或网络故障。
        foreach ($symbol in @('IWorkflowActionHandler', 'WorkflowActionContext')) {
            Assert-True ($joined.Contains($symbol, [StringComparison]::Ordinal)) `
                "旧模板负例缺少预期符号诊断：$symbol"
        }
    }
    finally { Pop-Location }
}

Assert-ChildPath $resultRoot $repositoryRoot 'G2 结果'
Assert-ChildPath $temporaryRoot $temporaryParent 'G2 临时树'
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $candidateFeed -Force | Out-Null
New-Item -ItemType Directory -Path $generatedRoot -Force | Out-Null
New-Item -ItemType Directory -Path $hostPackageRoot -Force | Out-Null
$originalPackages = [Environment]::GetEnvironmentVariable('NUGET_PACKAGES', 'Process')
$originalExternalRoot = [Environment]::GetEnvironmentVariable(
    'MYAVALONIA_WORKFLOW_G2_PLUGIN_ROOT', 'Process')

try {
    $env:NUGET_PACKAGES = $isolatedPackages

    # G1 是当前生产 API 与 Host 行为前置；Release 只表示本地优化配置。
    Invoke-Pwsh (Join-Path $PSScriptRoot 'Test-WorkflowActionG1.ps1') `
        @('-Configuration', $Configuration)
    Invoke-Pwsh (Join-Path $PSScriptRoot 'Test-ManagedPluginPackages.ps1') `
        @('-Configuration', $Configuration, '-ResultsDirectory',
            'artifacts/test-results/WorkflowActionG2/ManagedPluginPackages')

    foreach ($project in @(
            'Host/MyAvaloniaManagement.PluginSdk/MyAvaloniaManagement.PluginSdk.csproj',
            'Host/MyAvaloniaManagement.PluginSdk.UI/MyAvaloniaManagement.PluginSdk.UI.csproj')) {
        Invoke-DotNet @(
            'pack', $project, '-c', $Configuration, '--no-restore', '--nologo',
            '-o', $candidateFeed)
    }
    $corePackage = Join-Path $candidateFeed 'MyAvaloniaManagement.PluginSdk.3.1.0.nupkg'
    $uiPackage = Join-Path $candidateFeed 'MyAvaloniaManagement.PluginSdk.UI.3.1.0.nupkg'
    ConvertTo-DeterministicNupkg $corePackage
    ConvertTo-DeterministicNupkg $uiPackage
    $coreLockHash = Get-Sha512Base64 $corePackage
    $uiLockHash = Get-Sha512Base64 $uiPackage
    Assert-TemplateLocks $coreLockHash $uiLockHash

    Invoke-DotNet @(
        'pack', $templateProject, '-c', $Configuration, '--no-restore', '--nologo',
        '-o', $candidateFeed)
    $templatePackage = Join-Path $candidateFeed (
        'MyAvaloniaManagement.Plugin.Templates.1.1.0.nupkg')
    ConvertTo-DeterministicNupkg $templatePackage

    $templateEntries = Read-ZipEntries $templatePackage
    Assert-True (-not ($templateEntries | Where-Object {
                $_ -like 'lib/*' -or $_ -like 'ref/*' })) `
        'Template 包不得包含 lib/ref 程序集。'
    Assert-True (@($templateEntries | Where-Object { $_ -like 'content/*/packages.lock.json' }).Count -eq 3) `
        'Template 包必须携带三个生成项目 lock file。'
    Assert-True (-not ($templateEntries | Where-Object {
                $_ -like '*Host/MyAvaloniaManagement*' -or $_ -like '*MyAvaloniaManagement.csproj*' })) `
        'Template 包不得携带 Host 源码。'
    [xml]$templateNuspec = Read-ZipText $templatePackage (
        'MyAvaloniaManagement.Plugin.Templates.nuspec')
    Assert-True (
        [string]$templateNuspec.package.metadata.packageTypes.packageType.name -ceq 'Template') `
        'Template nupkg 的 PackageType 不是 Template。'

    Invoke-DotNet @(
        'new', 'install', $templatePackage, '--debug:custom-hive', $templateHive, '--force')
    $neutral = New-TemplateProject 'WorkflowStudio' 'myavalonia.plugin.workflow-studio'
    $dotted = New-TemplateProject 'MyAvalonia.WorkflowStudio' (
        'myavalonia.plugin.workflow-studio-dotted')
    $provider = New-TemplateProject 'WorkflowProviderProbe' (
        'myavalonia.plugin.workflow-g2-provider')
    $consumer = New-TemplateProject 'WorkflowConsumerProbe' (
        'myavalonia.plugin.workflow-g2-consumer')
    Write-ProviderProbe $provider
    Write-ConsumerProbe $consumer

    foreach ($project in @($neutral, $dotted, $provider, $consumer)) {
        Assert-GeneratedBoundary $project
        Invoke-GeneratedBuildAndTest $project
    }
    $dottedProjectText = [IO.File]::ReadAllText((Join-Path $dotted.Root (
            'src\MyAvalonia.WorkflowStudio.Plugin\MyAvalonia.WorkflowStudio.Plugin.csproj')))
    Assert-True ($dottedProjectText.Contains(
            '<ManagedPluginEntryType>MyAvalonia.WorkflowStudio.Plugin.WorkflowStudioModule</ManagedPluginEntryType>',
            [StringComparison]::Ordinal)) `
        '点号名称没有生成合法的末段类型名。'
    Test-StandaloneStartup $neutral

    $providerPackage = Build-PluginPackageTwice $provider
    $consumerPackage = Build-PluginPackageTwice $consumer
    $combinedControls = Join-Path $hostPackageRoot 'combined\Controls'
    New-Item -ItemType Directory -Path $combinedControls -Force | Out-Null
    foreach ($package in @($providerPackage, $consumerPackage)) {
        $sourceControls = Join-Path $package.ExtractRoot 'Controls'
        Copy-Item -LiteralPath (Get-ChildItem -LiteralPath $sourceControls -Directory).FullName `
            -Destination $combinedControls -Recurse
    }

    $env:MYAVALONIA_WORKFLOW_G2_PLUGIN_ROOT = $combinedControls
    $hostTestRoot = Join-Path $resultRoot 'ExternalHost'
    New-Item -ItemType Directory -Path $hostTestRoot -Force | Out-Null
    Invoke-DotNet @(
        'test', 'Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj',
        '-c', $Configuration, '--no-restore', '--nologo', '-warnaserror',
        '-p:SkipPluginDeploy=true',
        '--filter', 'FullyQualifiedName~WorkflowActionG2ExternalPackageTests',
        '--results-directory', $hostTestRoot,
        '--logger', 'trx;LogFileName=WorkflowActionG2ExternalPackageTests.trx')
    $externalHostPassed = Get-TrxPassed (
        Join-Path $hostTestRoot 'WorkflowActionG2ExternalPackageTests.trx')

    Test-LegacyTemplateNegative
    Invoke-Pwsh (Join-Path $PSScriptRoot 'Test-Documentation.ps1')

    $g1Summary = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot 'artifacts\test-results\WorkflowActionG1\summary.json') |
        ConvertFrom-Json
    $buildSummary = Get-Content -Raw -LiteralPath (
        Join-Path $resultRoot 'ManagedPluginPackages\summary.json') | ConvertFrom-Json
    $packageHashes = [ordered]@{}
    foreach ($package in @($corePackage, $uiPackage, $templatePackage)) {
        $packageHashes[[IO.Path]::GetFileName($package)] = (
            Get-FileHash $package -Algorithm SHA256).Hash
    }
    $buildHash = (Get-Content -Raw -LiteralPath (
            Join-Path $templateContentRoot 'src\DemoPlugin.Plugin\packages.lock.json') |
        ConvertFrom-Json).dependencies.'net10.0'.'MyAvaloniaManagement.Plugin.Build'.contentHash
    $summary = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        productVersion = '3.0.0'
        sdkVersion = '3.1.0'
        templateVersion = '1.1.0'
        buildVersion = '1.1.2'
        manifestSchema = 2
        sdkRange = '[3.1.0,4.0.0)'
        configuration = $Configuration
        api = $g1Summary.api
        hostCoverage = $g1Summary.hostCoverage
        tests = [ordered]@{
            g1 = $g1Summary.tests
            buildContractNegativeCases = [int]$buildSummary.gates.contractNegativeCases
            externalHost = $externalHostPassed
            generatedSolutions = 4
            generatedLockFilesPerSolution = 3
            legacyTemplateNegative = 1
        }
        packages = [ordered]@{
            candidateSha256 = $packageHashes
            coreLockSha512 = $coreLockHash
            uiLockSha512 = $uiLockHash
            publishedBuildLockSha512 = $buildHash
        }
        # 摘要只保存可比较事实，不泄漏本机临时目录。ExtractRoot 仅是本轮 Host 装载的过程路径，
        # 既不属于插件契约，也不能成为跨机器证据的一部分。
        externalPackages = @(@($providerPackage, $consumerPackage) | ForEach-Object {
                [ordered]@{
                    project = $_.Project
                    pluginId = $_.PluginId
                    zipSha256 = $_.ZipSha256
                    manifestSha256 = $_.ManifestSha256
                    files = $_.Files
                }
            })
        standaloneStartupCheck = $true
        dottedNameSupported = $true
        deterministicPluginPackageRuns = 2
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        published = $false
        uploaded = $false
        tagCreated = $false
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 20),
        [Text.UTF8Encoding]::new($false))
    Write-Host "[Workflow Action G2] 非发布门禁通过。摘要：$resultRoot\summary.json"
}
finally {
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $originalPackages, 'Process')
    [Environment]::SetEnvironmentVariable(
        'MYAVALONIA_WORKFLOW_G2_PLUGIN_ROOT', $originalExternalRoot, 'Process')
    if (Test-Path -LiteralPath $temporaryRoot) {
        Assert-ChildPath $temporaryRoot $temporaryParent 'G2 临时清理'
        & dotnet build-server shutdown | Out-Host
        # NuGet/MSBuild 偶尔会在进程退出后的极短时间内继续持有缓存文件句柄。
        # 清理属于卫生动作，不应覆盖此前更有价值的门禁失败；因此有限重试后只告警，
        # 并且目标已经由 Assert-ChildPath 约束在本次随机临时目录内。
        $temporaryCleanupError = $null
        foreach ($attempt in 1..3) {
            try {
                Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction Stop
                $temporaryCleanupError = $null
                break
            }
            catch {
                $temporaryCleanupError = $_
                if ($attempt -lt 3) {
                    Start-Sleep -Milliseconds 500
                }
            }
        }
        if ($null -ne $temporaryCleanupError) {
            Write-Warning "G2 临时目录暂未完全清理：$temporaryRoot。原因：$($temporaryCleanupError.Exception.Message)"
        }
    }
}
