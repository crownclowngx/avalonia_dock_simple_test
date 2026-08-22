[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\ExclusiveDocumentActivation'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G3 结果目录不在仓库内：$resultRoot。"
}

# G3 是开发阶段的本地非发布门禁。各测试进程串行执行，避免 Host 输出、插件部署目录和
# Headless Avalonia 资源互相干扰。本脚本不读取或初始化 AIFLOW，不调用 Windows CI/Smoke、
# ReleaseAcceptance、任何发布门禁、签名、上传、标签或发布操作。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$suites = @(
    [pscustomobject]@{
        Name = 'G3-Sdk'
        Project = 'Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj'
        Filter = 'FullyQualifiedName~DocumentAndDescriptorTests|FullyQualifiedName~SdkBoundaryTests'
    },
    [pscustomobject]@{
        Name = 'G3-Host'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
        Filter = 'FullyQualifiedName~DocumentPersistenceV2Tests|FullyQualifiedName~HostDockAdapterTests'
    },
    [pscustomobject]@{
        Name = 'G3-HeadlessUi'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        Filter = 'FullyQualifiedName~HostDockAdapterUiTests|FullyQualifiedName~MyPlugTestV3UiTests|FullyQualifiedName~DaTangAccountingHelpPlugV2UiTests|FullyQualifiedName~MySmallToolsV2UiTests'
    },
    [pscustomobject]@{
        Name = 'G3-PluginIntegration'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        Filter = 'FullyQualifiedName~MyPlugTestV3AcceptanceTests|FullyQualifiedName~DaTangAccountingHelpPlugV2MigrationTests|FullyQualifiedName~MySmallToolsV2MigrationTests'
    },
    [pscustomobject]@{
        Name = 'G3-MyPlugTest'
        Project = 'Plugins\MyPlugTest\MyPlugTest.Tests\MyPlugTest.Tests.csproj'
        Filter = 'FullyQualifiedName~RevisionedDocumentSaveTests'
    },
    [pscustomobject]@{
        Name = 'G3-MySmallTools'
        Project = 'Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj'
        Filter = 'FullyQualifiedName~VideoToolStabilityTests|FullyQualifiedName~VideoDecryptionTests'
    },
    [pscustomobject]@{
        Name = 'G3-BiliDownloader'
        Project = 'Plugins\BiliDownloader\BiliDownloader.Tests\BiliDownloader.Tests.csproj'
        Filter = 'FullyQualifiedName~G12V2MigrationTests'
    }
)

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Set-Utf8File {
    param([string]$Path, [string]$Content)
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

$suiteSummary = [ordered]@{}
$totalPassed = 0
Push-Location $repositoryRoot
try {
    foreach ($suite in $suites) {
        $suiteDirectory = Join-Path $resultRoot $suite.Name
        New-Item -ItemType Directory -Path $suiteDirectory | Out-Null
        $arguments = @(
            'test', $suite.Project,
            '-c', $Configuration,
            '-p:SkipPluginDeploy=true',
            '--filter', $suite.Filter,
            '--results-directory', $suiteDirectory,
            '--logger', "trx;LogFileName=$($suite.Name).trx",
            '--logger', 'console;verbosity=minimal'
        )
        if ($NoRestore) { $arguments += '--no-restore' }

        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$($suite.Name) 失败，退出码：$LASTEXITCODE。"
        }

        [xml]$trx = Get-Content -LiteralPath (Join-Path $suiteDirectory "$($suite.Name).trx")
        $counters = $trx.TestRun.ResultSummary.Counters
        Assert-True (
            [int]$counters.failed -eq 0 -and
            [int]$counters.notExecuted -eq 0 -and
            [int]$counters.executed -eq [int]$counters.passed) `
            "$($suite.Name) TRX 未全绿。"
        $passed = [int]$counters.passed
        $suiteSummary[$suite.Name] = $passed
        $totalPassed += $passed
    }

    $productionPaths = @(
        'Host\MyAvaloniaManagement.PluginSdk',
        'Host\MyAvaloniaManagement',
        'Plugins\MyPlugTest\MyPlugTest',
        'Plugins\DaTangAccountingHelpPlug\DaTangAccountingHelpPlug',
        'Plugins\BiliDownloader\BiliDownloader',
        'Plugins\MySmallTools\MySmallTools'
    )
    & rg --quiet 'DocumentActivationContext' @productionPaths -g '*.cs'
    Assert-True ($LASTEXITCODE -eq 1) 'G3 生产源码重新出现旧 DocumentActivationContext。'

    $coordinator = 'Host\MyAvaloniaManagement\Business\Documents\DocumentPersistenceCoordinator.cs'
    & rg --quiet 'new NewDocumentActivation' $coordinator
    Assert-True ($LASTEXITCODE -eq 0) 'Host 新建入口没有构造 NewDocumentActivation。'
    & rg --quiet 'new RestoreDocumentActivation' $coordinator
    Assert-True ($LASTEXITCODE -eq 0) 'Host 文件打开入口没有构造 RestoreDocumentActivation。'

    $apiPath = 'Host\MyAvaloniaManagement.PluginSdk\ApiCompatibility\v3\PublicAPI.Unshipped.txt'
    $api = Get-Content -LiteralPath $apiPath -Raw
    Assert-True ($api.Contains('NewDocumentActivation', [StringComparison]::Ordinal)) `
        'v3 Unshipped 缺少 NewDocumentActivation。'
    Assert-True ($api.Contains('RestoreDocumentActivation', [StringComparison]::Ordinal)) `
        'v3 Unshipped 缺少 RestoreDocumentActivation。'
    Assert-True (-not $api.Contains('DocumentActivationContext', [StringComparison]::Ordinal)) `
        'v3 Unshipped 仍包含旧 DocumentActivationContext。'

    # 独立消费负例不依赖源码扫描：用真实 SDK 项目引用编译旧调用，必须由 C# 编译器报告
    # CS0246。这样即使有人只从文本基线删除旧名字、却在程序集里留下兼容类型，门禁仍会失败。
    $legacyProbeRoot = Join-Path $resultRoot 'LegacyActivationCompileProbe'
    New-Item -ItemType Directory -Path $legacyProbeRoot | Out-Null
    $sdkProject = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
        'Host\MyAvaloniaManagement.PluginSdk\MyAvaloniaManagement.PluginSdk.csproj'))
    Set-Utf8File (Join-Path $legacyProbeRoot 'LegacyActivationCompileProbe.csproj') @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$sdkProject" />
  </ItemGroup>
