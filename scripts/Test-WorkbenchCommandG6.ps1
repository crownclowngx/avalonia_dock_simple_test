[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$ReuseVerifiedBaseGate,
    [switch]$RefreshTemplateLocks
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = Join-Path $repositoryRoot 'artifacts\test-results\WorkbenchCommandG6'
$candidateFeed = Join-Path $resultRoot 'candidate-feed'
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryParent (
    'myavalonia-workbench-command-g6-' + [Guid]::NewGuid().ToString('N'))
$isolatedPackages = Join-Path $temporaryRoot 'packages'
$generatedRoot = Join-Path $temporaryRoot 'generated'
$hostPackageRoot = Join-Path $temporaryRoot 'host-packages'
$templateHive = Join-Path $temporaryRoot 'template-hive'
$templateProject = Join-Path $repositoryRoot (
    'Packaging\MyAvaloniaManagement.Plugin.Templates\MyAvaloniaManagement.Plugin.Templates.csproj')
$templateContentRoot = Join-Path $repositoryRoot (
    'Packaging\MyAvaloniaManagement.Plugin.Templates\content\myavalonia-plugin')
$inputCommit = '97732d21ad16676a38a298d6a8fda3140d467759'
$inputTree = 'd81b79445283019b11e772d1103d98b2f5417886'
$fixedPackageTimestamp = [DateTimeOffset]::new(
    2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

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

function Get-TrxCounts {
    param([Parameter(Mandatory)] [string]$Path)
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "缺少 TRX：$Path。"
    [xml]$trx = Get-Content -LiteralPath $Path
    $counters = $trx.TestRun.ResultSummary.Counters
    Assert-True (
        [int]$counters.failed -eq 0 -and
        [int]$counters.notExecuted -eq 0 -and
        [int]$counters.executed -eq [int]$counters.passed) `
        "TRX 未做到全部执行、零失败、零跳过：$Path"
    return [ordered]@{
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        skipped = [int]$counters.notExecuted
    }
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

    # .NET 10 的 NuGet.Packaging 会把随机 OPC 核心属性路径、关系 ID 和打包时间写入容器。
    # 这里只规范化候选包的容器元数据；程序集、PDB、XML、README 和 nuspec 字节保持不变。
    $source = [IO.Compression.ZipFile]::OpenRead($Path)
    $payload = [Collections.Generic.List[object]]::new()
    try {
        foreach ($entry in $source.Entries) {
            $stream = [IO.MemoryStream]::new()
            $input = $entry.Open()
            try { $input.CopyTo($stream) }
            finally { $input.Dispose() }
            $payload.Add([pscustomobject]@{ Name = $entry.FullName; Bytes = $stream.ToArray() })
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

function New-CandidateSdkPackages {
    $runs = @()
    foreach ($run in 1..2) {
        $runRoot = Join-Path $resultRoot "candidate-run$run"
        New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
        Invoke-DotNet @(
            'pack', 'Host/MyAvaloniaManagement.PluginSdk/MyAvaloniaManagement.PluginSdk.csproj',
            '-c', $Configuration, '--no-restore', '--nologo', '-warnaserror', '-o', $runRoot)
        Invoke-DotNet @(
            'pack', 'Host/MyAvaloniaManagement.PluginSdk.UI/MyAvaloniaManagement.PluginSdk.UI.csproj',
            '-c', $Configuration, '--no-restore', '--nologo', '-warnaserror', '-o', $runRoot)
        foreach ($package in Get-ChildItem -LiteralPath $runRoot -File |
                 Where-Object { $_.Extension -in @('.nupkg', '.snupkg') }) {
            ConvertTo-DeterministicNupkg $package.FullName
        }
        $runs += ,([ordered]@{})
        foreach ($package in Get-ChildItem -LiteralPath $runRoot -File | Sort-Object Name) {
            $runs[$run - 1][$package.Name] = (
                Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
        }
    }

    Assert-True ($runs[0].Count -eq 4 -and $runs[1].Count -eq 4) `
        'Core/UI 候选必须各生成 nupkg 与 snupkg。'
    foreach ($name in $runs[0].Keys) {
        Assert-True ($runs[1].Contains($name) -and $runs[0][$name] -ceq $runs[1][$name]) `
            "候选包两轮字节不一致：$name。"
    }

    New-Item -ItemType Directory -Path $candidateFeed -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $resultRoot 'candidate-run1') -File |
        Copy-Item -Destination $candidateFeed -Force
    return $runs[0]
}

function Write-NuGetConfig {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [string]$LocalFeed,
        [string]$LocalKey = 'g6-local'
    )
    $path = Join-Path $Root 'NuGet.Config'
    $escapedFeed = [Security.SecurityElement]::Escape($LocalFeed)
    $text = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="$LocalKey" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="$LocalKey">
      <package pattern="MyAvaloniaManagement.PluginSdk" />
      <package pattern="MyAvaloniaManagement.PluginSdk.UI" />
      <package pattern="MyAvaloniaManagement.Plugin.Templates" />
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

function Write-PublicNuGetConfig {
    param([Parameter(Mandatory)] [string]$Root)

    # 历史模板必须还原它发布时精确锁定的 SDK 字节，不能被 G6 候选 feed 的同名包遮蔽。
    # 独立配置同时避免继承临时目录顶层为 3.3 候选设置的 package source mapping。
    $path = Join-Path $Root 'NuGet.Config'
    $text = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
'@
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
    return $path
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
                $dependencies.'MyAvaloniaManagement.PluginSdk'.resolved -ceq '3.3.0' -and
                $dependencies.'MyAvaloniaManagement.PluginSdk'.contentHash -ceq $CoreHash) `
                "$($lock.FullName) 的 Core SDK 锁定事实与候选包不一致。"
        }
        if ($dependencies.PSObject.Properties.Name -contains 'MyAvaloniaManagement.PluginSdk.UI') {
            Assert-True (
                $dependencies.'MyAvaloniaManagement.PluginSdk.UI'.resolved -ceq '3.3.0' -and
                $dependencies.'MyAvaloniaManagement.PluginSdk.UI'.contentHash -ceq $UiHash) `
                "$($lock.FullName) 的 UI SDK 锁定事实与候选包不一致。"
        }
        if ($dependencies.PSObject.Properties.Name -contains 'MyAvaloniaManagement.Plugin.Build') {
            Assert-True ($dependencies.'MyAvaloniaManagement.Plugin.Build'.resolved -ceq '1.1.2') `
                "$($lock.FullName) 必须继续精确锁定 Build 1.1.2。"
        }
    }
}

function New-TemplateProject {
    param([string]$Name, [string]$PluginId)
    $root = Join-Path $generatedRoot $Name.Replace('.', '-')
    Invoke-DotNet @(
        'new', 'myavalonia-plugin', '-n', $Name, '--plugin-id', $PluginId,
        '-o', $root, '--debug:custom-hive', $templateHive, '--no-update-check')
    $config = Write-NuGetConfig $root $candidateFeed
    return [pscustomobject]@{ Name = $Name; PluginId = $PluginId; Root = $root; Config = $config }
}

function Assert-GeneratedBoundary {
    param([Parameter(Mandatory)] $Project)
    $files = @(Get-ChildItem -LiteralPath $Project.Root -Recurse -File | Where-Object {
            $_.Name -ne 'NuGet.Config' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        })
    $combined = ($files | Where-Object Extension -In @(
            '.cs', '.csproj', '.props', '.targets', '.md', '.json') |
        ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
    Assert-True (-not $combined.Contains($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) `
        "$($Project.Name) 生成物泄漏仓库绝对路径。"
    Assert-True ($combined -notmatch '<ProjectReference[^>]+Host[\\/]') `
        "$($Project.Name) 生成物不得引用 Host 源码项目。"
    Assert-True (@(Get-ChildItem -LiteralPath $Project.Root -Filter packages.lock.json -Recurse -File).Count -eq 3) `
        "$($Project.Name) 必须生成三份 lock file。"
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

function Test-StandaloneStartup {
    param([Parameter(Mandatory)] $Project)
    $standaloneProject = Join-Path $Project.Root (
        "src\$($Project.Name).Standalone\$($Project.Name).Standalone.csproj")
    $process = Start-Process -FilePath 'dotnet' -WindowStyle Hidden -PassThru `
        -WorkingDirectory $Project.Root -ArgumentList @(
            'run', '--project', $standaloneProject, '-c', $Configuration,
            '--no-build', '--no-restore')
    try {
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
    Assert-True ($runs[0].ZipHash -ceq $runs[1].ZipHash) "$($Project.Name) 两轮 ZIP 不确定。"
    Assert-True ($runs[0].ManifestHash -ceq $runs[1].ManifestHash) `
        "$($Project.Name) 两轮 manifest 不确定。"

    $extractRoot = Join-Path $hostPackageRoot $Project.Name
    Expand-Archive -LiteralPath $runs[0].Zip -DestinationPath $extractRoot -Force
    $manifestPath = @(Get-ChildItem -LiteralPath $extractRoot -Filter plugin.manifest.json -Recurse -File)
    Assert-True ($manifestPath.Count -eq 1) "$($Project.Name) ZIP 缺少唯一 manifest。"
    $manifestObject = Get-Content -Raw -LiteralPath $manifestPath[0].FullName | ConvertFrom-Json
    Assert-True (
        [int]$manifestObject.schemaVersion -eq 2 -and
        [string]$manifestObject.sdk.minInclusive -ceq '3.3.0' -and
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

function New-LegacyTemplatePackage {
    param([string]$TemplateVersion, [string]$Name, [string]$PluginId)
    $hive = Join-Path $temporaryRoot "legacy-hive-$TemplateVersion"
    $root = Join-Path $generatedRoot $Name
    Invoke-DotNet @(
        'new', 'install', "MyAvaloniaManagement.Plugin.Templates::$TemplateVersion",
        '--debug:custom-hive', $hive, '--force')
    Invoke-DotNet @(
        'new', 'myavalonia-plugin', '-n', $Name, '--plugin-id', $PluginId,
        '-o', $root, '--debug:custom-hive', $hive, '--no-update-check')
    $publicConfig = Write-PublicNuGetConfig $root
    Invoke-DotNet @(
        'restore', "$Name.slnx", '--locked-mode', '--configfile', $publicConfig,
        '--packages', $isolatedPackages, '--nologo') $root
    Invoke-DotNet @(
        'build', "$Name.slnx", '-c', $Configuration, '--no-restore', '--nologo',
        '-warnaserror', '-p:SkipPluginDeploy=true') $root
    $output = Join-Path $resultRoot "legacy-packages\$Name"
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Invoke-DotNet @(
        'msbuild', "src\$Name.Plugin\$Name.Plugin.csproj", '-t:BuildManagedPluginPackage',
        "-p:Configuration=$Configuration", "-p:ManagedPluginPackageOutput=$output",
        "-p:RestoreConfigFile=$publicConfig", '--nologo') $root
    $zip = @(Get-ChildItem -LiteralPath $output -Filter '*.zip' -File)
    Assert-True ($zip.Count -eq 1) "$Name 没有生成唯一旧插件 ZIP。"
    $extractRoot = Join-Path $hostPackageRoot $Name
    Expand-Archive -LiteralPath $zip[0].FullName -DestinationPath $extractRoot -Force
    return $extractRoot
}

function Invoke-OldHostNegative {
    param([string]$CandidatePluginRoot)
    $oldRoot = Join-Path $temporaryRoot 'old-host'
    $archive = Join-Path $temporaryRoot 'old-host.zip'
    & git -C $repositoryRoot archive --format=zip --output=$archive $inputCommit
    Assert-True ($LASTEXITCODE -eq 0) '无法导出 G5 Host 源码快照。'
    Expand-Archive -LiteralPath $archive -DestinationPath $oldRoot
    $testRoot = Join-Path $oldRoot 'G6OldHostNegative'
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $testRoot 'G6OldHostNegative.csproj'), @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <AssemblyName>MyAvaloniaManagement.PluginTests</AssemblyName>
    <RootNamespace>G6OldHostNegative</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <ProjectReference Include="..\Host\MyAvaloniaManagement\MyAvaloniaManagement.csproj" />
  </ItemGroup>
</Project>
'@, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $testRoot 'OldHostNegativeTests.cs'), @'
using System;
using System.IO;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using Xunit;

namespace G6OldHostNegative;

public sealed class OldHostNegativeTests
{
    [Fact]
    public void G5Host在执行三三插件代码前按Manifest拒绝()
    {
        var root = Environment.GetEnvironmentVariable("G6_CANDIDATE_PLUGIN_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(root));
        var snapshot = AssemblyLoaderHelper.Discover(Path.GetFullPath(root!));
        Assert.Empty(snapshot.Assemblies);
        var diagnostic = Assert.Single(snapshot.Diagnostics);
        Assert.Equal(HostDiagnosticCodes.PluginSdkIncompatible, diagnostic.Code);
        Assert.DoesNotContain(
            snapshot.Diagnostics,
            item => item.Code == HostDiagnosticCodes.PluginAssemblyLoadFailed);
    }
}
'@, [Text.UTF8Encoding]::new($false))
    $previous = [Environment]::GetEnvironmentVariable('G6_CANDIDATE_PLUGIN_ROOT')
    try {
        $env:G6_CANDIDATE_PLUGIN_ROOT = $CandidatePluginRoot
        $trxRoot = Join-Path $resultRoot 'old-host-negative'
        New-Item -ItemType Directory -Path $trxRoot -Force | Out-Null
        Invoke-DotNet @(
            'test', 'G6OldHostNegative.csproj', '-c', $Configuration, '--nologo', '-warnaserror',
            '--results-directory', $trxRoot,
            '--logger', 'trx;LogFileName=WorkbenchCommandG6.OldHostNegative.trx') $testRoot
        return Get-TrxCounts (Join-Path $trxRoot 'WorkbenchCommandG6.OldHostNegative.trx')
    }
    finally { [Environment]::SetEnvironmentVariable('G6_CANDIDATE_PLUGIN_ROOT', $previous) }
}

Assert-ChildPath $resultRoot (Join-Path $repositoryRoot 'artifacts\test-results') 'G6 结果'
Assert-ChildPath $temporaryRoot $temporaryParent 'G6 临时树'
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null
New-Item -ItemType Directory -Path $generatedRoot -Force | Out-Null
New-Item -ItemType Directory -Path $hostPackageRoot -Force | Out-Null

$originalPackages = [Environment]::GetEnvironmentVariable('NUGET_PACKAGES', 'Process')
$originalCandidateRoot = [Environment]::GetEnvironmentVariable(
    'MYAVALONIA_WORKBENCH_COMMAND_G6_PLUGIN_ROOT', 'Process')
$originalLegacyRoot = [Environment]::GetEnvironmentVariable(
    'MYAVALONIA_WORKBENCH_COMMAND_G6_LEGACY_PLUGIN_ROOT', 'Process')

try {
    $env:NUGET_PACKAGES = $isolatedPackages
    $resolvedInputTree = (& git -C $repositoryRoot show -s --format=%T $inputCommit).Trim()
    Assert-True ($LASTEXITCODE -eq 0 -and $resolvedInputTree -ceq $inputTree) `
        'G6 输入 commit/tree 与冻结的 G5 基线不一致。'
    & git -C $repositoryRoot merge-base --is-ancestor $inputCommit HEAD
    Assert-True ($LASTEXITCODE -eq 0) '当前 HEAD 不是 G6 输入提交的后继。'

    Invoke-DotNet @('restore', 'MyAvaloniaManagement.sln', '--locked-mode', '--nologo')
    $candidateHashes = New-CandidateSdkPackages
    $corePackage = Join-Path $candidateFeed 'MyAvaloniaManagement.PluginSdk.3.3.0.nupkg'
    $uiPackage = Join-Path $candidateFeed 'MyAvaloniaManagement.PluginSdk.UI.3.3.0.nupkg'
    $coreLockHash = Get-Sha512Base64 $corePackage
    $uiLockHash = Get-Sha512Base64 $uiPackage

    if ($RefreshTemplateLocks) {
        $config = Write-NuGetConfig $temporaryRoot $candidateFeed
        Invoke-DotNet @(
            'restore', (Join-Path $templateContentRoot 'DemoPlugin.slnx'),
            '--force-evaluate', '--configfile', $config, '--packages', $isolatedPackages, '--nologo')
        Write-Host '[Workbench Command G6] 模板 lock file 已按规范候选包刷新；请重新运行默认门禁。'
        return
    }

    Assert-TemplateLocks $coreLockHash $uiLockHash
    [xml]$versions = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props')
    $properties = $versions.Project.PropertyGroup
    Assert-True (
        [string]$properties.MyAvaloniaPluginSdkVersion -ceq '3.3.0' -and
        [string]$properties.MyAvaloniaPluginSdkFileVersion -ceq '3.3.0.0' -and
        [string]$properties.MyAvaloniaPluginSdkAssemblyVersion -ceq '3.3.0.0') `
        'Core/UI SDK 包、FileVersion 或 AssemblyVersion 不是 3.3.0 候选。'
    Assert-True (
        [string]$properties.MyAvaloniaProductVersion -ceq '3.0.0' -and
        [string]$properties.MyAvaloniaPluginSdkWorkflowVersion -ceq '1.0.0') `
        'G6 不得提升 Host 产品或 Workflow SDK。'

    if (-not $ReuseVerifiedBaseGate) {
        Invoke-Pwsh (Join-Path $PSScriptRoot 'Test-HostV4DevelopmentGate.ps1') @(
            '-Stage', 'G7', '-Configuration', $Configuration)
    }

    Invoke-Pwsh (Join-Path $PSScriptRoot 'Test-PluginSdkPackage.ps1') @(
        '-Configuration', $Configuration)

    $templateConfig = Write-NuGetConfig $temporaryRoot $candidateFeed
    Invoke-DotNet @(
        'restore', (Join-Path $templateContentRoot 'DemoPlugin.slnx'), '--locked-mode',
        '--configfile', $templateConfig, '--packages', $isolatedPackages, '--nologo')
    Invoke-DotNet @(
        'pack', $templateProject, '-c', $Configuration, '--no-restore', '--nologo',
        '-warnaserror', '-o', $candidateFeed)
    $templatePackage = Join-Path $candidateFeed (
        'MyAvaloniaManagement.Plugin.Templates.1.3.0.nupkg')
    ConvertTo-DeterministicNupkg $templatePackage
    $templateEntries = Read-ZipEntries $templatePackage
    Assert-True (-not ($templateEntries | Where-Object { $_ -like 'lib/*' -or $_ -like 'ref/*' })) `
        'Template 包不得包含 lib/ref 程序集。'
    Assert-True (@($templateEntries | Where-Object { $_ -like 'content/*/packages.lock.json' }).Count -eq 3) `
        'Template 包必须携带三份 lock file。'
    Assert-True ($templateEntries -contains 'content/myavalonia-plugin/docs/workbench-commands.md') `
        'Template 包缺少 Workbench Command 专用说明。'
    [xml]$templateNuspec = Read-ZipText $templatePackage (
        'MyAvaloniaManagement.Plugin.Templates.nuspec')
    Assert-True (
        [string]$templateNuspec.package.metadata.version -ceq '1.3.0' -and
        [string]$templateNuspec.package.metadata.packageTypes.packageType.name -ceq 'Template') `
        'Template nupkg 的版本或 PackageType 错误。'

    Invoke-DotNet @(
        'new', 'install', $templatePackage, '--debug:custom-hive', $templateHive, '--force')
    $neutral = New-TemplateProject 'CommandTemplateProbe' (
        'myavalonia.plugin.command-template-probe')
    $dotted = New-TemplateProject 'MyAvalonia.CommandTemplateProbe' (
        'myavalonia.plugin.command-template-probe-dotted')
    foreach ($project in @($neutral, $dotted)) {
        Assert-GeneratedBoundary $project
        Invoke-GeneratedBuildAndTest $project
    }
    $dottedProject = Get-Content -Raw -LiteralPath (Join-Path $dotted.Root (
        'src\MyAvalonia.CommandTemplateProbe.Plugin\MyAvalonia.CommandTemplateProbe.Plugin.csproj'))
    Assert-True ($dottedProject.Contains(
            '<ManagedPluginEntryType>MyAvalonia.CommandTemplateProbe.Plugin.CommandTemplateProbeModule</ManagedPluginEntryType>',
            [StringComparison]::Ordinal)) `
        '点号项目名没有生成合法入口类型。'
    Test-StandaloneStartup $neutral

    $candidatePackages = @(
        Build-PluginPackageTwice $neutral
        Build-PluginPackageTwice $dotted)
    $combinedControls = Join-Path $hostPackageRoot 'combined\Controls'
    New-Item -ItemType Directory -Path $combinedControls -Force | Out-Null
    foreach ($package in $candidatePackages) {
        $sourceControls = Join-Path $package.ExtractRoot 'Controls'
        Copy-Item -LiteralPath (Get-ChildItem -LiteralPath $sourceControls -Directory).FullName `
            -Destination $combinedControls -Recurse
    }

    $env:MYAVALONIA_WORKBENCH_COMMAND_G6_PLUGIN_ROOT = $combinedControls
    $targetedRoot = Join-Path $resultRoot 'targeted'
    New-Item -ItemType Directory -Path $targetedRoot -Force | Out-Null
    Invoke-DotNet @(
        'test', 'Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj',
        '-c', $Configuration, '--no-restore', '--nologo', '-warnaserror',
        '-p:SkipPluginDeploy=true',
        '--filter', 'FullyQualifiedName~WorkbenchCommandG6ExternalPackageTests',
        '--results-directory', $targetedRoot,
        '--logger', 'trx;LogFileName=WorkbenchCommandG6.ExternalPackages.trx')
    $externalHost = Get-TrxCounts (
        Join-Path $targetedRoot 'WorkbenchCommandG6.ExternalPackages.trx')

    $legacyRoots = @(
        New-LegacyTemplatePackage '1.0.4' 'Legacy30' 'myavalonia.plugin.legacy30'
        New-LegacyTemplatePackage '1.1.0' 'Legacy31' 'myavalonia.plugin.legacy31'
        New-LegacyTemplatePackage '1.2.0' 'Legacy32' 'myavalonia.plugin.legacy32')
    $legacyControls = Join-Path $hostPackageRoot 'legacy-combined\Controls'
    New-Item -ItemType Directory -Path $legacyControls -Force | Out-Null
    foreach ($legacyRoot in $legacyRoots) {
        Copy-Item -LiteralPath (Get-ChildItem -LiteralPath (
                Join-Path $legacyRoot 'Controls') -Directory).FullName `
            -Destination $legacyControls -Recurse
    }
    $env:MYAVALONIA_WORKBENCH_COMMAND_G6_LEGACY_PLUGIN_ROOT = $legacyControls
    Invoke-DotNet @(
        'test', 'Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj',
        '-c', $Configuration, '--no-build', '--no-restore', '--nologo',
        '--filter', 'FullyQualifiedName~WorkbenchCommandG6LegacyPackageTests',
        '--results-directory', $targetedRoot,
        '--logger', 'trx;LogFileName=WorkbenchCommandG6.LegacyPackages.trx')
    $legacyHost = Get-TrxCounts (Join-Path $targetedRoot 'WorkbenchCommandG6.LegacyPackages.trx')
    $oldHostNegative = Invoke-OldHostNegative (Join-Path $candidatePackages[0].ExtractRoot 'Controls')

    Invoke-Pwsh (Join-Path $PSScriptRoot 'Test-Documentation.ps1')

    $packageHashes = [ordered]@{}
    foreach ($package in Get-ChildItem -LiteralPath $candidateFeed -File | Sort-Object Name) {
        $packageHashes[$package.Name] = (
            Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
    }
    $summary = [ordered]@{
        schemaVersion = 1
        stage = 'G6'
        configuration = $Configuration
        inputCommit = $inputCommit
        inputTree = $inputTree
        baseGateReused = [bool]$ReuseVerifiedBaseGate
        productVersion = '3.0.0'
        sdkVersion = '3.3.0'
        templateVersion = '1.3.0'
        workflowSdkVersion = '1.0.0'
        buildVersion = '1.1.2'
        schema = [ordered]@{
            manifest = 2
            documentEnvelope = 2
            layout = 2
            layoutFileName = 'layout-v2.json'
            dataRoot = 'v2'
        }
        tests = [ordered]@{
            generatedSolutions = 2
            generatedLockFilesPerSolution = 3
            generatedTemplateTestsPerSolution = 4
            externalHost = $externalHost
            legacyHost = $legacyHost
            oldHostNegative = $oldHostNegative
        }
        packages = [ordered]@{
            candidateSha256 = $packageHashes
            coreLockSha512 = $coreLockHash
            uiLockSha512 = $uiLockHash
            deterministicRuns = 2
        }
        externalPackages = @($candidatePackages | ForEach-Object {
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
        passed = $true
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        published = $false
        uploaded = $false
        signed = $false
        tagCreated = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 20),
        [Text.UTF8Encoding]::new($false))
    Write-Host '[Workbench Command G6] 本地候选、模板与独立消费门禁全部通过。'
}
finally {
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $originalPackages, 'Process')
    [Environment]::SetEnvironmentVariable(
        'MYAVALONIA_WORKBENCH_COMMAND_G6_PLUGIN_ROOT', $originalCandidateRoot, 'Process')
    [Environment]::SetEnvironmentVariable(
        'MYAVALONIA_WORKBENCH_COMMAND_G6_LEGACY_PLUGIN_ROOT', $originalLegacyRoot, 'Process')
    if (Test-Path -LiteralPath $temporaryRoot) {
        Assert-ChildPath $temporaryRoot $temporaryParent 'G6 临时清理'
        & dotnet build-server shutdown | Out-Host
        foreach ($attempt in 1..3) {
            try {
                Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction Stop
                break
            }
            catch {
                if ($attempt -eq 3) {
                    Write-Warning "G6 临时目录暂未完全清理：$temporaryRoot。"
                }
                else { Start-Sleep -Milliseconds 500 }
            }
        }
    }
}
