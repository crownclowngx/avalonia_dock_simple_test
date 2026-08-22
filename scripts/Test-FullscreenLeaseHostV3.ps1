[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\FullscreenLeaseHostV3'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G8 结果目录不在仓库内：$resultRoot。"
}

# G8 只执行开发期本地非发布验证。测试进程串行运行，避免 Host 输出目录、Avalonia Headless
# 全局资源和插件部署目录相互污染。本脚本不读取、初始化或修改 AIFLOW，也不调用 Windows CI、
# Windows Smoke、ReleaseAcceptance、Accept/Approve/Release 脚本、发布门禁、签名、上传或标签流程。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$settings = Join-Path $repositoryRoot `
    'Host\MyAvaloniaManagement.Tests\coverage.runsettings'
$suites = @(
    [pscustomobject]@{
        Name = 'G8-Sdk'
        Project = 'Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj'
        HostCoverage = $false
    },
    [pscustomobject]@{
        Name = 'G8-Unit'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
        HostCoverage = $true
    },
    [pscustomobject]@{
        Name = 'G8-HeadlessUi'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        HostCoverage = $true
    },
    [pscustomobject]@{
        Name = 'G8-PluginDock'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        HostCoverage = $true
    },
    [pscustomobject]@{
        Name = 'G8-MySmallTools'
        Project = 'Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj'
        HostCoverage = $false
    }
)

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
    }
}

function Assert-PatternAbsent {
    param(
        [string]$Pattern,
        [string[]]$Paths,
        [string[]]$Globs = @('*.cs'),
        [string]$Message
    )
    $arguments = @('--quiet', $Pattern) + $Paths
    foreach ($glob in $Globs) { $arguments += @('-g', $glob) }
    & rg @arguments
    if ($LASTEXITCODE -eq 0) { throw $Message }
    if ($LASTEXITCODE -gt 1) { throw "无法执行 G8 结构扫描：$Pattern。" }
}

function Get-FileLineCoverage {
    param([object[]]$Classes, [string]$RelativePath)
    $matching = @($Classes | Where-Object {
        $_.filename.Replace('\', '/').EndsWith(
            $RelativePath,
            [StringComparison]::OrdinalIgnoreCase)
    })
    Assert-True ($matching.Count -gt 0) "覆盖率报告缺少 G8 关键文件：$RelativePath。"
    $lines = @($matching |
        ForEach-Object { $_.lines.line } |
        Group-Object number |
        ForEach-Object {
            [pscustomobject]@{
                Covered = @($_.Group | Where-Object { [int]$_.hits -gt 0 }).Count -gt 0
            }
        })
    if ($lines.Count -eq 0) { return 100.0 }
    [Math]::Round(100 * @($lines | Where-Object Covered).Count / $lines.Count, 2)
}

