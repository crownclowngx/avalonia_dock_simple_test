[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\LayoutLifecycleV2'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G8 结果目录不在仓库内：$resultRoot。"
}

# G8 是开发阶段的非发布门禁。本脚本只执行本地 .NET 测试和静态结构扫描；
# 不读取或初始化 AIFLOW，也不调用 Windows CI、Windows Smoke、ReleaseAcceptance、
# 发布包构建、签名、上传、标签或任何发布门禁。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$suites = @(
    [pscustomobject]@{
        Name = 'G8-HostUnit'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
        Filter = 'FullyQualifiedName~HostDiagnosticsTests|FullyQualifiedName~G11LowValuePublicSurfaceTests|FullyQualifiedName~IdentityAndRegistryTests|FullyQualifiedName~HostRuntime'
    },
    [pscustomobject]@{
        Name = 'G8-Plugin'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        Filter = 'FullyQualifiedName~DockLayout|FullyQualifiedName~DockFourWayLayoutTests|FullyQualifiedName~DockFloatingDisabledTests|FullyQualifiedName~PluginLifecycleCoordinatorTests|FullyQualifiedName~PluginCompatibilityTests|FullyQualifiedName~VersionPolicyTests'
    },
    [pscustomobject]@{
        Name = 'G8-HeadlessUI'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        Filter = 'FullyQualifiedName~ApplicationAndWindowTests|FullyQualifiedName~HostDockAdapterUiTests|FullyQualifiedName~BiliDownloaderDocumentVisualTests'
    },
    [pscustomobject]@{
        Name = 'G8-PluginSdk'
        Project = 'Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj'
        Filter = 'FullyQualifiedName~SdkBoundaryTests|FullyQualifiedName~ApiBaselineTests'
    },
    [pscustomobject]@{
        Name = 'G8-BiliDownloader'
        Project = 'Plugins\BiliDownloader\BiliDownloader.Tests\BiliDownloader.Tests.csproj'
        Filter = 'FullyQualifiedName~BiliDownloaderModuleTests|FullyQualifiedName~SchedulerViewModelTests'
    }
)

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
        if ($NoRestore) {
            $arguments += '--no-restore'
        }

        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$($suite.Name) 失败，退出码：$LASTEXITCODE。"
        }

        $trxPath = Join-Path $suiteDirectory "$($suite.Name).trx"
        [xml]$trx = Get-Content -LiteralPath $trxPath
        $counters = $trx.TestRun.ResultSummary.Counters
        if ([int]$counters.failed -ne 0 -or
            [int]$counters.notExecuted -ne 0 -or
            [int]$counters.executed -ne [int]$counters.passed) {
            throw "$($suite.Name) TRX 未全绿。"
        }

        $passed = [int]$counters.passed
        $suiteSummary[$suite.Name] = $passed
        $totalPassed += $passed
    }

    $layoutRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement\Business\Layout'
    $layoutForbidden = `
        'DockLayoutSnapshotV1|LayoutMigrator|LegacyContributionIdMap|' + `
        'NormalizePersistedToolId|IsFloating|FloatingBounds|FloatingWidth|FloatingHeight'
    & rg --quiet $layoutForbidden $layoutRoot -g '*.cs'
    if ($LASTEXITCODE -eq 0) {
        throw 'G8 Host Layout 生产代码重新出现 V1、Migrator、浮动字段或历史 ID 归一化。'
    }
    if ($LASTEXITCODE -gt 1) {
        throw '无法执行 G8 Layout 结构扫描。'
    }

    $layoutStore = Join-Path $layoutRoot 'DockLayoutStore.cs'
    & rg --quiet 'LayoutFileName\s*=\s*"layout-v2\.json"' $layoutStore
    if ($LASTEXITCODE -ne 0) {
        throw 'G8 Layout 文件名没有固定为 layout-v2.json。'
    }
    $layoutSnapshot = Join-Path $layoutRoot 'DockLayoutSnapshotV2.cs'
    & rg --quiet 'CurrentSchemaVersion\s*=\s*2' $layoutSnapshot
    if ($LASTEXITCODE -ne 0) {
        throw 'G8 Layout schemaVersion 没有固定为 2。'
    }

    $legacyRoot = Join-Path $repositoryRoot `
        'Host\MyAvaloniaManagement.LegacyPluginContracts'
    $legacyLifecycleForbidden = `
        'PluginLifecycleManager|PluginLifecycleState|PluginLifecycleStage|' + `
        'PluginLifecycleStatus|PluginLifecycleOptions|PluginLifecycleRegistration|' + `
        'PluginLifecyclePlanBuilder|PluginLifecycleOperationRunner|' + `
        'IPluginLifecycleDependencies'
    & rg --quiet $legacyLifecycleForbidden $legacyRoot -g '*.cs'
    if ($LASTEXITCODE -eq 0) {
        throw 'G8 Legacy contracts 重新出现已删除的 public 生命周期编排类型。'
    }
    if ($LASTEXITCODE -gt 1) {
        throw '无法执行 G8 Legacy 生命周期结构扫描。'
    }

    $legacyLifecycleContract = Join-Path $legacyRoot 'Plugin\IPluginLifecycle.cs'
    & rg --quiet '\bOrder\b' $legacyLifecycleContract
    if ($LASTEXITCODE -eq 0) {
        throw 'G8 Legacy IPluginLifecycle 重新出现 Order。'
    }
    if ($LASTEXITCODE -gt 1) {
        throw '无法检查 G8 Legacy IPluginLifecycle。'
    }

    $biliProduction = Join-Path $repositoryRoot 'Plugins\BiliDownloader\BiliDownloader'
    & rg --quiet 'PluginLifecycleManager|IPluginLifecycleDependencies' `
        $biliProduction -g '*.cs'
    if ($LASTEXITCODE -eq 0) {
        throw 'G8 BiliDownloader 生产代码仍依赖 Host Manager 或生命周期依赖图。'
    }
    if ($LASTEXITCODE -gt 1) {
        throw '无法执行 G8 BiliDownloader 结构扫描。'
    }

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        suites = $suiteSummary
        passed = $totalPassed
        failed = 0
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseGate = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 4),
        [Text.UTF8Encoding]::new($false))
    Write-Host "G8 Layout/Lifecycle V2 专项门禁通过：$totalPassed 项。"
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
