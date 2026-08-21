[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore,
    [ValidateRange(1, 100)]
    [int]$HarnessCycles = 20
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\MySmallToolsV2'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G11 结果目录不在仓库内：$resultRoot。"
}

# G11 是开发期迁移门禁：不读取或初始化 AIFLOW，也不调用 Windows CI/Smoke、
# ReleaseAcceptance、旧 Accept/Approve G11、发布总门禁、签名、上传或标签流程。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$suites = @(
    [pscustomobject]@{
        Name = 'G11-Plugin'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        Filter = 'FullyQualifiedName~MySmallToolsV2MigrationTests|FullyQualifiedName~CurrentManagedPluginLoadingTests|FullyQualifiedName~ManagedOnlyPluginLoadingTests|FullyQualifiedName~PluginCompatibilityTests|FullyQualifiedName~PluginHostBoundaryTests|FullyQualifiedName~PluginSdkDependencyBoundaryTests|FullyQualifiedName~VersionPolicyTests'
    },
    [pscustomobject]@{
        Name = 'G11-HeadlessUI'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        Filter = 'FullyQualifiedName~MySmallToolsV2UiTests|FullyQualifiedName~SmallToolsVisualTests|FullyQualifiedName~ApplicationAndWindowTests'
    },
    [pscustomobject]@{
        Name = 'G11-PluginSdk'
        Project = 'Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj'
        Filter = 'FullyQualifiedName~SdkBoundaryTests|FullyQualifiedName~DocumentAndDescriptorTests'
    },
    [pscustomobject]@{
        Name = 'G11-Business'
        Project = 'Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj'
        Filter = ''
    }
)

function Invoke-TestSuite {
    param([Parameter(Mandatory)] $Suite)

    $suiteDirectory = Join-Path $resultRoot $Suite.Name
    New-Item -ItemType Directory -Path $suiteDirectory | Out-Null
    $arguments = @(
        'test', $Suite.Project,
        '-c', $Configuration,
        '-p:SkipPluginDeploy=true',
        '--results-directory', $suiteDirectory,
        '--logger', "trx;LogFileName=$($Suite.Name).trx",
        '--logger', 'console;verbosity=minimal'
    )
    if (-not [string]::IsNullOrWhiteSpace($Suite.Filter)) {
        $arguments += @('--filter', $Suite.Filter)
    }
    if ($NoRestore) { $arguments += '--no-restore' }

    & dotnet @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$($Suite.Name) 失败，退出码：$LASTEXITCODE。"
    }

    $trxPath = Join-Path $suiteDirectory "$($Suite.Name).trx"
    [xml]$trx = Get-Content -LiteralPath $trxPath
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.failed -ne 0 -or
        [int]$counters.notExecuted -ne 0 -or
        [int]$counters.executed -ne [int]$counters.passed) {
        throw "$($Suite.Name) TRX 未全绿。"
    }
    return [int]$counters.passed
}

function Assert-RgAbsent {
    param(
        [Parameter(Mandatory)] [string]$Pattern,
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Message
    )
    & rg --quiet $Pattern $Path -g '*.cs' -g '*.csproj'
    if ($LASTEXITCODE -eq 0) { throw $Message }
    if ($LASTEXITCODE -gt 1) { throw "无法执行 G11 结构扫描：$Path。" }
}