$suiteSummary = [ordered]@{}
$totalPassed = 0
Push-Location $repositoryRoot
try {
    Invoke-DotNet @('tool', 'restore')
    foreach ($suite in $suites) {
        $suiteDirectory = Join-Path $resultRoot $suite.Name
        New-Item -ItemType Directory -Path $suiteDirectory | Out-Null
        $arguments = @(
            'test', $suite.Project,
            '-c', $Configuration,
            '-p:SkipPluginDeploy=true',
            '-p:TreatWarningsAsErrors=true',
            '--results-directory', $suiteDirectory,
            '--logger', "trx;LogFileName=$($suite.Name).trx",
            '--logger', 'console;verbosity=minimal'
        )
        if ($suite.HostCoverage) {
            $arguments += @(
                '--settings', $settings,
                '--collect:XPlat Code Coverage')
        }
        if ($NoRestore) { $arguments += '--no-restore' }
        Invoke-DotNet $arguments

        $trxPath = Get-ChildItem -LiteralPath $suiteDirectory -Recurse `
            -Filter "$($suite.Name).trx" |
            Select-Object -First 1 -ExpandProperty FullName
        Assert-True (-not [string]::IsNullOrWhiteSpace($trxPath)) `
            "$($suite.Name) 缺少 TRX。"
        [xml]$trx = Get-Content -LiteralPath $trxPath
        $counters = $trx.TestRun.ResultSummary.Counters
        Assert-True (
            [int]$counters.failed -eq 0 -and
            [int]$counters.notExecuted -eq 0 -and
            [int]$counters.executed -eq [int]$counters.passed) `
            "$($suite.Name) TRX 未做到全部执行、零失败、零跳过。"
        $passed = [int]$counters.passed
        Assert-True ($passed -gt 0) "$($suite.Name) 没有实际执行测试。"
        $suiteSummary[$suite.Name] = $passed
        $totalPassed += $passed
    }

    $coverageReports = @(Get-ChildItem -LiteralPath $resultRoot -Recurse `
        -Filter coverage.cobertura.xml |
        Get-FileHash |
        Group-Object Hash |
        ForEach-Object { $_.Group[0].Path })
    Assert-True ($coverageReports.Count -eq 3) `
        "G8 预期三份 Host 覆盖率报告，实际为 $($coverageReports.Count) 份。"
    $coverageRoot = Join-Path $resultRoot 'coverage'
    Invoke-DotNet @(
        'reportgenerator',
        "-reports:$($coverageReports -join ';')",
        "-targetdir:$coverageRoot",
        '-reporttypes:Cobertura;JsonSummary',
        '-assemblyfilters:+MyAvaloniaManagement;-*.Tests',
        '-filefilters:-*/obj/*;-*.g.cs;-*.g.i.cs'
    )

    [xml]$coverage = Get-Content -LiteralPath (Join-Path $coverageRoot 'Cobertura.xml')
    $lineCoverage = [Math]::Round(100 * [double]$coverage.coverage.'line-rate', 2)
    $branchCoverage = [Math]::Round(100 * [double]$coverage.coverage.'branch-rate', 2)
    Assert-True ($lineCoverage -ge 83.24) `
        "Host 总行覆盖率 $lineCoverage% 低于 G0 基线 83.24%。"
    Assert-True ($branchCoverage -ge 68.98) `
        "Host 总分支覆盖率 $branchCoverage% 低于 G0 基线 68.98%。"

    $classes = @($coverage.coverage.packages.package.classes.class)
    $fullscreenCoverage = Get-FileLineCoverage $classes `
        'Business/Presentation/WindowContentFullscreenSession.cs'
    Assert-True ($fullscreenCoverage -ge 90.0) `
        "WindowContentFullscreenSession 行覆盖率 $fullscreenCoverage% 低于 90%。"

    if ($env:OS -ne 'Windows_NT' -or
        [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
        throw 'G8 真实媒体资源门禁需要本地 Windows x64 进程。'
    }
    $harnessReport = Join-Path $resultRoot 'real-media-fullscreen-20-cycles.json'
    $harnessArguments = @(
        'run', '--project',
        'Plugins\MySmallTools\MySmallTools.Playback.IntegrationHarness\MySmallTools.Playback.IntegrationHarness.csproj',
        '-c', $Configuration,
        '-p:SkipPluginDeploy=true'
    )
    if ($NoRestore) { $harnessArguments += '--no-restore' }
    $harnessArguments += @(
        '--', '--suite', 'g3', '--cycles', '20',
        # G8 固定压力轴是 20 轮“真实播放 -> 进入全屏 -> 直接关闭 Document”。普通进入/退出、
        # Esc、媒体切换与 Dock 切换仍由同一 Harness 的功能矩阵覆盖；这里把额外循环数置零，
        # 避免把其他阶段的发布级压力叠加到开发期 G8 门禁并掩盖全屏租约证据。
        '--dock-switches', '0', '--media-switches', '0',
        '--report', $harnessReport)
    Invoke-DotNet $harnessArguments

    Assert-True (Test-Path -LiteralPath $harnessReport -PathType Leaf) `
        'G8 真实媒体 Harness 未生成 JSON 报告。'
    $harness = Get-Content -Raw -LiteralPath $harnessReport | ConvertFrom-Json
    Assert-True ([bool]$harness.Success) 'G8 真实媒体 Harness 报告未通过。'
    Assert-True ([int]$harness.Cycles -eq 20) 'G8 真实媒体 Harness 没有执行固定 20 轮。'
    Assert-True (@($harness.Failures).Count -eq 0) 'G8 真实媒体 Harness 包含失败条目。'
    foreach ($resource in $harness.FinalResources.PSObject.Properties) {
        Assert-True ([long]$resource.Value -eq 0) `
            "G8 真实媒体 Harness 遗留资源 $($resource.Name)=$($resource.Value)。"
    }
    Assert-True (
        [int]$harness.AliveClosedDocuments -eq 0 -and
        [int]$harness.AliveClosedViews -eq 0 -and
        [int]$harness.AliveDisposedEncryptedStreams -eq 0) `
        'G8 真实媒体 Harness 仍保留已关闭 Document、View 或加密流。'

    $sdkRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginSdk.UI'
    $hostRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement'
    $smallToolsRoot = Join-Path $repositoryRoot 'Plugins\MySmallTools\MySmallTools'
    $harnessRoot = Join-Path $repositoryRoot `
        'Plugins\MySmallTools\MySmallTools.Playback.IntegrationHarness'
    Assert-PatternAbsent `
        'TryRestore\s*\(|TryPresent\s*\([^\r\n,]+,[^\r\n]+\)' `
        @($sdkRoot, $hostRoot, $smallToolsRoot, $harnessRoot) @('*.cs') `
        '活动生产或 Harness 源码重新出现 owner 式全屏 API。'
    Assert-PatternAbsent `
        '_fullscreenHost|_fullscreenOwner' `
        @($hostRoot, $smallToolsRoot) @('*.cs') `
        'Host 或 MySmallTools 重新保存全屏 Host/owner 引用。'

    $apiV3 = Get-Content -Raw -LiteralPath `
        'Host\MyAvaloniaManagement.PluginSdk.UI\ApiCompatibility\v3\PublicAPI.Unshipped.txt'
    Assert-True ($apiV3.Contains(
            'IWindowContentFullscreenHost.TryPresent(Avalonia.Controls.Control! content) -> System.IDisposable?',
            [StringComparison]::Ordinal)) `
        'v3 Unshipped 缺少最终全屏租约签名。'
    Assert-True (-not $apiV3.Contains('TryRestore', [StringComparison]::Ordinal)) `
        'v3 Unshipped 仍包含 TryRestore。'
    Assert-True (-not $apiV3.Contains('object! owner', [StringComparison]::Ordinal)) `
        'v3 Unshipped 仍包含任意 owner 参数。'
    $apiV2 = Get-Content -Raw -LiteralPath `
        'Host\MyAvaloniaManagement.PluginSdk.UI\ApiCompatibility\v2\PublicAPI.Shipped.txt'
    Assert-True (
        $apiV2.Contains('TryRestore(object! owner)', [StringComparison]::Ordinal) -and
        $apiV2.Contains('object! owner) -> bool', [StringComparison]::Ordinal)) `
        'G8 不得改写 v2 Shipped 历史全屏契约。'

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        suites = $suiteSummary
        passed = $totalPassed
        failed = 0
        lineCoverage = $lineCoverage
        branchCoverage = $branchCoverage
        criticalFileLineCoverage = [ordered]@{
            'Business/Presentation/WindowContentFullscreenSession.cs' = $fullscreenCoverage
        }
        realMediaHarness = [ordered]@{
            suite = 'g3'
            fullscreenDocumentCloseCycles = 20
            success = $true
            finalResourcesZero = $true
            closedReferencesZero = $true
            report = 'real-media-fullscreen-20-cycles.json'
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
        ($summary | ConvertTo-Json -Depth 7),
        [Text.UTF8Encoding]::new($false))
    Write-Host (
        "G8 全屏租约专项门禁通过：$totalPassed 项；" +
        "Host 行覆盖率 $lineCoverage%，分支覆盖率 $branchCoverage%；" +
        '真实播放/全屏关闭 20 轮资源归零。')
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
