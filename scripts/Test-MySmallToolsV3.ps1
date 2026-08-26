[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore,
    [ValidateRange(1, 100)]
    [int]$HarnessCycles = 20
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'PluginV3Acceptance.Core.psm1') -Force
$resultRoot = New-PluginV3ResultRoot $repositoryRoot `
    'artifacts\test-results\MySmallToolsV3'

# G11 只做本地开发期验收。真实媒体 Harness 是插件资源所有权的必要测试，
# 并不等价于 Windows CI、Windows Smoke 或发布验收；本脚本不会触达这些发布入口。
$suites = @(
    [pscustomobject]@{
        Name = 'G11-PluginSdk'
        Project = 'Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj'
        CollectCoverage = $false
        HostCoverage = $false
        PluginCoverage = $false
    },
    [pscustomobject]@{
        Name = 'G11-HostUnit'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
        Settings = 'Host\MyAvaloniaManagement.Tests\coverage.runsettings'
        CollectCoverage = $true
        HostCoverage = $true
        PluginCoverage = $false
    },
    [pscustomobject]@{
        Name = 'G11-HeadlessUi'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        CollectCoverage = $true
        HostCoverage = $true
        PluginCoverage = $true
    },
    [pscustomobject]@{
        Name = 'G11-PluginDock'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        CollectCoverage = $true
        HostCoverage = $true
        PluginCoverage = $true
    },
    [pscustomobject]@{
        Name = 'G11-MySmallTools'
        Project = 'Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj'
        CollectCoverage = $true
        HostCoverage = $false
        PluginCoverage = $true
    }
)

$suiteSummary = [ordered]@{}
$coveragePaths = @{}
$totalPassed = 0
Push-Location $repositoryRoot
try {
    Invoke-PluginV3DotNet @('tool', 'restore')
    foreach ($suite in $suites) {
        $result = Invoke-PluginV3TestSuite `
            -Suite $suite `
            -ResultRoot $resultRoot `
            -Configuration $Configuration `
            -NoRestore $NoRestore.IsPresent
        $suiteSummary[$suite.Name] = $result.Passed
        $coveragePaths[$suite.Name] = $result.CoveragePath
        $totalPassed += $result.Passed
    }

    $hostReports = @($suites | Where-Object HostCoverage | ForEach-Object {
        $coveragePaths[$_.Name]
    })
    Assert-PluginV3True ($hostReports.Count -eq 3) 'G11 预期三份 Host 覆盖率报告。'
    $hostCoverage = Merge-PluginV3Coverage `
        $hostReports `
        (Join-Path $resultRoot 'coverage-host') `
        '+MyAvaloniaManagement;-*.Tests'
    Assert-PluginV3True ($hostCoverage.Line -ge 83.24) `
        "Host 总行覆盖率 $($hostCoverage.Line)% 低于 G0 基线 83.24%。"
    Assert-PluginV3True ($hostCoverage.Branch -ge 68.98) `
        "Host 总分支覆盖率 $($hostCoverage.Branch)% 低于 G0 基线 68.98%。"

    # 插件整体覆盖率先以本轮稳定绿色事实签署为 G11 基线；关键的资源释放与全屏租约
    # 另由真实媒体进程验证，避免用低价值的 View 属性访问测试人为抬高数字。
    $pluginReports = @($suites | Where-Object PluginCoverage | ForEach-Object {
        $coveragePaths[$_.Name]
    })
    $pluginCoverage = Merge-PluginV3Coverage `
        $pluginReports `
        (Join-Path $resultRoot 'coverage-mysmalltools') `
        '+MySmallTools;-*.Tests'
    $pluginBaselinePath = Join-Path $repositoryRoot `
        'Plugins\MySmallTools\MySmallTools.Tests\coverage-baseline.json'
    $pluginBaseline = Get-Content -Raw -LiteralPath $pluginBaselinePath | ConvertFrom-Json
    Assert-PluginV3True ($pluginCoverage.Line -ge [double]$pluginBaseline.line) `
        "MySmallTools 行覆盖率 $($pluginCoverage.Line)% 低于 G11 基线 $($pluginBaseline.line)% 。"
    Assert-PluginV3True ($pluginCoverage.Branch -ge [double]$pluginBaseline.branch) `
        "MySmallTools 分支覆盖率 $($pluginCoverage.Branch)% 低于 G11 基线 $($pluginBaseline.branch)% 。"
    $pluginClasses = @($pluginCoverage.Xml.coverage.packages.package.classes.class)
    $g4ActionCoverage = Get-PluginV3FileLineCoverage `
        $pluginClasses `
        'Business/SecretVideoPlayer/Workflow/EncryptVideoWorkflowAction.cs'
    Assert-PluginV3True ($g4ActionCoverage -ge 90) `
        "G4 加密 Action 关键文件行覆盖率 $g4ActionCoverage% 低于 90%。"

    $pluginRoot = Join-Path $repositoryRoot 'Plugins\MySmallTools\MySmallTools'
    Assert-PluginV3RgAbsent `
        'MyAvaloniaManagementCommon|LegacyPluginContracts|Dock\.Model|IDocumentCreationStrategy|IDocumentScopeFactory|DocumentTypeIdConstant|Legacy[A-Za-z]+DocumentId|Newtonsoft\.Json|MyAvaloniaManagement\.Business|MyAvaloniaManagement\.ViewModels|IHostEventBus|HostEventBus|IServiceProvider|IClassicDesktopStyleApplicationLifetime' `
        @($pluginRoot) @('*.cs', '*.csproj') `
        'G11 MySmallTools 生产代码重新出现 Legacy、Dock、旧 GUID、Host 总线、服务定位器或直接窗口访问。'

    $projectPath = Join-Path $pluginRoot 'MySmallTools.csproj'
    foreach ($requiredReference in
        'MyAvaloniaManagement.PluginSdk.csproj',
        'MyAvaloniaManagement.PluginSdk.UI.csproj') {
        & rg --quiet ([Regex]::Escape($requiredReference)) $projectPath
        Assert-PluginV3True ($LASTEXITCODE -eq 0) `
            "G11 MySmallTools 缺少最终 SDK 引用：$requiredReference。"
    }
    Assert-PluginV3RgAbsent `
        'ManagedPluginUseV2EntryContract|ManagedPluginHostApi|ManagedPluginCommonContract' `
        @($projectPath) @('*.csproj') `
        'G11 MySmallTools 项目重新出现过渡入口开关或 Host/Common 双区间。'

    $legacyPattern = 'MySmallTools' + 'V2(Migration|Ui)Tests|Test-MySmallTools' + 'V2'
    Assert-PluginV3RgAbsent `
        $legacyPattern `
        @(
            (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginTests'),
            (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.UiTests'),
            (Join-Path $repositoryRoot 'scripts\Test-MySmallToolsV3.ps1')) `
        @('*.cs', '*.ps1') `
        'G11 活动测试或脚本仍保留 MySmallTools V2 阶段入口。'

    $harnessReport = Join-Path $resultRoot 'real-media-harness.json'
    $harnessArguments = @(
        'run', '--project',
        'Plugins\MySmallTools\MySmallTools.Playback.IntegrationHarness\MySmallTools.Playback.IntegrationHarness.csproj',
        '-c', $Configuration,
        '-p:SkipPluginDeploy=true',
        '-p:TreatWarningsAsErrors=true'
    )
    if ($NoRestore) { $harnessArguments += '--no-restore' }
    $harnessArguments += @(
        '--', '--suite', 'g3', '--cycles', $HarnessCycles.ToString(),
        '--report', $harnessReport)
    Invoke-PluginV3DotNet $harnessArguments
    $harness = Get-Content -Raw -LiteralPath $harnessReport | ConvertFrom-Json
    Assert-PluginV3True ([bool]$harness.success) 'G11 真实媒体 Harness 报告未通过。'
    Assert-PluginV3True ([int]$harness.cycles -eq $HarnessCycles) `
        'G11 Harness 报告轮数与请求不一致。'
    Assert-PluginV3True (@($harness.failures).Count -eq 0) `
        'G11 Harness 报告仍包含失败事实。'
    Assert-PluginV3True ([int]$harness.aliveClosedDocuments -eq 0) `
        'G11 关闭后的 Document 弱引用仍存活。'
    Assert-PluginV3True ([int]$harness.aliveClosedViews -eq 0) `
        'G11 关闭后的 View 弱引用仍存活。'
    Assert-PluginV3True ([int]$harness.aliveDisposedEncryptedStreams -eq 0) `
        'G11 已释放的加密流仍被持有。'
    foreach ($resource in $harness.finalResources.PSObject.Properties) {
        Assert-PluginV3True ([int64]$resource.Value -eq 0) `
            "G11 最终资源 $($resource.Name) 未归零：$($resource.Value)。"
    }

    $package = New-PluginV3PackageEvidence `
        $repositoryRoot $PSScriptRoot $resultRoot $projectPath `
        'MySmallTools' $Configuration
    $manifestPath = Join-Path $package.LoadRoot 'Controls\SmallTools\plugin.manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    Assert-PluginV3Manifest `
        $manifest `
        'myavalonia.plugin.my-small-tools' `
        '3.1.0' `
        '3.2.0'

    $variableName = 'MYAVALONIA_G11_V3_PACKAGE_ROOT'
    $previousPackageRoot = [Environment]::GetEnvironmentVariable($variableName)
    try {
        [Environment]::SetEnvironmentVariable(
            $variableName, (Join-Path $package.LoadRoot 'Controls'))
        $zipSuite = [pscustomobject]@{
            Name = 'G11-FinalZip'
            Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
            Filter = 'FullyQualifiedName~G11最终测试Zip通过真实V3发现组合并进入Workspace目录'
            CollectCoverage = $false
        }
        $zipResult = Invoke-PluginV3TestSuite `
            $zipSuite $resultRoot $Configuration $NoRestore.IsPresent
        $suiteSummary[$zipSuite.Name] = $zipResult.Passed
        $totalPassed += $zipResult.Passed
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
        skipped = 0
        hostCoverage = [ordered]@{
            line = $hostCoverage.Line
            branch = $hostCoverage.Branch
        }
        pluginCoverage = [ordered]@{
            line = $pluginCoverage.Line
            branch = $pluginCoverage.Branch
            g4ActionLine = $g4ActionCoverage
        }
        harness = [ordered]@{
            suite = 'g3'
            cycles = $HarnessCycles
            success = $true
            allFinalResourcesZero = $true
            aliveClosedDocuments = [int]$harness.aliveClosedDocuments
            aliveClosedViews = [int]$harness.aliveClosedViews
            aliveDisposedEncryptedStreams = [int]$harness.aliveDisposedEncryptedStreams
            report = 'real-media-harness.json'
        }
        manifest = [ordered]@{
            schemaVersion = [int]$manifest.schemaVersion
            pluginId = $manifest.pluginId
            pluginVersion = $manifest.pluginVersion
            sdkMinInclusive = $manifest.sdk.minInclusive
            sdkMaxExclusive = $manifest.sdk.maxExclusive
        }
        archiveSha256 = $package.ArchiveSha256
        packageFiles = $package.FileCount
        deterministicBuilds = 2
        workspaceDocuments = 4
        workspaceTools = 0
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    Write-PluginV3Json (Join-Path $resultRoot 'summary.json') $summary
    Write-Host (
        "G11 MySmallTools V3 专项门禁通过：$totalPassed 项；" +
        "Host $($hostCoverage.Line)%/$($hostCoverage.Branch)%；" +
        "真实媒体 $HarnessCycles 轮资源归零；测试 ZIP $($package.FileCount) 个文件。")
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
