[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\test-results\WorkflowActionG1'))
$packageRoot = Join-Path $resultRoot 'packages'
$coreShippedHash = '063BCB5852827612B0501C135D23FECD015069A6F7DDB409547157E4FA00F80F'
$uiShippedHash = 'B11FBE768C3AD04CA65CBF5128BF6FCE8C00058EBB24052D51FE5464A65AD803'

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
    param([string[]]$Arguments)
    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
    }
}

function Invoke-Pwsh {
    param([string]$Script, [string[]]$Arguments = @())
    & pwsh -NoProfile -File $Script @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$Script 失败，退出码：$LASTEXITCODE。"
    }
}

function Get-TrxPassed {
    param([string]$Path)
    [xml]$trx = Get-Content -LiteralPath $Path
    $counters = $trx.TestRun.ResultSummary.Counters
    Assert-True (
        [int]$counters.failed -eq 0 -and
        [int]$counters.notExecuted -eq 0 -and
        [int]$counters.executed -eq [int]$counters.passed) `
        "TRX 未做到全部执行、零失败、零跳过：$Path"
    return [int]$counters.passed
}

function Get-ApiEntries {
    param([string]$Path)
    $lines = @(Get-Content -LiteralPath $Path)
    Assert-True ($lines.Count -gt 0 -and $lines[0] -ceq '#nullable enable') `
        "API 文本缺少 nullable 头：$Path"
    return @($lines | Select-Object -Skip 1)
}

function Get-FileLineCoverage {
    param([object[]]$Classes, [string]$RelativePath)
    $matching = @($Classes | Where-Object {
        $_.filename.Replace('\', '/').EndsWith($RelativePath,
            [StringComparison]::OrdinalIgnoreCase)
    })
    Assert-True ($matching.Count -gt 0) "覆盖率中缺少关键文件：$RelativePath"
    $lines = @($matching | ForEach-Object { $_.lines.line } |
        Group-Object number | ForEach-Object {
            [pscustomobject]@{
                Covered = @($_.Group | Where-Object { [int]$_.hits -gt 0 }).Count -gt 0
            }
        })
    return [Math]::Round(100 * @($lines | Where-Object Covered).Count / $lines.Count, 2)
}

Assert-ChildPath $resultRoot $repositoryRoot 'G1 结果'
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

$suiteCounts = [ordered]@{}
$packageHashes = [ordered]@{}

