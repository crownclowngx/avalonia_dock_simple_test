[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$WorkflowStudioRoot,
    [switch]$ReuseVerifiedBaseGate,
    [switch]$ReuseVerifiedStudioGate
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($WorkflowStudioRoot)) {
    $WorkflowStudioRoot = Join-Path $repositoryRoot `
        '..\avalonia_management_plug\myavalonia-workflow-studio'
}
$WorkflowStudioRoot = [IO.Path]::GetFullPath($WorkflowStudioRoot)
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\WorkbenchCommandG7'))
$allowedResultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\test-results'))
$hostSummaryPath = Join-Path $repositoryRoot 'artifacts\test-results\HostV4\G7\summary.json'
$studioSummaryPath = Join-Path $WorkflowStudioRoot `
    'artifacts\test-results\WorkflowStudioG7\summary.json'
$studioGate = Join-Path $WorkflowStudioRoot 'scripts\Test-WorkflowStudioG7.ps1'

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
    'Workbench Command G7 结果目录越界。'
Assert-True (Test-Path -LiteralPath $studioGate -PathType Leaf) `
    "外部 WorkflowStudio G7 门禁不存在：$studioGate。"

if (-not $ReuseVerifiedBaseGate) {
    # 这是现有 V4 本地开发门禁，不是 Windows CI、Windows Smoke 或发布门禁。
    Invoke-PwshChecked (Join-Path $PSScriptRoot 'Test-HostV4DevelopmentGate.ps1') @(
        '-Stage', 'G7', '-Configuration', $Configuration)
}
Assert-True (Test-Path -LiteralPath $hostSummaryPath -PathType Leaf) `
    'Host V4 G7 开发门禁没有生成 summary.json。'
$hostSummary = Get-Content -Raw -LiteralPath $hostSummaryPath | ConvertFrom-Json
Assert-True ([bool]$hostSummary.passed) 'Host V4 G7 开发门禁摘要不是通过状态。'
Assert-True ([double]$hostSummary.hostLineCoverage -ge 86.98) `
    "Host 行覆盖率 $($hostSummary.hostLineCoverage)% 低于 Workbench Command G6 的 86.98%。"
Assert-True ([double]$hostSummary.hostBranchCoverage -ge 72.39) `
    "Host 分支覆盖率 $($hostSummary.hostBranchCoverage)% 低于 Workbench Command G6 的 72.39%。"

if (-not $ReuseVerifiedStudioGate) {
    Invoke-PwshChecked $studioGate @(
        '-Configuration', $Configuration, '-ReuseNuGetCache')
}
Assert-True (Test-Path -LiteralPath $studioSummaryPath -PathType Leaf) `
    'WorkflowStudio G7 门禁没有生成 summary.json。'
$studioSummary = Get-Content -Raw -LiteralPath $studioSummaryPath | ConvertFrom-Json
Assert-True ($studioSummary.stage -ceq 'WorkbenchCommandG7') `
    'WorkflowStudio 摘要阶段不是 WorkbenchCommandG7。'
Assert-True ([int]$studioSummary.tests.failed -eq 0 -and
    [int]$studioSummary.tests.skipped -eq 0 -and
    [int]$studioSummary.tests.passed -ge 54) `
    'WorkflowStudio G7 测试摘要不完整。'
Assert-True ([double]$studioSummary.mainDocumentLineCoverage -ge 90) `
    'WorkflowStudio MainDocument 关键覆盖率低于 90%。'

if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null
$combinedControls = Join-Path $resultRoot 'combined\Controls'
$studioTarget = Join-Path $combinedControls 'WorkflowStudio'
$actionTarget = Join-Path $combinedControls 'WorkflowActionG1Provider'
New-Item -ItemType Directory -Path $studioTarget, $actionTarget -Force | Out-Null
Copy-Item -Path (Join-Path ([string]$studioSummary.hostInputRoot) 'WorkflowStudio\*') `
    -Destination $studioTarget -Force
$actionFixture = Join-Path $repositoryRoot `
    "Host\MyAvaloniaManagement.PluginTests\bin\$Configuration\net10.0\WorkflowActionG1Fixtures\Provider"
Assert-True (Test-Path -LiteralPath (Join-Path $actionFixture 'plugin.manifest.json') -PathType Leaf) `
    'Host 测试输出缺少 WorkflowActionG1 Provider 真实夹具。'
Copy-Item -Path (Join-Path $actionFixture '*') -Destination $actionTarget -Force

$targetedRoot = Join-Path $resultRoot 'targeted'
New-Item -ItemType Directory -Path $targetedRoot | Out-Null
$previousStudioRoot = $env:MYAVALONIA_WORKBENCH_COMMAND_G7_WORKFLOW_PLUGIN_ROOT
$previousCombinedRoot = $env:MYAVALONIA_WORKBENCH_COMMAND_G7_WORKFLOW_WITH_ACTION_ROOT
try {
    $env:MYAVALONIA_WORKBENCH_COMMAND_G7_WORKFLOW_PLUGIN_ROOT =
        [string]$studioSummary.hostInputRoot
    $env:MYAVALONIA_WORKBENCH_COMMAND_G7_WORKFLOW_WITH_ACTION_ROOT = $combinedControls

    Invoke-DotnetChecked @(
        'test', 'Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj',
        '-c', $Configuration, '--no-build', '--no-restore', '-m:1',
        '--filter', 'FullyQualifiedName~WorkbenchCommandG7WorkflowStudioExternalPackageTests',
        '--results-directory', $targetedRoot,
        '--logger', 'trx;LogFileName=WorkbenchCommandG7.Plugin.trx')
    Invoke-DotnetChecked @(
        'test', 'Host/MyAvaloniaManagement.UiTests/MyAvaloniaManagement.UiTests.csproj',
        '-c', $Configuration, '--no-build', '--no-restore', '-m:1',
        '--filter', 'FullyQualifiedName~WorkbenchCommandG7WorkflowStudioUiTests',
        '--results-directory', $targetedRoot,
        '--logger', 'trx;LogFileName=WorkbenchCommandG7.Ui.trx')
}
finally {
    $env:MYAVALONIA_WORKBENCH_COMMAND_G7_WORKFLOW_PLUGIN_ROOT = $previousStudioRoot
    $env:MYAVALONIA_WORKBENCH_COMMAND_G7_WORKFLOW_WITH_ACTION_ROOT = $previousCombinedRoot
}

$pluginTests = Get-TrxCounts (Join-Path $targetedRoot 'WorkbenchCommandG7.Plugin.trx')
$uiTests = Get-TrxCounts (Join-Path $targetedRoot 'WorkbenchCommandG7.Ui.trx')
Assert-True ($pluginTests.passed -eq 2 -and $pluginTests.failed -eq 0 -and
    $pluginTests.skipped -eq 0) 'G7 真实包 PluginTests 未达到 2/2。'
Assert-True ($uiTests.passed -eq 1 -and $uiTests.failed -eq 0 -and
    $uiTests.skipped -eq 0) 'G7 真实包 Headless UI 未达到 1/1。'

Invoke-PwshChecked (Join-Path $PSScriptRoot 'Test-Documentation.ps1')

$summary = [ordered]@{
    schemaVersion = 1
    stage = 'WorkbenchCommandG7'
    configuration = $Configuration
    hostBaseGateReused = [bool]$ReuseVerifiedBaseGate
    studioGateReused = [bool]$ReuseVerifiedStudioGate
    hostPassed = [int]$hostSummary.hostPassed
    hostLineCoverage = [double]$hostSummary.hostLineCoverage
    hostBranchCoverage = [double]$hostSummary.hostBranchCoverage
    workflowStudio = [ordered]@{
        tests = $studioSummary.tests
        lineCoverage = [double]$studioSummary.lineCoverage
        branchCoverage = [double]$studioSummary.branchCoverage
        mainDocumentLineCoverage = [double]$studioSummary.mainDocumentLineCoverage
        archiveSha256 = [string]$studioSummary.archiveSha256
        packageFiles = [int]$studioSummary.packageFiles
        deterministicBuilds = [int]$studioSummary.deterministicBuilds
        manifest = $studioSummary.manifest
    }
    externalPackageTests = $pluginTests
    headlessUiTests = $uiTests
    callerBoundActionInvoked = $true
    twoStudioDocumentsVerified = $true
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
    "Workbench Command G7 门禁通过：Studio $($studioSummary.tests.passed) 项，" +
    "Host 真实包 $($pluginTests.passed) 项，Headless UI $($uiTests.passed) 项。")
& dotnet build-server shutdown | Out-Null
