[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\RevisionedDocumentSave'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G2 结果目录不在仓库内：$resultRoot。"
}

# G2 是开发阶段的本地非发布门禁。各测试进程串行运行，避免 Host 构建输出、
# 插件部署目录和 Headless 资源互相干扰。本脚本不读取或初始化 AIFLOW，不调用
# Windows CI/Smoke、ReleaseAcceptance、发布总门禁、签名、上传、标签或发布操作。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$suites = @(
    [pscustomobject]@{
        Name = 'G2-Sdk'
        Project = 'Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj'
        Filter = 'FullyQualifiedName~DocumentAndDescriptorTests|FullyQualifiedName~SdkBoundaryTests'
    },
    [pscustomobject]@{
        Name = 'G2-Host'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
        Filter = 'FullyQualifiedName~DocumentPersistenceV2Tests|FullyQualifiedName~DocumentCloseV2Tests'
    },
    [pscustomobject]@{
        Name = 'G2-PluginIntegration'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        Filter = 'FullyQualifiedName~MyPlugTestV3AcceptanceTests|FullyQualifiedName~DaTangAccountingHelpPlugV3AcceptanceTests'
    },
    [pscustomobject]@{
        Name = 'G2-MyPlugTest'
        Project = 'Plugins\MyPlugTest\MyPlugTest.Tests\MyPlugTest.Tests.csproj'
        Filter = 'FullyQualifiedName~RevisionedDocumentSaveTests'
    },
    [pscustomobject]@{
        Name = 'G2-BiliDownloader'
        Project = 'Plugins\BiliDownloader\BiliDownloader.Tests\BiliDownloader.Tests.csproj'
        Filter = 'FullyQualifiedName~BiliDownloaderV3AcceptanceTests|FullyQualifiedName~DocumentV3G4Tests|FullyQualifiedName~DocumentV2G5Tests'
    }
)

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
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
        'Plugins\BiliDownloader\BiliDownloader'
    )
    & rg --quiet 'CaptureContentAsync|AcceptChanges\s*\(\s*\)' @productionPaths -g '*.cs'
    Assert-True ($LASTEXITCODE -eq 1) 'G2 生产源码重新出现旧捕获方法或无参 AcceptChanges。'

    $implementers = @(& rg -l `
        'public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync' `
        'Plugins\MyPlugTest\MyPlugTest' `
        'Plugins\DaTangAccountingHelpPlug\DaTangAccountingHelpPlug' `
        'Plugins\BiliDownloader\BiliDownloader' -g '*.cs')
    Assert-True ($LASTEXITCODE -eq 0 -and $implementers.Count -eq 3) `
        'G2 必须且只能迁移三个真实可持久化插件 Document。'

    $saveService = 'Host\MyAvaloniaManagement\Business\Documents\DocumentSaveService.cs'
    & rg --quiet 'AcceptChanges\(snapshot\.Revision\)' $saveService
    Assert-True ($LASTEXITCODE -eq 0) 'Host SaveService 没有把捕获 Revision 原样交还插件。'
    & rg --quiet 'snapshot\.Content' $saveService
    Assert-True ($LASTEXITCODE -eq 0) 'Host SaveService 没有只序列化修订快照中的 Content。'

    $api = Get-Content -LiteralPath `
        'Host\MyAvaloniaManagement.PluginSdk\ApiCompatibility\v3\PublicAPI.Unshipped.txt' -Raw
    Assert-True ($api.Contains('DocumentSaveSnapshot', [StringComparison]::Ordinal)) `
        'v3 Unshipped 缺少 DocumentSaveSnapshot。'
    Assert-True (-not $api.Contains('CaptureContentAsync', [StringComparison]::Ordinal)) `
        'v3 Unshipped 仍包含旧 CaptureContentAsync。'

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        suites = $suiteSummary
        passed = $totalPassed
        failed = 0
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 4),
        [Text.UTF8Encoding]::new($false))
    Write-Host "G2 修订化 Document 保存专项门禁通过：$totalPassed 项。"
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