Push-Location $repositoryRoot
try {
    # Release 在这里只表示本地优化编译配置；本脚本没有任何签名、上传、标签或发布入口。
    Invoke-DotNet @('tool', 'restore')
    Invoke-DotNet @('restore', 'MyAvaloniaManagement.sln', '--locked-mode', '--nologo')
    Invoke-DotNet @(
        'build', 'MyAvaloniaManagement.sln', '-c', $Configuration,
        '--no-restore', '--nologo', '-warnaserror', '-p:SkipPluginDeploy=true')

    Invoke-Pwsh (Join-Path $PSScriptRoot 'Test-WorkflowActionG0.ps1') `
        @('-Configuration', $Configuration, '-CandidateOnly')
    $g0Summary = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot 'artifacts\test-results\WorkflowActionG0\summary.json') |
        ConvertFrom-Json

    & (Join-Path $PSScriptRoot 'Invoke-MyAvaloniaManagementTests.ps1') `
        -Configuration $Configuration -NoRestore
    if ($LASTEXITCODE -ne 0) { throw 'Host 三层测试或覆盖率门禁失败。' }
    $hostSummary = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot 'artifacts\test-results\MyAvaloniaManagement\summary.json') |
        ConvertFrom-Json
    $suiteCounts.HostThreeLayer = [int]$hostSummary.passed

    $dedicatedSuites = [ordered]@{
        PluginSdk = @(
            'Host/MyAvaloniaManagement.PluginSdk.Tests/MyAvaloniaManagement.PluginSdk.Tests.csproj',
            'FullyQualifiedName~WorkflowActionContractTests')
        WorkflowActionG1Plugins = @(
            'Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj',
            'FullyQualifiedName~WorkflowActionG1IntegrationTests')
    }
    foreach ($suite in $dedicatedSuites.GetEnumerator()) {
        $suiteRoot = Join-Path $resultRoot $suite.Key
        New-Item -ItemType Directory -Path $suiteRoot -Force | Out-Null
        Invoke-DotNet @(
            'test', $suite.Value[0], '-c', $Configuration,
            '--no-restore', '--nologo', '-warnaserror', '-p:SkipPluginDeploy=true',
            '--filter', $suite.Value[1], '--results-directory', $suiteRoot,
            '--logger', "trx;LogFileName=$($suite.Key).trx")
        $suiteCounts[$suite.Key] = Get-TrxPassed (
            Join-Path $suiteRoot "$($suite.Key).trx")
    }

    $regressionProjects = [ordered]@{
        MyPlugTest = 'Plugins/MyPlugTest/MyPlugTest.Tests/MyPlugTest.Tests.csproj'
        DaTangAccountingHelp = 'Plugins/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug.Tests/DaTangAccountingHelpPlug.Tests.csproj'
        MySmallTools = 'Plugins/MySmallTools/MySmallTools.Tests/MySmallTools.Tests.csproj'
        BiliDownloader = 'Plugins/BiliDownloader/BiliDownloader.Tests/BiliDownloader.Tests.csproj'
    }
    foreach ($suite in $regressionProjects.GetEnumerator()) {
        $suiteRoot = Join-Path $resultRoot "regression\$($suite.Key)"
        New-Item -ItemType Directory -Path $suiteRoot -Force | Out-Null
        Invoke-DotNet @(
            'test', $suite.Value, '-c', $Configuration,
            '--no-restore', '--nologo', '-warnaserror', '-p:SkipPluginDeploy=true',
            '--results-directory', $suiteRoot,
            '--logger', "trx;LogFileName=$($suite.Key).trx")
        $suiteCounts[$suite.Key] = Get-TrxPassed (
            Join-Path $suiteRoot "$($suite.Key).trx")
    }

    Invoke-Pwsh (Join-Path $PSScriptRoot 'Test-PluginSdkCompatibility.ps1') `
        @('-Baseline', 'v3', '-Configuration', $Configuration)
    Invoke-Pwsh (Join-Path $PSScriptRoot 'Test-PluginSdkPackage.ps1') `
        @('-Configuration', $Configuration)

    foreach ($project in @(
            'Host/MyAvaloniaManagement.PluginSdk/MyAvaloniaManagement.PluginSdk.csproj',
            'Host/MyAvaloniaManagement.PluginSdk.UI/MyAvaloniaManagement.PluginSdk.UI.csproj')) {
        Invoke-DotNet @(
            'pack', $project, '-c', $Configuration, '--no-restore', '--nologo',
            '-o', $packageRoot)
    }
    foreach ($package in Get-ChildItem -LiteralPath $packageRoot -Filter '*.nupkg' -File |
        Where-Object Name -NotLike '*.snupkg') {
        $packageHashes[$package.Name] = (
            Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
    }
    Assert-True ($packageHashes.Count -eq 2) 'G1 候选 Core/UI nupkg 数量不正确。'

    & (Join-Path $PSScriptRoot 'Test-Documentation.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'G1 文档链接与事实门禁失败。' }

    $coreShipped = Join-Path $repositoryRoot (
        'Host\MyAvaloniaManagement.PluginSdk\ApiCompatibility\v3\PublicAPI.Shipped.txt')
    $uiShipped = Join-Path $repositoryRoot (
        'Host\MyAvaloniaManagement.PluginSdk.UI\ApiCompatibility\v3\PublicAPI.Shipped.txt')
    $coreUnshipped = Join-Path $repositoryRoot (
        'Host\MyAvaloniaManagement.PluginSdk\ApiCompatibility\v3\PublicAPI.Unshipped.txt')
    $uiUnshipped = Join-Path $repositoryRoot (
        'Host\MyAvaloniaManagement.PluginSdk.UI\ApiCompatibility\v3\PublicAPI.Unshipped.txt')
    Assert-True ((Get-FileHash $coreShipped).Hash -ceq $coreShippedHash) `
        'Core v3 Shipped 哈希漂移。'
    Assert-True ((Get-FileHash $uiShipped).Hash -ceq $uiShippedHash) `
        'UI v3 Shipped 哈希漂移。'

    $coveragePath = Join-Path $repositoryRoot (
        'artifacts\test-results\MyAvaloniaManagement\coverage\Cobertura.xml')
    [xml]$coverage = Get-Content -LiteralPath $coveragePath
    $classes = @($coverage.coverage.packages.package.classes.class)
    $criticalCoverage = [ordered]@{}
    foreach ($relativePath in @(
            'Business/WorkflowActions/WorkflowActionSchemaValidator.cs',
            'Business/WorkflowActions/WorkflowActionCatalogStore.cs',
            'Business/WorkflowActions/WorkflowActionRuntime.cs',
            'Business/WorkflowActions/WorkflowActionShutdownGate.cs')) {
        $actual = Get-FileLineCoverage $classes $relativePath
        Assert-True ($actual -ge 90.0) "$relativePath 行覆盖率 $actual% 低于 90%。"
        $criticalCoverage[$relativePath] = $actual
    }

    $summary = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        productVersion = '3.0.0'
        sdkVersion = '3.1.0'
        configuration = $Configuration
        api = [ordered]@{
            coreShippedEntries = (Get-ApiEntries $coreShipped).Count
            uiShippedEntries = (Get-ApiEntries $uiShipped).Count
            coreUnshippedEntries = (Get-ApiEntries $coreUnshipped).Count
            uiUnshippedEntries = (Get-ApiEntries $uiUnshipped).Count
            coreShippedSha256 = $coreShippedHash
            uiShippedSha256 = $uiShippedHash
        }
        tests = $suiteCounts
        hostCoverage = [ordered]@{
            line = [double]$hostSummary.lineCoverage
            branch = [double]$hostSummary.branchCoverage
            criticalFiles = $criticalCoverage
        }
        oldPluginArchiveSha256 = $g0Summary.oldPluginArchiveSha256
        candidatePackageSha256 = $packageHashes
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
        ($summary | ConvertTo-Json -Depth 12),
        [Text.UTF8Encoding]::new($false))
    Write-Host "[Workflow Action G1] 非发布门禁通过。摘要：$resultRoot\summary.json"
}
finally {
    Pop-Location
}
