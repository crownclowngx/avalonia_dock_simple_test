[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('G1', 'G2', 'G3', 'G4', 'G5', 'G6', 'G7')]
    [string]$Stage,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\test-results\HostV4\$Stage"))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\test-results\HostV4'))

if (-not $resultRoot.StartsWith(
        $allowedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "V4 开发门禁结果目录越界：$resultRoot。"
}

function Invoke-Checked {
    param([string]$FilePath, [string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
    }
}

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )

    if (-not $Condition) { throw $Message }
}

function Get-RequiredJsonProperty {
    param(
        [Parameter(Mandatory)] $InputObject,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Description
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "$Description 缺少必需字段：$Name。"
    }
    return $property.Value
}

function Assert-PatternAbsent {
    param([string]$Pattern, [string[]]$Paths, [string[]]$Globs, [string]$Message)
    $arguments = @('--quiet', $Pattern) + $Paths
    foreach ($glob in $Globs) { $arguments += @('-g', $glob) }
    & rg @arguments
    if ($LASTEXITCODE -eq 0) { throw $Message }
    if ($LASTEXITCODE -gt 1) { throw "结构扫描失败：$Pattern。" }
}

if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$stageNumber = [int]$Stage.Substring(1)
$hostRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement'

Push-Location $repositoryRoot
try {
    # 该入口只执行开发期本地验证。它不会调用 AIFLOW、Windows CI/Smoke、
    # ReleaseAcceptance、Host Release Gate、标签、上传或发布命令。
    Invoke-Checked dotnet @('tool', 'restore')
    Invoke-Checked dotnet @('restore', 'MyAvaloniaManagement.sln', '--locked-mode')
    Invoke-Checked dotnet @(
        'build', 'MyAvaloniaManagement.sln',
        '-c', $Configuration,
        '--no-restore',
        '-warnaserror')

    & (Join-Path $PSScriptRoot 'Invoke-MyAvaloniaManagementTests.ps1') `
        -Configuration $Configuration -NoRestore
    if ($LASTEXITCODE -ne 0) { throw 'Host 三层测试或覆盖率门禁失败。' }

    Assert-PatternAbsent `
        'IDropTarget|DragDrop\.AllowDrop|Microsoft\.Extensions\.Hosting' `
        @($hostRoot, (Join-Path $repositoryRoot 'Directory.Packages.props')) `
        @('*.cs', '*.axaml', '*.csproj', '*.props') `
        'G1 已删除的拖放面或 Hosting 直接依赖重新出现。'
    Assert-PatternAbsent '<Separator\s*/>' `
        @((Join-Path $hostRoot 'Views\MenuView.axaml')) @('*.axaml') `
        '文件菜单重新出现悬空 Separator。'

    if ($stageNumber -ge 2) {
        Assert-PatternAbsent `
            'DockNameConstant|CreateDocumentAsync\(string|OpenDocumentByPath|public\s+async\s+Task\s+CreateDocument\(string' `
            @($hostRoot) @('*.cs') `
            'G2 已删除的字符串身份或 ViewModel 用例转发重新出现。'
    }

    if ($stageNumber -ge 3) {
        $layoutRoot = Join-Path $hostRoot 'Business\Layout'
        foreach ($file in @(
                'DockLayoutLifecycle.cs',
                'DockLayoutSnapshotMapper.cs',
                'DockLayoutRuntimeValidator.cs')) {
            if (-not (Test-Path -LiteralPath (Join-Path $layoutRoot $file) -PathType Leaf)) {
                throw "G3 Layout 职责文件缺失：$file。"
            }
        }
    }

    if ($stageNumber -ge 4) {
        Assert-PatternAbsent `
            'Application\.Current[^;\r\n]*Resources' `
            @((Join-Path $hostRoot 'Business')) @('*.cs') `
            'G4 业务或生命周期代码重新通过 Application.Current 查找回收器。'
    }

    if ($stageNumber -ge 5) {
        if (Test-Path -LiteralPath (Join-Path $hostRoot 'Business\Helpers')) {
            throw 'G5 完成后 Business/Helpers 必须不存在。'
        }
        if (Test-Path -LiteralPath (Join-Path $hostRoot 'Common\Utils\Misc')) {
            throw 'G5 完成后 Common/Utils/Misc 必须不存在。'
        }
        Assert-PatternAbsent `
            'MyAvaloniaManagement\.Business\.Helpers|MyAvaloniaManagement\.(ViewModels|Views)\.Hello' `
            @($hostRoot) @('*.cs', '*.axaml') `
            'G5 完成后旧 Helpers 或 Hello 命名空间不得存在。'
    }

    if ($stageNumber -ge 6) {
        foreach ($file in @(
                'Models\FileSystem\FileSystemPath.cs',
                'Business\Constants\PluginDeploymentConstants.cs')) {
            if (-not (Test-Path -LiteralPath (Join-Path $hostRoot $file) -PathType Leaf)) {
                throw "G6 路径或部署常量文件缺失：$file。"
            }
        }
        Assert-PatternAbsent `
            'AssemblyLoadConstant|PLUGINS_SUBDIRECTORY|class\s+FileHelper' `
            @($hostRoot) @('*.cs') `
            'G6 完成后旧部署常量或 FileHelper 不得存在。'
        $storageContract = Get-Content -Raw -LiteralPath (
            Join-Path $hostRoot 'Business\Storage\IHostStorageService.cs')
        if ($storageContract -notmatch 'bool\s+DirectoryExists\s*\(string\s+path\)') {
            throw 'G6 完成后 IHostStorageService 必须提供 DirectoryExists 存在性端口。'
        }
    }

    $hostSummary = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot 'artifacts\test-results\MyAvaloniaManagement\summary.json') |
        ConvertFrom-Json
    if ([double]$hostSummary.lineCoverage -lt 84.39 -or
        [double]$hostSummary.branchCoverage -lt 70.58) {
        throw 'Host 覆盖率低于 V4 G0 的 84.39% / 70.58%。'
    }

    $g7Evidence = $null
    if ($stageNumber -ge 7) {
        # G7 是集成回归，不重新实现 SDK 或四插件的专项规则。这里通过独立 pwsh 进程串行复用
        # 已签署的开发期入口：既避免共享 PowerShell 全局状态，也避免 Avalonia、部署目录、
        # LibVLC 原生资源和测试输出并行互相污染。Release 仅是编译配置，不代表发布批准。
        Invoke-Checked pwsh @(
            '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-PluginSdkCompatibility.ps1'),
            '-Baseline', 'v3', '-Configuration', $Configuration)
        Invoke-Checked pwsh @(
            '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-PluginSdkPackage.ps1'),
            '-Configuration', $Configuration)
        Invoke-Checked pwsh @(
            '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-HostDiagnosticRedaction.ps1'))

        $pluginGates = @(
            [pscustomobject]@{
                Name = 'MyPlugTest'
                Script = 'Test-MyPlugTestV3.ps1'
                ResultDirectory = 'MyPlugTestV3'
                PluginId = 'myavalonia.plugin.my-plug-test'
                ExpectedPluginVersion = '3.0.0'
                ExpectedSdkMinInclusive = '3.2.0'
                ExpectedSuites = @(
                    'G9-PluginSdk', 'G9-HostUnit', 'G9-HeadlessUi',
                    'G9-PluginDock', 'G9-MyPlugTest', 'G9-FinalZip')
                AdditionalArguments = @()
            },
            [pscustomobject]@{
                Name = 'DaTangAccountingHelpPlug'
                Script = 'Test-DaTangAccountingHelpPlugV3.ps1'
                ResultDirectory = 'DaTangAccountingHelpPlugV3'
                PluginId = 'myavalonia.plugin.datang-accounting-help'
                ExpectedPluginVersion = '3.0.0'
                ExpectedSdkMinInclusive = '3.2.0'
                ExpectedSuites = @(
                    'G10-PluginSdk', 'G10-HostUnit', 'G10-HeadlessUi',
                    'G10-PluginDock', 'G10-DaTang', 'G10-FinalZip')
                AdditionalArguments = @()
            },
            [pscustomobject]@{
                Name = 'MySmallTools'
                Script = 'Test-MySmallToolsV3.ps1'
                ResultDirectory = 'MySmallToolsV3'
                PluginId = 'myavalonia.plugin.my-small-tools'
                ExpectedPluginVersion = '3.1.0'
                ExpectedSdkMinInclusive = '3.2.0'
                ExpectedSuites = @(
                    'G11-PluginSdk', 'G11-HostUnit', 'G11-HeadlessUi',
                    'G11-PluginDock', 'G11-MySmallTools', 'G11-FinalZip')
                AdditionalArguments = @('-HarnessCycles', '20')
            },
            [pscustomobject]@{
                Name = 'BiliDownloader'
                Script = 'Test-BiliDownloaderV3.ps1'
                ResultDirectory = 'BiliDownloaderV3'
                PluginId = 'myavalonia.plugin.bili-downloader'
                ExpectedPluginVersion = '3.0.0'
                ExpectedSdkMinInclusive = '3.2.0'
                ExpectedSuites = @(
                    'G12-PluginSdk', 'G12-HostUnit', 'G12-HeadlessUi',
                    'G12-PluginDock', 'G12-BiliDownloader', 'G12-FinalZip')
                AdditionalArguments = @()
            }
        )

        $pluginEvidence = [ordered]@{}
        foreach ($pluginGate in $pluginGates) {
            $arguments = @(
                '-NoProfile', '-File', (Join-Path $PSScriptRoot $pluginGate.Script),
                '-Configuration', $Configuration, '-NoRestore') + $pluginGate.AdditionalArguments
            Invoke-Checked pwsh $arguments

            $pluginSummaryPath = Join-Path $repositoryRoot (
                "artifacts\test-results\$($pluginGate.ResultDirectory)\summary.json")
            Assert-True (Test-Path -LiteralPath $pluginSummaryPath -PathType Leaf) (
                "$($pluginGate.Name) 专项没有生成 summary.json。")
            $pluginSummary = Get-Content -Raw -LiteralPath $pluginSummaryPath | ConvertFrom-Json
            $description = "$($pluginGate.Name) G7 专项摘要"

            # 子脚本已经逐个检查 TRX、覆盖率、包内容和真实 Host 加载；聚合层仍校验稳定接口，
            # 防止脚本意外提前退出、摘要沿用旧结构，或发布类入口被误接入 G7 开发门禁。
            Assert-True ([int](Get-RequiredJsonProperty $pluginSummary 'schemaVersion' $description) -eq 1) (
                "$description schemaVersion 必须为 1。")
            Assert-True ((Get-RequiredJsonProperty $pluginSummary 'configuration' $description) -ceq $Configuration) (
                "$description 编译配置与 G7 请求不一致。")
            Assert-True ([int](Get-RequiredJsonProperty $pluginSummary 'passed' $description) -gt 0) (
                "$description 没有实际通过测试。")
            Assert-True ([int](Get-RequiredJsonProperty $pluginSummary 'failed' $description) -eq 0) (
                "$description 存在失败测试。")
            Assert-True ([int](Get-RequiredJsonProperty $pluginSummary 'skipped' $description) -eq 0) (
                "$description 存在跳过测试。")
            Assert-True ([int](Get-RequiredJsonProperty $pluginSummary 'deterministicBuilds' $description) -eq 2) (
                "$description 没有完成两次确定性测试包构建。")

            $suites = Get-RequiredJsonProperty $pluginSummary 'suites' $description
            $suiteProperties = @($suites.PSObject.Properties)
            Assert-True ($suiteProperties.Count -eq $pluginGate.ExpectedSuites.Count) (
                "$description 的套件数量与 G7 锁定清单不一致。")
            $suitePassed = 0
            foreach ($suiteName in $pluginGate.ExpectedSuites) {
                Assert-True ($null -ne $suites.PSObject.Properties[$suiteName]) (
                    "$description 缺少必需套件：$suiteName。")
                Assert-True ([int]$suites.$suiteName -gt 0) (
                    "$description 套件 $suiteName 没有实际通过测试。")
                $suitePassed += [int]$suites.$suiteName
            }
            Assert-True ($suitePassed -eq [int]$pluginSummary.passed) (
                "$description 的套件通过数之和与摘要总数不一致。")

            $manifest = Get-RequiredJsonProperty $pluginSummary 'manifest' $description
            Assert-True (
                [int]$manifest.schemaVersion -eq 2 -and
                $manifest.pluginId -ceq $pluginGate.PluginId -and
                $manifest.pluginVersion -ceq $pluginGate.ExpectedPluginVersion -and
                $manifest.sdkMinInclusive -ceq $pluginGate.ExpectedSdkMinInclusive -and
                $manifest.sdkMaxExclusive -ceq '4.0.0') (
                "$description manifest 身份、版本、schema 或 SDK 区间不正确。")
            Assert-True (
                [string](Get-RequiredJsonProperty $pluginSummary 'archiveSha256' $description) -match '^[0-9A-F]{64}$') (
                "$description 缺少规范的测试 ZIP SHA-256。")
            Assert-True ([int](Get-RequiredJsonProperty $pluginSummary 'packageFiles' $description) -gt 0) (
                "$description 测试 ZIP 没有文件事实。")
            Assert-True ([int](Get-RequiredJsonProperty $pluginSummary 'workspaceDocuments' $description) -gt 0) (
                "$description 没有真实 Host Loader 创建的 Document 事实。")
            Assert-True ([int](Get-RequiredJsonProperty $pluginSummary 'workspaceTools' $description) -ge 0) (
                "$description 缺少真实 Host Loader 的 Tool 计数。")

            foreach ($flag in @(
                    'aiflow', 'windowsCi', 'windowsSmoke',
                    'releaseAcceptance', 'releaseGate', 'publishable')) {
                Assert-True (
                    (Get-RequiredJsonProperty $pluginSummary $flag $description) -is [bool] -and
                    -not [bool]$pluginSummary.$flag) (
                    "$description 的非发布标记 $flag 必须为 false。")
            }

            if ($pluginGate.Name -ceq 'MySmallTools') {
                $harness = Get-RequiredJsonProperty $pluginSummary 'harness' $description
                Assert-True (
                    $harness.suite -ceq 'g3' -and
                    [int]$harness.cycles -eq 20 -and
                    [bool]$harness.success -and
                    [bool]$harness.allFinalResourcesZero -and
                    [int]$harness.aliveClosedDocuments -eq 0 -and
                    [int]$harness.aliveClosedViews -eq 0 -and
                    [int]$harness.aliveDisposedEncryptedStreams -eq 0) (
                    'MySmallTools 20 轮真实媒体、全屏关闭或 Runtime 退出后仍有资源存活。')
            }

            $pluginCoverage = if ($null -ne $pluginSummary.PSObject.Properties['pluginCoverage']) {
                $pluginSummary.pluginCoverage
            }
            else {
                Get-RequiredJsonProperty $pluginSummary 'myPlugTestCoverage' $description
            }
            $pluginEvidence[$pluginGate.Name] = [ordered]@{
                passed = [int]$pluginSummary.passed
                failed = [int]$pluginSummary.failed
                skipped = [int]$pluginSummary.skipped
                suites = $pluginSummary.suites
                hostCoverage = $pluginSummary.hostCoverage
                pluginCoverage = $pluginCoverage
                manifest = $pluginSummary.manifest
                archiveSha256 = [string]$pluginSummary.archiveSha256
                packageFiles = [int]$pluginSummary.packageFiles
                deterministicBuilds = [int]$pluginSummary.deterministicBuilds
                workspaceDocuments = [int]$pluginSummary.workspaceDocuments
                workspaceTools = [int]$pluginSummary.workspaceTools
                harness = if ($pluginGate.Name -ceq 'MySmallTools') {
                    $pluginSummary.harness
                }
                else { $null }
            }
        }

        $g7Evidence = [ordered]@{
            sdkCompatibility = $true
            sdkPackageConsumption = $true
            diagnosticRedaction = $true
            plugins = $pluginEvidence
        }
    }

    & (Join-Path $PSScriptRoot 'Test-Documentation.ps1')
    if ($LASTEXITCODE -ne 0) { throw '文档门禁失败。' }

    $summary = [ordered]@{
        schemaVersion = 1
        stage = $Stage
        configuration = $Configuration
        hostPassed = [int]$hostSummary.passed
        hostLineCoverage = [double]$hostSummary.lineCoverage
        hostBranchCoverage = [double]$hostSummary.branchCoverage
        passed = $true
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    if ($null -ne $g7Evidence) {
        $summary['sdkCompatibility'] = $g7Evidence.sdkCompatibility
        $summary['sdkPackageConsumption'] = $g7Evidence.sdkPackageConsumption
        $summary['diagnosticRedaction'] = $g7Evidence.diagnosticRedaction
        $summary['plugins'] = $g7Evidence.plugins
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))
    Write-Host "$Stage Host V4 本地开发门禁通过。"
}
finally {
    Pop-Location
}