</Project>
"@
    Set-Utf8File (Join-Path $legacyProbeRoot 'LegacyProbe.cs') @'
using MyAvaloniaManagement.PluginSdk;

public static class LegacyProbe
{
    public static object Create() => new DocumentActivationContext("旧入口");
}
'@
    Push-Location $legacyProbeRoot
    try {
        $legacyOutput = @(& dotnet build -c $Configuration --nologo -p:SkipPluginDeploy=true 2>&1)
        $legacyExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $legacyText = $legacyOutput -join [Environment]::NewLine
    Assert-True ($legacyExitCode -ne 0) '旧 DocumentActivationContext 独立消费负例意外编译成功。'
    Assert-True ($legacyText.Contains('CS0246', [StringComparison]::Ordinal)) `
        '旧 DocumentActivationContext 独立消费负例缺少 CS0246。'

    # record 会自动生成受保护复制构造函数，因此这里不能只检查公开构造函数的可见性。
    # 独立程序集尝试借复制构造函数引入第三种激活形态时，必须因 SDK 内部抽象标记未实现而失败。
    $closedHierarchyProbeRoot = Join-Path $resultRoot 'ClosedActivationHierarchyCompileProbe'
    New-Item -ItemType Directory -Path $closedHierarchyProbeRoot | Out-Null
    Set-Utf8File (Join-Path $closedHierarchyProbeRoot 'ClosedActivationHierarchyCompileProbe.csproj') @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$sdkProject" />
  </ItemGroup>
</Project>
"@
    Set-Utf8File (Join-Path $closedHierarchyProbeRoot 'ExternalActivation.cs') @'
using MyAvaloniaManagement.PluginSdk;

public sealed record ExternalActivation : DocumentActivation
{
    public ExternalActivation(DocumentActivation source)
        : base(source)
    {
    }
}
'@
    Push-Location $closedHierarchyProbeRoot
    try {
        $closedHierarchyOutput = @(& dotnet build -c $Configuration --nologo -p:SkipPluginDeploy=true 2>&1)
        $closedHierarchyExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $closedHierarchyText = $closedHierarchyOutput -join [Environment]::NewLine
    Assert-True ($closedHierarchyExitCode -ne 0) '外部程序集意外成功派生第三种 DocumentActivation。'
    Assert-True ($closedHierarchyText.Contains('CS0534', [StringComparison]::Ordinal)) `
        '封闭激活层次消费负例缺少预期的 CS0534。'

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        suites = $suiteSummary
        passed = $totalPassed
        failed = 0
        legacyActivationCompileRejected = $true
        externalActivationSubtypeCompileRejected = $true
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    Set-Utf8File (Join-Path $resultRoot 'summary.json') `
        ($summary | ConvertTo-Json -Depth 4)
    Write-Host "G3 互斥 Document 激活专项门禁通过：$totalPassed 项。"
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
