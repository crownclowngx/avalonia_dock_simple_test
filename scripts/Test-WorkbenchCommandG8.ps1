[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$ClassicGameRoot,
    [switch]$ReuseVerifiedBaseGate,
    [switch]$ReuseVerifiedClassicGameGate
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($ClassicGameRoot)) {
    $ClassicGameRoot = Join-Path $repositoryRoot `
        '..\avalonia_management_plug\myavalonia-classic-game'
}
$ClassicGameRoot = [IO.Path]::GetFullPath($ClassicGameRoot)
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\WorkbenchCommandG8'))
$allowedResultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\test-results'))
$hostSummaryPath = Join-Path $repositoryRoot 'artifacts\test-results\HostV4\G7\summary.json'
$classicSummaryPath = Join-Path $ClassicGameRoot `
    'artifacts\test-results\ClassicGameWorkbenchCommandG8\summary.json'
$classicGate = Join-Path $ClassicGameRoot 'scripts\Test-ClassicGameWorkbenchCommandG8.ps1'

function Assert-True {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-PwshChecked {
    param([Parameter(Mandatory)][string]$Script, [string[]]$Arguments = @())
    & pwsh -NoProfile -File $Script @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Script 失败，退出码：$LASTEXITCODE。"
    }
}

function Invoke-DotnetChecked {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
    }
}

function Get-TrxCounts {
    param([Parameter(Mandatory)][string]$Path)
    [xml]$trx = Get-Content -Raw -LiteralPath $Path
    $counters = $trx.TestRun.ResultSummary.Counters
    return [ordered]@{
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        skipped = [int]$counters.notExecuted
    }
}

$resultPrefix = $allowedResultRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
Assert-True ($resultRoot.StartsWith($resultPrefix, [StringComparison]::OrdinalIgnoreCase)) `
    'Workbench Command G8 结果目录越界。'
Assert-True (Test-Path -LiteralPath $classicGate -PathType Leaf) `
    "外部 ClassicGame G8 门禁不存在：$classicGate。"

if (-not $ReuseVerifiedBaseGate) {
    # 这是现有 Host V4 本地开发门禁，不是 Windows CI、Windows Smoke 或发布门禁。
    Invoke-PwshChecked (Join-Path $PSScriptRoot 'Test-HostV4DevelopmentGate.ps1') @(
        '-Stage', 'G7', '-Configuration', $Configuration)
}
Assert-True (Test-Path -LiteralPath $hostSummaryPath -PathType Leaf) `
    'Host V4 G7 开发门禁没有生成 summary.json。'
$hostSummary = Get-Content -Raw -LiteralPath $hostSummaryPath | ConvertFrom-Json
Assert-True ([bool]$hostSummary.passed) 'Host V4 G7 开发门禁摘要不是通过状态。'
Assert-True ([double]$hostSummary.hostLineCoverage -ge 86.98) `
    "Host 行覆盖率 $($hostSummary.hostLineCoverage)% 低于 G7 的 86.98%。"
Assert-True ([double]$hostSummary.hostBranchCoverage -ge 72.39) `
    "Host 分支覆盖率 $($hostSummary.hostBranchCoverage)% 低于 G7 的 72.39%。"

if (-not $ReuseVerifiedClassicGameGate) {
    Invoke-PwshChecked $classicGate @('-Configuration', $Configuration, '-ReuseNuGetCache')
}
Assert-True (Test-Path -LiteralPath $classicSummaryPath -PathType Leaf) `
    'ClassicGame G8 门禁没有生成 summary.json。'
$classicSummary = Get-Content -Raw -LiteralPath $classicSummaryPath | ConvertFrom-Json
Assert-True ($classicSummary.stage -ceq 'WorkbenchCommandG8') `
    'ClassicGame 摘要阶段不是 WorkbenchCommandG8。'
Assert-True ([int]$classicSummary.tests.failed -eq 0 -and
    [int]$classicSummary.tests.skipped -eq 0 -and
    [int]$classicSummary.tests.passed -ge 526) `
    'ClassicGame G8 测试摘要不完整。'
Assert-True ([double]$classicSummary.gomokuDocumentLineCoverage -eq 100) `
    'ClassicGame GomokuDocument 关键覆盖率未达到 100%。'
Assert-True ([double]$classicSummary.workbenchDocumentCommandAdapterLineCoverage -eq 100) `
    'ClassicGame WorkbenchDocumentCommandAdapter 关键覆盖率未达到 100%。'

