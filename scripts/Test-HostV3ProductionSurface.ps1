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
    'artifacts\test-results\HostV3ProductionSurface'))
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

    # 仓库脚本统一以 PowerShell 异常表示自身失败；部分反向编译门禁会有意运行退出码非零的
    # dotnet 子进程，并在验证诊断后正常返回，因此这里不能再次读取残留的全局 LASTEXITCODE。
    & (Join-Path $PSScriptRoot $Name) @Parameters
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
    if ([int]$counters.failed -ne 0 -or
        [int]$counters.notExecuted -ne 0 -or
        [int]$counters.executed -ne [int]$counters.passed) {
        throw "$Name 没有做到零失败、零跳过。"
    }
    return [int]$counters.passed
}

Push-Location $repositoryRoot
try {
    if (-not $NoRestore) {
        Invoke-DotNetChecked @(
            'restore', 'MyAvaloniaManagement.sln', '--locked-mode',
            '-p:SkipPluginDeploy=true', '--nologo')
    }

    # 只扫描活动生产根与当前构建入口。历史 API 文本、历史文档和编译负例必须继续写出被删除名称，
    # 因而不能用全仓库关键词禁令伪造“零残留”。schema v2、layout-v2 与数据根 v2 也属于当前线格式，
    # 不在删除范围；下面的表达式只针对已经被 V3 替代的类型、分支和 Dock Locator 行为。
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
        'CaptureContentAsync|DocumentActivationContext|IHostEventBus|HostEventBus|ManagementFactory|ToolManagementData|V2Owner|AppendHostContributions' `
        $productionPaths `
        '活动生产面重新出现已删除的 V2 类型、Facade、总线或 Host 伪插件分支。' `
        @('*.cs', '*.csproj', '*.props', '*.targets', '*.ps1')
    Assert-PatternAbsent `
        'DockableLocator[^\r\n]*("Files"|"Plug"|\["Files"\]|\["Plug"\])|GetDockable[^\r\n]*("Files"|"Plug")' `
        $productionPaths `
        '活动生产面重新注册或查询 Files/Plug Dock Locator。' `
        @('*.cs')
    Assert-PatternAbsent `
        '#if[^\r\n]*(V2|V3)|DefineConstants[^\r\n]*(V2|V3)|ManagedPluginUseV[23]|ManagedPlugin(V2|V3)(Fallback|Compatibility)' `
        $productionPaths `
        '活动生产面重新出现 V2/V3 条件编译、入口选择开关或隐藏 fallback。' `
        @('*.cs', '*.csproj', '*.props', '*.targets')

    # 这些名称表示旧 Host 阶段测试双，而不是当前仍受支持的 envelope/layout schema 2。
    # 当前测试和当前文档必须使用版本无关名称；历史目录由文档门禁的精确例外负责审计。
    Assert-PatternAbsent `
        'DocumentV2Test(Context|Probe)|DocumentPersistenceV2Tests|DocumentCloseV2Tests' `
        @('Host/MyAvaloniaManagement.Tests', 'scripts', 'README.md', 'docs', 'Host/MyAvaloniaManagement/docs') `
        '当前测试、脚本或文档重新引用 V2 Host 阶段测试双。' `
        @('*.cs', '*.ps1', '*.psm1', '*.md', '!Test-HostV3ProductionSurface.ps1')

    Invoke-DotNetChecked @(
        'build', 'MyAvaloniaManagement.sln',
        '-c', $Configuration, '--no-restore',
        '-p:SkipPluginDeploy=true',
        '-p:TreatWarningsAsErrors=true', '--nologo')

    # deps.json 是本次构建解析后的真实运行闭包。SDK 程序集名称跨主版本保持稳定，因此同时检查
    # 旧程序集、Legacy 项目和明确的 2.x SDK 库身份，不能只依赖源码扫描。
    $dependencyManifests = @(
        "Host/MyAvaloniaManagement/bin/$Configuration/net10.0/MyAvaloniaManagement.deps.json",
        "Plugins/BiliDownloader/BiliDownloader/bin/$Configuration/net10.0/BiliDownloader.deps.json",
        "Plugins/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug/bin/$Configuration/net10.0/DaTangAccountingHelpPlug.deps.json",
        "Plugins/MyPlugTest/MyPlugTest/bin/$Configuration/net10.0/MyPlugTest.deps.json",
        "Plugins/MySmallTools/MySmallTools/bin/$Configuration/net10.0/MySmallTools.deps.json")
    foreach ($manifest in $dependencyManifests) {
        $manifestPath = Join-Path $repositoryRoot $manifest
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "本次构建缺少依赖清单：$manifest"
        }
        $manifestText = [IO.File]::ReadAllText($manifestPath)
        if ($manifestText -match
            'MyAvaloniaManagementCommon|LegacyPluginContracts|MyAvaloniaManagement\.PluginSdk(?:\.UI)?/2\.') {
            throw "运行依赖闭包仍包含 Legacy/Common 或 V2 SDK：$manifest"
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
    $suiteResults.MyPlugTest = Invoke-TestProject 'MyPlugTest' `
        'Plugins/MyPlugTest/MyPlugTest.Tests/MyPlugTest.Tests.csproj'
    $suiteResults.DaTang = Invoke-TestProject 'DaTang' `
        'Plugins/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug.Tests/DaTangAccountingHelpPlug.Tests.csproj'
    $suiteResults.MySmallTools = Invoke-TestProject 'MySmallTools' `
        'Plugins/MySmallTools/MySmallTools.Tests/MySmallTools.Tests.csproj'
    $suiteResults.BiliDownloader = Invoke-TestProject 'BiliDownloader' `
        'Plugins/BiliDownloader/BiliDownloader.Tests/BiliDownloader.Tests.csproj'

    Invoke-RepositoryScript 'Test-PluginSdkCompatibility.ps1' @{
        Baseline = 'v3'
        Configuration = $Configuration
    }
    Invoke-RepositoryScript 'Test-PluginSdkPackage.ps1' @{ Configuration = $Configuration }
    Invoke-RepositoryScript 'Test-ManagedPluginPackages.ps1' @{
        Configuration = $Configuration
        ResultsDirectory = 'artifacts/test-results/HostV3ProductionSurface/ManagedPluginPackages'
    }
    Invoke-RepositoryScript 'Test-HostDiagnosticRedaction.ps1' @{}
    Invoke-RepositoryScript 'Test-DocumentationCore.ps1' @{}
    Invoke-RepositoryScript 'Test-Documentation.ps1' @{}

    $packageSummaryPath = Join-Path $resultRoot 'ManagedPluginPackages\summary.json'
    if (-not (Test-Path -LiteralPath $packageSummaryPath -PathType Leaf)) {
        throw '四插件包矩阵没有生成 summary.json。'
    }
    $packageSummary = Get-Content -Raw -LiteralPath $packageSummaryPath | ConvertFrom-Json

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        host = $hostSummary
        additionalSuites = $suiteResults
        managedPluginPackages = $packageSummary
        gates = [ordered]@{
            sourceScan = $true
            binaryDependencyScan = $true
            warnAsErrorBuild = $true
            apiV3Compatibility = $true
            sdkPackageAndCompileNegatives = $true
            deterministicPluginPackageMatrix = $true
            diagnosticRedaction = $true
            documentationCore = $true
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
        ($summary | ConvertTo-Json -Depth 12),
        [Text.UTF8Encoding]::new($false))
    Write-Host "G13 V3 非发布生产面门禁通过：$resultRoot"
}
finally {
    Pop-Location
}
