[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\HostCatalogPluginRegistry'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G7 结果目录不在仓库内：$resultRoot。"
}

# G7 是开发阶段的本地非发布门禁。三个进程必须串行运行，避免共享编译输出、Avalonia
# Headless 资源和插件部署目录相互污染。本脚本不读取、初始化或修改 AIFLOW，也不调用
# Windows CI/Smoke、发布验收、发布门禁、签名、上传、标签或其他发布操作。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$settings = Join-Path $repositoryRoot `
    'Host\MyAvaloniaManagement.Tests\coverage.runsettings'
$suites = @(
    [pscustomobject]@{
        Name = 'G7-Unit'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
    },
    [pscustomobject]@{
        Name = 'G7-HeadlessUi'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
    },
    [pscustomobject]@{
        Name = 'G7-PluginDock'
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
    if ($LASTEXITCODE -gt 1) { throw "无法执行结构扫描：$Pattern。" }
}

function Assert-PatternPresent {
    param([string]$Pattern, [string[]]$Paths, [string]$Message)
    & rg --quiet $Pattern @Paths -g '*.cs'
    if ($LASTEXITCODE -ne 0) { throw $Message }
}

function Get-FileLineCoverage {
    param([object[]]$Classes, [string]$RelativePath)
    $matching = @($Classes | Where-Object {
        $_.filename.Replace('\', '/').EndsWith(
            $RelativePath,
            [StringComparison]::OrdinalIgnoreCase)
    })
    Assert-True ($matching.Count -gt 0) "覆盖率报告缺少关键文件：$RelativePath。"
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
        'Business/Workspace/HostWorkspaceCatalog.cs',
        'Business/Workspace/WorkspaceCatalog.cs',
        'Business/Workspace/HostWorkspaceActivator.cs',
        'Business/Helpers/PluginContributionActivator.cs')) {
        $actual = Get-FileLineCoverage $classes $relativePath
        Assert-True ($actual -ge 90.0) `
            "$relativePath 行覆盖率 $actual% 低于 90%。"
        $criticalCoverage[$relativePath] = $actual
    }

    $productionRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement'
    $unitRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.Tests'
    $uiRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.UiTests'
    $pluginRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginTests'
    $harnessRoot = Join-Path $repositoryRoot `
        'Plugins\MySmallTools\MySmallTools.Playback.IntegrationHarness'
    $allCode = @($productionRoot, $unitRoot, $uiRoot, $pluginRoot, $harnessRoot)

    Assert-PatternAbsent 'V2Owner|AppendHostContributions|CreatePluginDocument|ActivatedPlugin(Document|Tool)' `
        $allCode @('*.cs') `
        '生产或测试 Harness 重新出现 G7 已删除的临时所有权/激活符号。'
    Assert-PatternAbsent 'DockableLocator[^\r\n]*("Plug"|\["Plug"\])' `
        $allCode @('*.cs') `
        '生产或测试 Harness 重新注册 Plug Locator。'
    Assert-PatternAbsent 'new\s+PluginRegistration\([^\)]*HostExtensionIds|PluginRegistration\([^\)]*myavalonia\.host' `
        @($productionRoot) @('*.cs') `
        '组合根重新把 Host 贡献包装成 PluginRegistration。'
    Assert-PatternAbsent 'HostExtensionIds|myavalonia\.host' `
        @(
            (Join-Path $productionRoot 'Business\Helpers\PluginRegistryBuilder.cs'),
            (Join-Path $productionRoot 'Business\Helpers\PluginContributionActivator.cs'),
            (Join-Path $productionRoot 'Business\Lifecycle\PluginLifecycleStateStore.cs')) `
        @('*.cs') `
        'Plugin Registry/Activator/Availability 重新出现 Host 特判。'
    Assert-PatternAbsent 'IServiceProvider' `
        @(
            (Join-Path $productionRoot 'Business\Workspace\HostWorkspaceCatalog.cs'),
            (Join-Path $productionRoot 'Business\Workspace\WorkspaceCatalog.cs')) `
        @('*.cs') `
        'Catalog 不得持有或解析通用服务容器。'
    Assert-PatternAbsent 'public\s+(sealed\s+)?class\s+.*Workspace.*Context' `
        @($productionRoot) @('*.cs') `
        'G7 不得新增公共 Workspace Context。'
    Assert-PatternPresent 'WorkspaceCatalog' `
        @(
            (Join-Path $productionRoot 'Business\Workspace\WorkspaceSession.cs'),
            (Join-Path $productionRoot 'ViewLocator.cs')) `
        'WorkspaceSession 与 ViewLocator 必须依赖 WorkspaceCatalog。'

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
        "G7 Host Catalog / Plugin Registry 专项门禁通过：$totalPassed 项；" +
        "Host 行覆盖率 $lineCoverage%，分支覆盖率 $branchCoverage%。")
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
