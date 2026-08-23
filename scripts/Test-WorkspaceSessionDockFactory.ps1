[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\WorkspaceSessionDockFactory'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G6 结果目录不在仓库内：$resultRoot。"
}

# 本脚本只执行开发阶段的本地非发布验证。三个测试进程必须串行，避免共享编译输出、
# Avalonia Headless 资源和插件部署目录互相干扰；脚本不读取或运行 AIFLOW，也不调用
# Windows CI、Windows Smoke、ReleaseAcceptance、Host 发布门禁或任何发布脚本。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$settings = Join-Path $repositoryRoot `
    'Host\MyAvaloniaManagement.Tests\coverage.runsettings'
$suites = @(
    [pscustomobject]@{
        Name = 'G6-Unit'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
    },
    [pscustomobject]@{
        Name = 'G6-HeadlessUi'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
    },
    [pscustomobject]@{
        Name = 'G6-PluginDock'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
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

function Get-FileLineCoverage {
    param([object[]]$Classes, [string]$RelativePath)
    $matching = @($Classes | Where-Object {
        $_.filename.Replace('\', '/').EndsWith(
            $RelativePath,
            [StringComparison]::OrdinalIgnoreCase)
    })
    Assert-True ($matching.Count -gt 0) "覆盖率报告缺少核心文件：$RelativePath。"
    $lines = @($matching |
        ForEach-Object { $_.lines.line } |
        Group-Object number |
        ForEach-Object {
            [pscustomobject]@{
                Covered = @($_.Group | Where-Object { [int]$_.hits -gt 0 }).Count -gt 0
            }
        })
    if ($lines.Count -eq 0) { return 100.0 }
    return [Math]::Round(
        100 * @($lines | Where-Object Covered).Count / $lines.Count,
        2)
}

function Assert-PatternAbsent {
    param([string]$Pattern, [string[]]$Paths, [string]$Message)
    & rg --quiet $Pattern @Paths -g '*.cs'
    if ($LASTEXITCODE -eq 0) { throw $Message }
    if ($LASTEXITCODE -gt 1) { throw "无法执行结构扫描：$Pattern。" }
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
            '--settings', $settings,
            '--collect:XPlat Code Coverage',
            '--results-directory', $suiteDirectory,
            '--logger', "trx;LogFileName=$($suite.Name).trx",
            '--logger', 'console;verbosity=minimal'
        )
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
    Assert-True ($coverageReports.Count -eq $suites.Count) `
        "预期 $($suites.Count) 份独立覆盖率报告，实际为 $($coverageReports.Count) 份。"
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
    $criticalCoverage = [ordered]@{}
    foreach ($relativePath in @(
        'Business/Workspace/WorkspaceSession.cs',
        'Business/Docking/HostDockFactory.cs',
        'Business/Workspace/ToolWorkspaceReadModel.cs')) {
        $actual = Get-FileLineCoverage $classes $relativePath
        Assert-True ($actual -ge 90.0) `
            "$relativePath 行覆盖率 $actual% 低于 90%。"
        $criticalCoverage[$relativePath] = $actual
    }

    $productionRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement'
    $harnessRoot = Join-Path $repositoryRoot `
        'Plugins\MySmallTools\MySmallTools.Playback.IntegrationHarness'
    Assert-PatternAbsent `
        'ManagementFactory|DocumentWorkspace|ToolManagementData' `
        @($productionRoot) `
        '生产代码重新出现已删除的万能 Factory、双重 Document 所有者或 Dock Tool DTO。'

    $factoryInheritance = @(& rg -l `
        'class\s+\w+[^\r\n{]*:\s*(Dock\.Model\.Mvvm\.)?Factory\b' `
        $productionRoot -g '*.cs')
    if ($LASTEXITCODE -gt 1) { throw '无法扫描 Dock Factory 继承面。' }
    $expectedFactory = [IO.Path]::GetFullPath((Join-Path $productionRoot `
        'Business\Docking\HostDockFactory.cs'))
    Assert-True ($factoryInheritance.Count -eq 1) `
        '生产代码必须且只能有一个 Dock Factory 子类。'
    Assert-True (
        [IO.Path]::GetFullPath($factoryInheritance[0]) -eq $expectedFactory) `
        'Dock Factory 子类不是唯一的 HostDockFactory。'
    & rg --quiet 'internal sealed class HostDockFactory\s*:\s*Factory' $expectedFactory
    Assert-True ($LASTEXITCODE -eq 0) 'HostDockFactory 必须保持 internal sealed。'

    $sessionPath = Join-Path $productionRoot 'Business\Workspace\WorkspaceSession.cs'
    Assert-PatternAbsent `
        'class\s+WorkspaceSession[^\r\n{]*:\s*(Dock\.Model|Factory)\b' `
        @($sessionPath) `
        'WorkspaceSession 不得继承 Dock 类型。'
    Assert-PatternAbsent `
        'HostDockFactory\?|PluginRegistry\?|IHostDockableFactory\?|DocumentPersistenceStateStore\?|DocumentCloseCoordinator\?|DocumentRecoveryRegistry\?|PluginAvailabilityReadModel\?' `
        @($sessionPath) `
        'WorkspaceSession 的正确性依赖不得恢复为可空构造参数。'

    $toolViewModelPath = Join-Path $productionRoot `
        'Models\Tools\ToolWorkspaceState.cs'
    Assert-PatternAbsent `
        'using\s+Dock\.|Dock\.Model|IRootDock|IServiceProvider|HostDockFactory|CreatedTools|Dictionary<' `
        @($toolViewModelPath) `
        'ToolManagementViewModel 重新接触 Dock、Factory 字典或服务定位器。'
    $toolStatePath = Join-Path $productionRoot 'Models\Tools\ToolWorkspaceState.cs'
    Assert-PatternAbsent `
        'using\s+Dock\.|Dock\.Model|IRootDock|IDockable|ITool' `
        @($toolStatePath) `
        'ToolWorkspaceState 重新泄漏 Dock 类型。'

    $mainViewModelPath = Join-Path $productionRoot 'ViewModels\MainWindowViewModel.cs'
    Assert-PatternAbsent `
        'MainWindowViewModel\s*\([^)]*(HostDockFactory|ManagementFactory|IRootDock)' `
        @($mainViewModelPath) `
        'MainWindowViewModel 构造函数重新依赖 Factory 或 Root Dock。'
    Assert-PatternAbsent `
        'GetDockable[^\r\n]*"Files"|DockableLocator[^\r\n]*"Files"|\["Files"\]' `
        @($productionRoot, $harnessRoot) `
        '生产或 Harness 重新出现 Files Locator 查询。'

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        suites = $suiteSummary
        passed = $totalPassed
        failed = 0
        lineCoverage = $lineCoverage
        branchCoverage = $branchCoverage
        criticalFileLineCoverage = $criticalCoverage
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
    Write-Host (
        "G6 Workspace Session / Dock Factory 专项门禁通过：$totalPassed 项；" +
        "Host 行覆盖率 $lineCoverage%，分支覆盖率 $branchCoverage%。")
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
