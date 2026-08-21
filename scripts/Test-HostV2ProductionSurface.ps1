[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\HostV2ProductionSurface'))
$artifactPrefix = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts')).TrimEnd('\') + '\'
if (-not $resultRoot.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G13 结果目录必须位于仓库 artifacts 下：$resultRoot"
}

if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

function Invoke-DotNetChecked {
    param([Parameter(Mandatory)] [string[]]$Arguments)

    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') 失败，退出码 $LASTEXITCODE。"
    }
}

function Invoke-RepositoryScript {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [hashtable]$Parameters
    )

    $scriptPath = Join-Path $PSScriptRoot $Name
    & $scriptPath @Parameters
}

function Assert-PatternAbsent {
    param(
        [Parameter(Mandatory)] [string]$Pattern,
        [Parameter(Mandatory)] [string[]]$Paths,
        [Parameter(Mandatory)] [string]$Message,
        [string[]]$Globs = @()
    )

    $arguments = @('-n', $Pattern) + $Paths
    foreach ($glob in $Globs) {
        $arguments += @('-g', $glob)
    }
    & rg @arguments | Out-Host
    if ($LASTEXITCODE -eq 0) {
        throw $Message
    }
    if ($LASTEXITCODE -ne 1) {
        throw "G13 源码扫描执行失败，退出码 $LASTEXITCODE。"
    }
}

function Invoke-TestProject {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Project
    )

    $suiteRoot = Join-Path $resultRoot $Name
    New-Item -ItemType Directory -Path $suiteRoot | Out-Null
    Invoke-DotNetChecked @(
        'test', (Join-Path $repositoryRoot $Project),
        '-c', $Configuration,
        '--no-build', '--no-restore',
        '-p:SkipPluginDeploy=true',
        '--results-directory', $suiteRoot,
        '--logger', "trx;LogFileName=$Name.trx",
        '--logger', 'console;verbosity=minimal')

    [xml]$trx = Get-Content -LiteralPath (Join-Path $suiteRoot "$Name.trx")
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0) {
        throw "$Name 没有全部通过。"
    }
    return [int]$counters.passed
}