$suiteSummary = [ordered]@{}
$totalPassed = 0
Push-Location $repositoryRoot
try {
    foreach ($suite in $suites) {
        $passed = Invoke-TestSuite $suite
        $suiteSummary[$suite.Name] = $passed
        $totalPassed += $passed
    }

    $pluginRoot = Join-Path $repositoryRoot 'Plugins\MySmallTools\MySmallTools'
    Assert-RgAbsent `
        'MyAvaloniaManagementCommon|LegacyPluginContracts|Dock\.Model|IDocumentCreationStrategy|IDocumentScopeFactory|DocumentTypeIdConstant|Legacy[A-Za-z]+DocumentId|Newtonsoft\.Json|MyAvaloniaManagement\.Business|MyAvaloniaManagement\.ViewModels' `
        $pluginRoot `
        'G11 MySmallTools 生产代码重新出现 Legacy、Dock、Strategy、旧 GUID 或 Host 实现依赖。'

    $projectPath = Join-Path $pluginRoot 'MySmallTools.csproj'
    foreach ($requiredReference in
        'MyAvaloniaManagement.PluginSdk.csproj',
        'MyAvaloniaManagement.PluginSdk.UI.csproj') {
        & rg --quiet ([Regex]::Escape($requiredReference)) $projectPath
        if ($LASTEXITCODE -ne 0) { throw "G11 MySmallTools 缺少最终 SDK 引用：$requiredReference。" }
    }
    & rg --quiet '<ManagedPluginUseV2EntryContract>true</ManagedPluginUseV2EntryContract>' $projectPath
    if ($LASTEXITCODE -ne 0) { throw 'G11 MySmallTools 构建入口没有切换到最终 V2 IPluginModule。' }

    $harnessReport = Join-Path $resultRoot 'real-media-harness.json'
    $harnessArguments = @(
        'run', '--project',
        'Plugins\MySmallTools\MySmallTools.Playback.IntegrationHarness\MySmallTools.Playback.IntegrationHarness.csproj',
        '-c', $Configuration,
        '-p:SkipPluginDeploy=true'
    )
    if ($NoRestore) { $harnessArguments += '--no-restore' }
    $harnessArguments += @(
        '--', '--suite', 'g3', '--cycles', $HarnessCycles.ToString(),
        '--report', $harnessReport)
    & dotnet @harnessArguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'G11 真实媒体 Harness 失败。' }
    $harness = Get-Content -Raw -LiteralPath $harnessReport | ConvertFrom-Json
    if (-not $harness.success) { throw 'G11 真实媒体 Harness 报告未通过。' }

    $firstPackageRoot = Join-Path $resultRoot 'package-first'
    $secondPackageRoot = Join-Path $resultRoot 'package-second'
    & (Join-Path $PSScriptRoot 'Build-ManagedPluginPackage.ps1') `
        -Project $projectPath -Configuration $Configuration -OutputDirectory $firstPackageRoot
    if ($LASTEXITCODE -ne 0) { throw 'G11 第一次隔离测试 ZIP 构建失败。' }
    & (Join-Path $PSScriptRoot 'Build-ManagedPluginPackage.ps1') `
        -Project $projectPath -Configuration $Configuration -OutputDirectory $secondPackageRoot
    if ($LASTEXITCODE -ne 0) { throw 'G11 第二次隔离测试 ZIP 构建失败。' }

    $baseName = 'MySmallTools-2.0.0-win-x64'
    $firstSidecar = Get-Content -Raw -LiteralPath `
        (Join-Path $firstPackageRoot "$baseName.manifest.json") | ConvertFrom-Json
    $secondSidecar = Get-Content -Raw -LiteralPath `
        (Join-Path $secondPackageRoot "$baseName.manifest.json") | ConvertFrom-Json
    if ($firstSidecar.archive.sha256 -ne $secondSidecar.archive.sha256) {
        throw 'G11 两次隔离测试 ZIP 的归档摘要不一致。'
    }
    $firstFiles = @($firstSidecar.files | ForEach-Object {
        "$($_.path)|$($_.length)|$($_.sha256)"
    })
    $secondFiles = @($secondSidecar.files | ForEach-Object {
        "$($_.path)|$($_.length)|$($_.sha256)"
    })
    if (Compare-Object $firstFiles $secondFiles) {
        throw 'G11 两次隔离测试 ZIP 的文件事实不一致。'
    }
    $forbiddenPackageFiles = @($firstSidecar.files.path | Where-Object {
        $_ -match '(^|/)(?:MyAvaloniaManagement(?:Common|\.PluginSdk(?:\.UI)?)?|Avalonia(?:\.|$)|Dock\.|Newtonsoft\.Json|Microsoft\.Extensions\.).*\.dll$'
    })
    if ($forbiddenPackageFiles.Count -ne 0) {
        throw "G11 测试 ZIP 混入宿主共享程序集：$($forbiddenPackageFiles -join ', ')"
    }

    $packageLoadRoot = Join-Path $resultRoot 'package-load'
    Expand-Archive -LiteralPath (Join-Path $firstPackageRoot "$baseName.zip") `
        -DestinationPath $packageLoadRoot
    $variableName = 'MYAVALONIA_MYSMALLTOOLS_V2_PACKAGE_ROOT'
    $previousPackageRoot = [Environment]::GetEnvironmentVariable($variableName)
    try {
        [Environment]::SetEnvironmentVariable(
            $variableName,
            (Join-Path $packageLoadRoot 'Controls'))
        $zipSuite = [pscustomobject]@{
            Name = 'G11-FinalZip'
            Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
            Filter = 'FullyQualifiedName~G11最终测试Zip通过真实发现组合并发布四个Document'
        }
        $zipPassed = Invoke-TestSuite $zipSuite
        $suiteSummary[$zipSuite.Name] = $zipPassed
        $totalPassed += $zipPassed
    }
    finally {
        [Environment]::SetEnvironmentVariable($variableName, $previousPackageRoot)
    }

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        suites = $suiteSummary
        passed = $totalPassed
        failed = 0
        harness = [ordered]@{
            suite = 'g3'
            cycles = $HarnessCycles
            success = $true
            report = 'real-media-harness.json'
        }
        archiveSha256 = $firstSidecar.archive.sha256
        packageFiles = $firstSidecar.files.Count
        deterministicBuilds = 2
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
        ($summary | ConvertTo-Json -Depth 6),
        [Text.UTF8Encoding]::new($false))
    Write-Host "G11 MySmallTools V2 专项门禁通过：$totalPassed 项；测试 ZIP $($firstSidecar.files.Count) 个文件。"
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
