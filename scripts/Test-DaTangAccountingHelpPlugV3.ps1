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
    'artifacts\test-results\DaTangAccountingHelpPlugV3'

# G10 是本地开发期非发布验收。Release 只表示编译配置；脚本不读取、初始化或修改
# AIFLOW，也不调用 Windows CI/Smoke、ReleaseAcceptance、发布门禁、签名、上传或标签。
$suites = @(
    [pscustomobject]@{
        Name = 'G10-PluginSdk'
        Project = 'Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj'
        CollectCoverage = $false
        HostCoverage = $false
        PluginCoverage = $false
    },
    [pscustomobject]@{
        Name = 'G10-HostUnit'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
        Settings = 'Host\MyAvaloniaManagement.Tests\coverage.runsettings'
        CollectCoverage = $true
        HostCoverage = $true
        PluginCoverage = $false
    },
    [pscustomobject]@{
        Name = 'G10-HeadlessUi'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        CollectCoverage = $true
        HostCoverage = $true
        PluginCoverage = $true
    },
    [pscustomobject]@{
        Name = 'G10-PluginDock'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        CollectCoverage = $true
        HostCoverage = $true
        PluginCoverage = $true
    },
    [pscustomobject]@{
        Name = 'G10-DaTang'
        Project = 'Plugins\DaTangAccountingHelpPlug\DaTangAccountingHelpPlug.Tests\DaTangAccountingHelpPlug.Tests.csproj'
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
    Assert-PluginV3True ($hostReports.Count -eq 3) 'G10 预期三份 Host 覆盖率报告。'
    $hostCoverage = Merge-PluginV3Coverage `
        $hostReports `
        (Join-Path $resultRoot 'coverage-host') `
        '+MyAvaloniaManagement;-*.Tests'
    Assert-PluginV3True ($hostCoverage.Line -ge 83.24) `
        "Host 总行覆盖率 $($hostCoverage.Line)% 低于 G0 基线 83.24%。"
    Assert-PluginV3True ($hostCoverage.Branch -ge 68.98) `
        "Host 总分支覆盖率 $($hostCoverage.Branch)% 低于 G0 基线 68.98%。"

    $pluginReports = @($suites | Where-Object PluginCoverage | ForEach-Object {
        $coveragePaths[$_.Name]
    })
    $pluginCoverage = Merge-PluginV3Coverage `
        $pluginReports `
        (Join-Path $resultRoot 'coverage-datang') `
        '+DaTangAccountingHelpPlug;-*.Tests'
    $pluginClasses = @($pluginCoverage.Xml.coverage.packages.package.classes.class)
    $documentCoverage = Get-PluginV3FileLineCoverage $pluginClasses `
        'ViewModels/BankBalanceReconciliation/BankBalanceReconciliationViewModel.cs'
    $codecCoverage = Get-PluginV3FileLineCoverage $pluginClasses `
        'Persistence/ReconciliationDocumentContentCodec.cs'
    Assert-PluginV3True ($documentCoverage -ge 90.0) `
        "银行对账 Document 行覆盖率 $documentCoverage% 低于 90%。"
    Assert-PluginV3True ($codecCoverage -ge 90.0) `
        "银行对账 Codec 行覆盖率 $codecCoverage% 低于 90%。"

    $pluginRoot = Join-Path $repositoryRoot `
        'Plugins\DaTangAccountingHelpPlug\DaTangAccountingHelpPlug'
    Assert-PluginV3RgAbsent `
        'MyAvaloniaManagementCommon|LegacyPluginContracts|Dock\.Model|IDocumentCreationStrategy|IDocumentScopeFactory|DocumentContentSnapshot|ISavableDocument|IDocumentSaveState|Newtonsoft\.Json|LegacyIds|IHostEventBus|HostEventBus|IServiceProvider|Application\.Current|IClassicDesktopStyleApplicationLifetime|StorageProvider|\.Clipboard' `
        @($pluginRoot) @('*.cs', '*.csproj') `
        'G10 DaTang 生产代码重新出现 Legacy、Dock、旧保存、Host 总线、服务定位器或直接窗口访问。'

    $projectPath = Join-Path $pluginRoot 'DaTangAccountingHelpPlug.csproj'
    foreach ($requiredReference in
        'MyAvaloniaManagement.PluginSdk.csproj',
        'MyAvaloniaManagement.PluginSdk.UI.csproj') {
        & rg --quiet ([Regex]::Escape($requiredReference)) $projectPath
        Assert-PluginV3True ($LASTEXITCODE -eq 0) `
            "G10 DaTang 缺少最终 SDK 引用：$requiredReference。"
    }
    Assert-PluginV3RgAbsent `
        'ManagedPluginUseV2EntryContract|ManagedPluginHostApi|ManagedPluginCommonContract' `
        @($projectPath) @('*.csproj') `
        'G10 DaTang 项目重新出现过渡入口开关或 Host/Common 双区间。'

    $legacyPattern = 'DaTangAccountingHelpPlug' + 'V2(Migration|Ui)Tests|Test-DaTangAccountingHelpPlug' + 'V2'
    Assert-PluginV3RgAbsent `
        $legacyPattern `
        @(
            (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginTests'),
            (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.UiTests'),
            (Join-Path $repositoryRoot 'scripts\Test-DaTangAccountingHelpPlugV3.ps1')) `
        @('*.cs', '*.ps1') `
        'G10 活动测试或脚本仍保留 DaTang V2 阶段入口。'

    $package = New-PluginV3PackageEvidence `
        $repositoryRoot $PSScriptRoot $resultRoot $projectPath `
        'DaTangAccountingHelpPlug' $Configuration
    $manifestPath = Join-Path $package.LoadRoot 'Controls\DaTang\plugin.manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    Assert-PluginV3Manifest $manifest 'myavalonia.plugin.datang-accounting-help'

    $variableName = 'MYAVALONIA_G10_V3_PACKAGE_ROOT'
    $previousPackageRoot = [Environment]::GetEnvironmentVariable($variableName)
    try {
        [Environment]::SetEnvironmentVariable(
            $variableName, (Join-Path $package.LoadRoot 'Controls'))
        $zipSuite = [pscustomobject]@{
            Name = 'G10-FinalZip'
            Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
            Filter = 'FullyQualifiedName~G10最终测试Zip通过真实V3发现组合并进入Workspace目录'
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
        hostCoverage = [ordered]@{
            line = $hostCoverage.Line
            branch = $hostCoverage.Branch
        }
        pluginCoverage = [ordered]@{
            line = $pluginCoverage.Line
            branch = $pluginCoverage.Branch
            documentLine = $documentCoverage
            codecLine = $codecCoverage
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
        workspaceDocuments = 2
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
        "G10 DaTang V3 专项门禁通过：$totalPassed 项；" +
        "Host $($hostCoverage.Line)%/$($hostCoverage.Branch)%；" +
        "测试 ZIP $($package.FileCount) 个文件。")
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