Push-Location $repositoryRoot
try {
    if (-not $NoRestore) {
        Invoke-DotNetChecked @('restore', 'MyAvaloniaManagement.sln', '--locked-mode')
    }

    # G13 的源码白名单只覆盖活动生产面和统一构建入口。历史文档、v1 API 文本与
    # 专项负例必须继续描述被删除的名称，因此不能用全仓库关键词删除伪造“零残留”。
    $productionPaths = @(
        'Host/MyAvaloniaManagement',
        'Host/MyAvaloniaManagement.PluginSdk',
        'Host/MyAvaloniaManagement.PluginSdk.UI',
        'Plugins/BiliDownloader/BiliDownloader',
        'Plugins/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug',
        'Plugins/MyPlugTest/MyPlugTest',
        'Plugins/MySmallTools/MySmallTools',
        'build/MyAvaloniaManagement.ManagedPlugin.props',
        'build/MyAvaloniaManagement.ManagedPlugin.targets',
        'scripts/Build-ManagedPluginPackage.ps1')
    Assert-PatternAbsent `
        'MyAvaloniaManagementCommon|LegacyPluginContracts|ManagedPluginUseV2EntryContract|ManagedPluginHostApi|ManagedPluginCommonContract' `
        $productionPaths `
        '活动生产面重新出现 V1 契约、Legacy 项目或过渡构建属性。' `
        @('*.cs', '*.csproj', '*.props', '*.targets', '*.ps1')
    Assert-PatternAbsent `
        'Newtonsoft\.Json' `
        @('Host/MyAvaloniaManagement') `
        'Host 生产项目重新引入 Newtonsoft。' `
        @('*.cs', '*.csproj')

    if (Test-Path -LiteralPath `
        (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.LegacyPluginContracts\MyAvaloniaManagement.LegacyPluginContracts.csproj')) {
        throw 'LegacyPluginContracts 项目重新出现。'
    }

    Invoke-DotNetChecked @(
        'build', 'MyAvaloniaManagement.sln',
        '-c', $Configuration,
        '--no-restore',
        '-p:SkipPluginDeploy=true',
        '-p:TreatWarningsAsErrors=true')

    # deps.json 来自本次构建解析后的真实运行闭包，不受旧 bin 目录中无引用文件影响。
    # 对 Host 与四插件逐一检查可同时覆盖项目引用和传递包回流。
    $dependencyManifests = @(
        "Host/MyAvaloniaManagement/bin/$Configuration/net10.0/MyAvaloniaManagement.deps.json"
        "Plugins/BiliDownloader/BiliDownloader/bin/$Configuration/net10.0/BiliDownloader.deps.json"
        "Plugins/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug/bin/$Configuration/net10.0/DaTangAccountingHelpPlug.deps.json"
        "Plugins/MyPlugTest/MyPlugTest/bin/$Configuration/net10.0/MyPlugTest.deps.json"
        "Plugins/MySmallTools/MySmallTools/bin/$Configuration/net10.0/MySmallTools.deps.json"
    )
    foreach ($manifest in $dependencyManifests) {
        $manifestPath = Join-Path $repositoryRoot $manifest
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "本次构建缺少依赖清单：$manifest"
        }
        $manifestText = [IO.File]::ReadAllText($manifestPath)
        if ($manifestText -match 'MyAvaloniaManagementCommon|LegacyPluginContracts|Newtonsoft\.Json') {
            throw "运行依赖闭包仍包含 V1 或 Newtonsoft：$manifest"
        }
    }

    $hostParameters = @{ Configuration = $Configuration; NoRestore = $true }
    Invoke-RepositoryScript 'Invoke-MyAvaloniaManagementTests.ps1' $hostParameters
    $hostSummary = Get-Content -Raw -LiteralPath `
        (Join-Path $repositoryRoot 'artifacts\test-results\MyAvaloniaManagement\summary.json') |
        ConvertFrom-Json

    $suiteResults = [ordered]@{}
    $suiteResults.PluginSdk = Invoke-TestProject 'PluginSdk' `
        'Host/MyAvaloniaManagement.PluginSdk.Tests/MyAvaloniaManagement.PluginSdk.Tests.csproj'
    $suiteResults.DaTang = Invoke-TestProject 'DaTang' `
        'Plugins/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug.Tests/DaTangAccountingHelpPlug.Tests.csproj'
    $suiteResults.MySmallTools = Invoke-TestProject 'MySmallTools' `
        'Plugins/MySmallTools/MySmallTools.Tests/MySmallTools.Tests.csproj'
    $suiteResults.BiliDownloader = Invoke-TestProject 'BiliDownloader' `
        'Plugins/BiliDownloader/BiliDownloader.Tests/BiliDownloader.Tests.csproj'

    Invoke-RepositoryScript 'Test-PluginSdkPackage.ps1' `
        @{ Configuration = $Configuration }
    Invoke-RepositoryScript 'Test-ManagedPluginPackages.ps1' @{
        Configuration = $Configuration
        ResultsDirectory = 'artifacts/test-results/HostV2ProductionSurface/ManagedPluginPackages'
    }
    Invoke-RepositoryScript 'Test-HostDiagnosticRedaction.ps1' @{}
    Invoke-RepositoryScript 'Test-Documentation.ps1' @{}

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        host = $hostSummary
        additionalSuites = $suiteResults
        gates = [ordered]@{
            sourceScan = $true
            warnAsErrorBuild = $true
            sdkPackageAndCompileNegatives = $true
            deterministicPluginPackageMatrix = $true
            diagnosticRedaction = $true
            documentation = $true
        }
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
        ($summary | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))
    Write-Host "G13 非发布生产面门禁通过：$resultRoot"
}
finally {
    Pop-Location
}