if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null
$targetedRoot = Join-Path $resultRoot 'targeted'
New-Item -ItemType Directory -Path $targetedRoot | Out-Null
$previousClassicRoot = $env:MYAVALONIA_WORKBENCH_COMMAND_G8_CLASSIC_GAME_PLUGIN_ROOT
try {
    $env:MYAVALONIA_WORKBENCH_COMMAND_G8_CLASSIC_GAME_PLUGIN_ROOT =
        [string]$classicSummary.hostInputRoot
    Invoke-DotnetChecked @(
        'test', 'Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj',
        '-c', $Configuration, '--no-build', '--no-restore', '-m:1',
        '--filter', 'FullyQualifiedName~WorkbenchCommandG8ClassicGameExternalPackageTests',
        '--results-directory', $targetedRoot,
        '--logger', 'trx;LogFileName=WorkbenchCommandG8.Plugin.trx')
    Invoke-DotnetChecked @(
        'test', 'Host/MyAvaloniaManagement.UiTests/MyAvaloniaManagement.UiTests.csproj',
        '-c', $Configuration, '--no-build', '--no-restore', '-m:1',
        '--filter', 'FullyQualifiedName~WorkbenchCommandG8ClassicGameUiTests',
        '--results-directory', $targetedRoot,
        '--logger', 'trx;LogFileName=WorkbenchCommandG8.Ui.trx')
}
finally {
    $env:MYAVALONIA_WORKBENCH_COMMAND_G8_CLASSIC_GAME_PLUGIN_ROOT = $previousClassicRoot
}

$pluginTests = Get-TrxCounts (Join-Path $targetedRoot 'WorkbenchCommandG8.Plugin.trx')
$uiTests = Get-TrxCounts (Join-Path $targetedRoot 'WorkbenchCommandG8.Ui.trx')
Assert-True ($pluginTests.passed -eq 1 -and $pluginTests.failed -eq 0 -and
    $pluginTests.skipped -eq 0) 'G8 真实包 PluginTests 未达到 1/1。'
Assert-True ($uiTests.passed -eq 1 -and $uiTests.failed -eq 0 -and
    $uiTests.skipped -eq 0) 'G8 真实包 Headless UI 未达到 1/1。'

Invoke-PwshChecked (Join-Path $PSScriptRoot 'Test-Documentation.ps1')

$summary = [ordered]@{
    schemaVersion = 1
    stage = 'WorkbenchCommandG8'
    configuration = $Configuration
    hostBaseGateReused = [bool]$ReuseVerifiedBaseGate
    classicGameGateReused = [bool]$ReuseVerifiedClassicGameGate
    hostPassed = [int]$hostSummary.hostPassed
    hostLineCoverage = [double]$hostSummary.hostLineCoverage
    hostBranchCoverage = [double]$hostSummary.hostBranchCoverage
    classicGame = [ordered]@{
        tests = $classicSummary.tests
        lineCoverage = [double]$classicSummary.lineCoverage
        branchCoverage = [double]$classicSummary.branchCoverage
        gomokuDocumentLineCoverage = [double]$classicSummary.gomokuDocumentLineCoverage
        workbenchDocumentCommandAdapterLineCoverage =
            [double]$classicSummary.workbenchDocumentCommandAdapterLineCoverage
        archiveSha256 = [string]$classicSummary.archiveSha256
        packageFiles = [int]$classicSummary.packageFiles
        deterministicBuilds = [int]$classicSummary.deterministicBuilds
        manifest = $classicSummary.manifest
    }
    externalPackageTests = $pluginTests
    headlessUiTests = $uiTests
    thirteenDocumentsVerified = $true
    catalogCommandsVerified = 22
    twoGomokuDocumentsVerified = $true
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
    ($summary | ConvertTo-Json -Depth 12),
    [Text.UTF8Encoding]::new($false))
Write-Host (
    "Workbench Command G8 门禁通过：ClassicGame $($classicSummary.tests.passed) 项，" +
    "Host 真实包 $($pluginTests.passed) 项，Headless UI $($uiTests.passed) 项。")
& dotnet build-server shutdown | Out-Null
