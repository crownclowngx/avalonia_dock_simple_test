[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$WorkflowStudioRoot,
    [ValidateRange(1, 100)]
    [int]$HarnessCycles = 20
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\test-results\WorkflowActionG4'))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\test-results'))
if (-not $resultRoot.StartsWith(
        $allowedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "G4 结果目录越界：$resultRoot。"
}
if ([string]::IsNullOrWhiteSpace($WorkflowStudioRoot)) {
    $WorkflowStudioRoot = Join-Path $repositoryRoot `
        '..\avalonia_dock_plug_test\myavalonia-workflow-studio'
}
$WorkflowStudioRoot = [IO.Path]::GetFullPath($WorkflowStudioRoot)
$studioGate = Join-Path $WorkflowStudioRoot 'scripts\Test-WorkflowStudioG3.1.ps1'
$studioProject = Join-Path $WorkflowStudioRoot `
    'src\WorkflowStudio.Plugin\WorkflowStudio.Plugin.csproj'
if (-not (Test-Path -LiteralPath $studioGate -PathType Leaf) -or
    -not (Test-Path -LiteralPath $studioProject -PathType Leaf)) {
    throw "外部 Workflow Studio 仓库不完整：$WorkflowStudioRoot。"
}

function Assert-True {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-Checked {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
    }
}

if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$previousPluginRoot = $env:MYAVALONIA_WORKFLOW_G4_PLUGIN_ROOT
$previousMediaPath = $env:MYAVALONIA_WORKFLOW_G4_MEDIA_PATH
Push-Location $repositoryRoot
try {
    # G4 是纯本地开发门禁。Release 只表示编译配置；本脚本不读取 AIFLOW，不调用
    # Windows CI/Smoke、ReleaseAcceptance、发布门禁、签名、标签或上传命令。
    & (Join-Path $PSScriptRoot 'Test-MySmallToolsV3.ps1') `
        -Configuration $Configuration `
        -HarnessCycles $HarnessCycles
    if ($LASTEXITCODE -ne 0) {
        throw "MySmallTools 既有开发门禁失败，退出码：$LASTEXITCODE。"
    }

    $candidateHostRoot = Join-Path $repositoryRoot (
        "Host\MyAvaloniaManagement\bin\$Configuration\net10.0")
    Assert-True (Test-Path -LiteralPath (
            Join-Path $candidateHostRoot 'MyAvaloniaManagement.exe') -PathType Leaf) `
        'G4 外部 Studio 验收缺少候选 Host 可执行输出。'
    & $studioGate `
        -Configuration $Configuration `
        -CandidateHostRoot $candidateHostRoot `
        -PublicOnly `
        -SkipCandidateHost
    if ($LASTEXITCODE -ne 0) {
        throw "Workflow Studio G3.1 公开源开发门禁失败，退出码：$LASTEXITCODE。"
    }

    $mySmallSummaryPath = Join-Path $repositoryRoot `
        'artifacts\test-results\MySmallToolsV3\summary.json'
    $studioResultRoot = Join-Path $WorkflowStudioRoot `
        'artifacts\test-results\WorkflowStudioG31'
    $studioSummaryPath = Join-Path $studioResultRoot 'summary.json'
    Assert-True (Test-Path -LiteralPath $mySmallSummaryPath -PathType Leaf) `
        'MySmallTools 开发门禁没有生成摘要。'
    Assert-True (Test-Path -LiteralPath $studioSummaryPath -PathType Leaf) `
        'Workflow Studio 开发门禁没有生成摘要。'
    $mySmallSummary = Get-Content -Raw -LiteralPath $mySmallSummaryPath | ConvertFrom-Json
    $studioSummary = Get-Content -Raw -LiteralPath $studioSummaryPath | ConvertFrom-Json
    Assert-True (
        [int]$mySmallSummary.failed -eq 0 -and
        [int]$mySmallSummary.skipped -eq 0 -and
        [int]$mySmallSummary.deterministicBuilds -eq 2 -and
        $mySmallSummary.manifest.pluginId -ceq 'myavalonia.plugin.my-small-tools' -and
        $mySmallSummary.manifest.pluginVersion -ceq '3.1.0' -and
        $mySmallSummary.manifest.sdkMinInclusive -ceq '3.2.0') `
        'MySmallTools G4 候选摘要的测试、版本或 SDK 事实不正确。'
    Assert-True (
        [int]$studioSummary.failed -eq 0 -and
        [int]$studioSummary.skipped -eq 0 -and
        [int]$studioSummary.deterministicBuilds -eq 2 -and
        $studioSummary.manifest.pluginId -ceq 'myavalonia.plugin.workflow-studio' -and
        $studioSummary.manifest.pluginVersion -ceq '1.1.0' -and
        $studioSummary.manifest.sdk.minInclusive -ceq '3.2.0') `
        'Workflow Studio 摘要的测试、版本或 SDK 事实不正确。'

    $integrationRoot = Join-Path $resultRoot 'integration-plugins'
    New-Item -ItemType Directory -Path $integrationRoot | Out-Null
    $mySmallLoad = Join-Path $repositoryRoot `
        'artifacts\test-results\MySmallToolsV3\package-load\Controls\SmallTools'
    Assert-True (Test-Path -LiteralPath $mySmallLoad -PathType Container) `
        'MySmallTools 开发门禁缺少可加载测试包。'
    $controlsRoot = Join-Path $integrationRoot 'Controls'
    New-Item -ItemType Directory -Path $controlsRoot | Out-Null
    Copy-Item -LiteralPath $mySmallLoad -Destination $controlsRoot -Recurse

    $studioZips = @(Get-ChildItem -LiteralPath (
            Join-Path $studioResultRoot 'package-1') -Filter '*.zip' -File)
    Assert-True ($studioZips.Count -eq 1) 'Workflow Studio 第一次构建没有唯一 ZIP。'
    Expand-Archive -LiteralPath $studioZips[0].FullName -DestinationPath $integrationRoot
    Assert-True (
        @(Get-ChildItem -LiteralPath $controlsRoot -Directory).Count -eq 2) `
        'G4 隔离 Controls 没有恰好包含两个真实插件。'

    # 只读取 Git 跟踪的生产文本。构建后 src 下包含大量 bin/obj 二进制；把它们当文本读取
    # 既会制造误报，也可能耗尽 PowerShell 内存。
    $studioProductionFiles = @(& git -C $WorkflowStudioRoot -c core.quotepath=false `
            ls-files -- 'src') | Where-Object {
        [IO.Path]::GetExtension($_) -in @(
            '.cs', '.csproj', '.axaml', '.props', '.targets', '.json', '.md')
    }
    Assert-True ($LASTEXITCODE -eq 0 -and $studioProductionFiles.Count -gt 0) `
        '无法枚举 Workflow Studio 的跟踪生产文本。'
    $studioProductionText = $studioProductionFiles | ForEach-Object {
        Get-Content -Raw -LiteralPath (Join-Path $WorkflowStudioRoot $_)
    }
    Assert-True (-not (($studioProductionText -join "`n") -match
            'MySmallTools|my-small-tools|encrypt-video')) `
        'Workflow Studio 生产代码出现 MySmallTools 预设或业务特判。'
    $actionFile = Join-Path $repositoryRoot (
        'Plugins\MySmallTools\MySmallTools\Business\SecretVideoPlayer\Workflow\' +
        'EncryptVideoWorkflowAction.cs')
    $actionText = Get-Content -Raw -LiteralPath $actionFile
    Assert-True ($actionText -notmatch
        'File\.Delete|DeletesLocalFiles|deleteSource|IServiceProvider|ViewModel') `
        'G4 Action 出现删除、服务定位或 UI 依赖。'
    $mySmallProjectText = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot 'Plugins\MySmallTools\MySmallTools\MySmallTools.csproj')
    Assert-True ($mySmallProjectText -notmatch 'PluginSdk\.Workflow') `
        'MySmallTools 生产项目不应新增 Workflow SDK 依赖。'

    $env:MYAVALONIA_WORKFLOW_G4_PLUGIN_ROOT = $controlsRoot
    $env:MYAVALONIA_WORKFLOW_G4_MEDIA_PATH = Join-Path $repositoryRoot (
        'Plugins\MySmallTools\MySmallTools.Tests\TestAssets\RealMedia\' +
        'synthetic-av-short.mp4')
    $integrationResults = Join-Path $resultRoot 'integration-tests'
    Invoke-Checked dotnet @(
        'test',
        'Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj',
        '-c', $Configuration,
        '-p:SkipPluginDeploy=true',
        '--no-restore',
        '--results-directory', $integrationResults,
        '--logger', 'trx;LogFileName=WorkflowActionG4.trx',
        '--filter', 'FullyQualifiedName~WorkflowActionG4IntegrationTests')
    [xml]$integrationTrx = Get-Content -Raw -LiteralPath (
        Join-Path $integrationResults 'WorkflowActionG4.trx')
    $integrationCounters = $integrationTrx.TestRun.ResultSummary.Counters
    Assert-True (
        [int]$integrationCounters.failed -eq 0 -and
        [int]$integrationCounters.notExecuted -eq 0 -and
        [int]$integrationCounters.passed -eq 1) `
        'G4 真实双 ZIP 集成测试没有精确通过 1 项。'

    # 仓库仍有与 G4 无关的历史格式债务；门禁精确验证本阶段触及的 C# 文件，避免自动
    # 改写 Host、DaTang 或旧播放 Harness。全局脏空白另由 diff check 和既有阶段处理。
    $g4FormatFiles = @(
        'Plugins/MySmallTools/MySmallTools/Business/SecretVideoPlayer/Workflow/EncryptVideoWorkflowAction.cs',
        'Plugins/MySmallTools/MySmallTools/Constants/MySmallToolsContributionIds.cs',
        'Plugins/MySmallTools/MySmallTools/Plugin/MySmallToolsPluginModule.cs',
        'Plugins/MySmallTools/MySmallTools.Tests/G4WorkflowActionTests.cs',
        'Host/MyAvaloniaManagement.PluginTests/WorkflowActionG4IntegrationTests.cs',
        'Host/MyAvaloniaManagement.PluginTests/MySmallToolsV3AcceptanceTests.cs',
        'Host/MyAvaloniaManagement.PluginTests/VersionPolicyTests.cs')
    Invoke-Checked dotnet (@(
            'format', 'MyAvaloniaManagement.sln',
            '--verify-no-changes', '--no-restore', '--verbosity', 'minimal',
            '--include') + $g4FormatFiles)
    & (Join-Path $PSScriptRoot 'Test-Documentation.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "G4 文档门禁失败，退出码：$LASTEXITCODE。"
    }

    $evidenceText = Get-ChildItem -LiteralPath $resultRoot -File -Recurse |
        ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName -ErrorAction SilentlyContinue }
    Assert-True (-not (($evidenceText -join "`n").Contains(
            'G4-INTEGRATION-SECRET-MUST-NOT-LEAK',
            [StringComparison]::Ordinal))) `
        'G4 Secret canary 进入测试、诊断或摘要证据。'

    $summary = [ordered]@{
        schemaVersion = 1
        stage = 'G4'
        configuration = $Configuration
        passed = [int]$mySmallSummary.passed +
            [int]$studioSummary.passed + [int]$integrationCounters.passed
        failed = 0
        skipped = 0
        mySmallTools = [ordered]@{
            passed = [int]$mySmallSummary.passed
            lineCoverage = [double]$mySmallSummary.pluginCoverage.line
            branchCoverage = [double]$mySmallSummary.pluginCoverage.branch
            g4ActionLineCoverage = [double]$mySmallSummary.pluginCoverage.g4ActionLine
            archiveSha256 = [string]$mySmallSummary.archiveSha256
            deterministicBuilds = 2
            pluginVersion = '3.1.0'
        }
        workflowStudio = [ordered]@{
            passed = [int]$studioSummary.passed
            lineCoverage = [double]$studioSummary.lineCoverage
            branchCoverage = [double]$studioSummary.branchCoverage
            archiveSha256 = [string]$studioSummary.archiveSha256
            deterministicBuilds = 2
            pluginVersion = '1.1.0'
            productionPreset = $false
        }
        integration = [ordered]@{
            passed = [int]$integrationCounters.passed
            plugins = 2
            invocations = 2
            firstSucceeded = $true
            repeatedArgumentsRejected = $true
            sourceRetained = $true
            secretCanaryAbsent = $true
        }
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        published = $false
        uploaded = $false
        tagCreated = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))
    Write-Host (
        "Workflow Action G4 本地开发门禁通过：$($summary.passed) 项；" +
        "MySmallTools $($summary.mySmallTools.lineCoverage)%/" +
        "$($summary.mySmallTools.branchCoverage)%；真实双 ZIP 调用 2 次。")
    $global:LASTEXITCODE = 0
}
finally {
    $env:MYAVALONIA_WORKFLOW_G4_PLUGIN_ROOT = $previousPluginRoot
    $env:MYAVALONIA_WORKFLOW_G4_MEDIA_PATH = $previousMediaPath
    Pop-Location
}
