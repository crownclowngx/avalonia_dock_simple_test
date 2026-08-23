[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'PluginV3Acceptance.Core.psm1') -Force
$resultRoot = New-PluginV3ResultRoot $repositoryRoot `
    'artifacts\test-results\BiliDownloaderV3'

# G12 是本地开发期非发布验收；Release 只是编译配置。脚本不读取或初始化 AIFLOW，
# 也不调用 Windows CI/Smoke、ReleaseAcceptance、签名、上传、标签或任何发布门禁。
$suites = @(
    [pscustomobject]@{
        Name = 'G12-PluginSdk'
        Project = 'Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj'
        CollectCoverage = $false
        HostCoverage = $false
    },
    [pscustomobject]@{
        Name = 'G12-HostUnit'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
        Settings = 'Host\MyAvaloniaManagement.Tests\coverage.runsettings'
        CollectCoverage = $true
        HostCoverage = $true
    },
    [pscustomobject]@{
        Name = 'G12-HeadlessUi'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        CollectCoverage = $true
        HostCoverage = $true
    },
    [pscustomobject]@{
        Name = 'G12-PluginDock'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        CollectCoverage = $true
        HostCoverage = $true
    },
    [pscustomobject]@{
        Name = 'G12-BiliDownloader'
        Project = 'Plugins\BiliDownloader\BiliDownloader.Tests\BiliDownloader.Tests.csproj'
        Settings = 'Plugins\BiliDownloader\BiliDownloader.Tests\coverage.runsettings'
        CollectCoverage = $true
        HostCoverage = $false
    }
)

function Get-BiliCoverageCounts {
    param([Parameter(Mandatory)] [object[]]$Classes)

    # RunSettings 已排除真实模态 UI；这里再按源文件聚合 condition-coverage，保持与插件原有
    # A/B/C 门禁完全相同的计算口径，不另造第二套覆盖率定义。
    return @($Classes |
        Where-Object {
            $normalized = $_.filename.Replace('\', '/')
            $normalized -like '*/Plugins/BiliDownloader/BiliDownloader/*' -and
            $normalized -notmatch '/obj/|/Views/'
        } |
        Group-Object filename |
        ForEach-Object {
            $lines = @($_.Group.lines.line)
            $branchCovered = 0
            $branchValid = 0
            foreach ($line in $lines) {
                if ($line.'condition-coverage' -match '\((\d+)/(\d+)\)') {
                    $branchCovered += [int]$Matches[1]
                    $branchValid += [int]$Matches[2]
                }
            }
            [pscustomobject]@{
                File = $_.Name.Replace('\', '/')
                LineCovered = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
                LineValid = $lines.Count
                BranchCovered = $branchCovered
                BranchValid = $branchValid
            }
        })
}

function Get-BiliCoverageMetric {
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]]$Files)

    $lineCovered = ($Files | Measure-Object LineCovered -Sum).Sum
    $lineValid = ($Files | Measure-Object LineValid -Sum).Sum
    $branchCovered = ($Files | Measure-Object BranchCovered -Sum).Sum
    $branchValid = ($Files | Measure-Object BranchValid -Sum).Sum
    return [pscustomobject]@{
        Line = if ($lineValid -gt 0) { [Math]::Round(100 * $lineCovered / $lineValid, 2) } else { 100.0 }
        Branch = if ($branchValid -gt 0) { [Math]::Round(100 * $branchCovered / $branchValid, 2) } else { 100.0 }
    }
}

function Assert-BiliCoverageMinimum {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] $Actual,
        [Parameter(Mandatory)] [double]$Line,
        [Parameter(Mandatory)] [double]$Branch)

    Assert-PluginV3True ($Actual.Line -ge $Line) `
        "$Name 行覆盖率 $($Actual.Line)% 低于 $Line%。"
    Assert-PluginV3True ($Actual.Branch -ge $Branch) `
        "$Name 分支覆盖率 $($Actual.Branch)% 低于 $Branch%。"
}

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
    Assert-PluginV3True ($hostReports.Count -eq 3) 'G12 预期三份 Host 覆盖率报告。'
    $hostCoverage = Merge-PluginV3Coverage `
        $hostReports `
        (Join-Path $resultRoot 'coverage-host') `
        '+MyAvaloniaManagement;-*.Tests'
    Assert-PluginV3True ($hostCoverage.Line -ge 83.24) `
        "Host 总行覆盖率 $($hostCoverage.Line)% 低于 G0 基线 83.24%。"
    Assert-PluginV3True ($hostCoverage.Branch -ge 68.98) `
        "Host 总分支覆盖率 $($hostCoverage.Branch)% 低于 G0 基线 68.98%。"

    [xml]$biliCoverageXml = Get-Content -LiteralPath $coveragePaths['G12-BiliDownloader']
    $biliFiles = Get-BiliCoverageCounts `
        @($biliCoverageXml.coverage.packages.package.classes.class)
    $baselinePath = Join-Path $repositoryRoot `
        'Plugins\BiliDownloader\BiliDownloader.Tests\coverage-baseline.json'
    $baseline = Get-Content -Raw -LiteralPath $baselinePath | ConvertFrom-Json
    $patterns = @{
        A = 'Services/Auth/|Services/Persistence/|Services/Download/BiliDownloadCoordinator.cs|Services/Download/DownloadProgressTracker.cs|Services/Infrastructure/SensitiveDataSanitizer.cs|Services/Infrastructure/BiliLocalStateInitializer.cs|Models/DownloadTaskStatus.cs'
        B = 'Services/Api/|Services/ContentSources/|Services/Download/'
        C = 'ViewModels/|Converters/|Plugin/'
    }
    $overall = Get-BiliCoverageMetric $biliFiles
    $groupA = Get-BiliCoverageMetric @($biliFiles | Where-Object { $_.File -match $patterns.A })
    $groupB = Get-BiliCoverageMetric @($biliFiles | Where-Object { $_.File -match $patterns.B })
    $groupC = Get-BiliCoverageMetric @($biliFiles | Where-Object { $_.File -match $patterns.C })
    Assert-BiliCoverageMinimum 'Bili Group A' $groupA $baseline.groups.A.line $baseline.groups.A.branch
    Assert-BiliCoverageMinimum 'Bili Group B' $groupB $baseline.groups.B.line $baseline.groups.B.branch
    Assert-BiliCoverageMinimum 'Bili Group C' $groupC $baseline.groups.C.line $baseline.groups.C.branch
    Assert-BiliCoverageMinimum `
        'Bili overall' $overall `
        ([double]$baseline.overall.line - [double]$baseline.overall.tolerance) `
        ([double]$baseline.overall.branch - [double]$baseline.overall.tolerance)
    foreach ($relativePath in $baseline.criticalFiles) {
        $file = @($biliFiles | Where-Object {
            $_.File.EndsWith($relativePath, [StringComparison]::OrdinalIgnoreCase)
        })
        Assert-PluginV3True ($file.Count -eq 1) `
            "Bili 覆盖率缺少唯一关键文件：$relativePath。"
        $metric = Get-BiliCoverageMetric $file
        Assert-PluginV3True ($metric.Line -ge [double]$baseline.criticalFileMinimumLine) `
            "Bili 关键文件 $relativePath 行覆盖率 $($metric.Line)% 低于 $($baseline.criticalFileMinimumLine)% 。"
    }

    $pluginRoot = Join-Path $repositoryRoot 'Plugins\BiliDownloader\BiliDownloader'
    Assert-PluginV3RgAbsent `
        'MyAvaloniaManagementCommon|LegacyPluginContracts|Dock\.Model|IDocumentCreationStrategy|IToolCreationStrategy|IDocumentScopeFactory|ISavableDocument|DocumentContentSnapshot|BiliDownloaderDocumentStrategy|BiliSchedulerToolStrategy|Newtonsoft\.Json|JObject|JToken|JArray|MyAvaloniaManagement\.Business|MyAvaloniaManagement\.ViewModels|IHostEventBus|HostEventBus|MyAvaloniaManagement\.[A-Za-z.]*Facade|IServiceProvider' `
        @($pluginRoot) @('*.cs', '*.csproj') `
        'G12 BiliDownloader 生产代码重新出现 Legacy、Dock、旧保存/JSON、Host 总线/Facade 或服务定位器。'

    $projectPath = Join-Path $pluginRoot 'BiliDownloader.csproj'
    foreach ($requiredReference in
        'MyAvaloniaManagement.PluginSdk.csproj',
        'MyAvaloniaManagement.PluginSdk.UI.csproj') {
        & rg --quiet ([Regex]::Escape($requiredReference)) $projectPath
        Assert-PluginV3True ($LASTEXITCODE -eq 0) `
            "G12 BiliDownloader 缺少最终 SDK 引用：$requiredReference。"
    }
    Assert-PluginV3RgAbsent `
        'ManagedPluginUseV2EntryContract|ManagedPluginHostApi|ManagedPluginCommonContract' `
        @($projectPath) @('*.csproj') `
        'G12 BiliDownloader 项目重新出现过渡入口开关或 Host/Common 双区间。'

    $legacyPattern = 'G12' + 'V2MigrationTests|BiliDownloader' + 'V2PackageTests|Test-BiliDownloader' + 'V2'
    Assert-PluginV3RgAbsent `
        $legacyPattern `
        @(
            (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginTests'),
            (Join-Path $repositoryRoot 'Plugins\BiliDownloader\BiliDownloader.Tests'),
            (Join-Path $repositoryRoot 'scripts\Test-BiliDownloaderV3.ps1')) `
        @('*.cs', '*.ps1') `
        'G12 活动测试或脚本仍保留 BiliDownloader V2 阶段入口。'

    $package = New-PluginV3PackageEvidence `
        $repositoryRoot $PSScriptRoot $resultRoot $projectPath `
        'BiliDownloader' $Configuration
    $manifestPath = Join-Path $package.LoadRoot 'Controls\BiliDownloader\plugin.manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    Assert-PluginV3Manifest $manifest 'myavalonia.plugin.bili-downloader'
    $runtimeRoots = @($package.FirstSidecar.files.path |
        Where-Object { $_ -match '/runtimes/([^/]+)/' } |
        ForEach-Object { [regex]::Match($_, '/runtimes/([^/]+)/').Groups[1].Value } |
        Sort-Object -Unique)
    Assert-PluginV3True (-not (Compare-Object @('win-x64') $runtimeRoots)) `
        "G12 测试 ZIP RID 边界无效：$($runtimeRoots -join ', ')。"

    $variableName = 'MYAVALONIA_G12_V3_PACKAGE_ROOT'
    $previousPackageRoot = [Environment]::GetEnvironmentVariable($variableName)
    try {
        [Environment]::SetEnvironmentVariable(
            $variableName, (Join-Path $package.LoadRoot 'Controls'))
        $zipSuite = [pscustomobject]@{
            Name = 'G12-FinalZip'
            Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
            Filter = 'FullyQualifiedName~G12最终测试Zip通过真实V3发现组合并进入Workspace目录|FullyQualifiedName~BiliDownloaderV3PackageTests'
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
        hostCoverage = [ordered]@{ line = $hostCoverage.Line; branch = $hostCoverage.Branch }
        pluginCoverage = [ordered]@{
            overall = $overall
            groupA = $groupA
            groupB = $groupB
            groupC = $groupC
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
        runtimeIdentifiers = $runtimeRoots
        workspaceDocuments = 1
        workspaceCreationIntents = 2
        workspaceTools = 1
        pluginLifecycles = 1
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
        "G12 BiliDownloader V3 专项门禁通过：$totalPassed 项；" +
        "Host $($hostCoverage.Line)%/$($hostCoverage.Branch)%；" +
        "Bili $($overall.Line)%/$($overall.Branch)%；测试 ZIP $($package.FileCount) 个文件。")
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
