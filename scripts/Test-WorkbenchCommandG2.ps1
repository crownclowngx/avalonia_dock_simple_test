[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\test-results\WorkbenchCommandG2'))
$allowedRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\test-results'))

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )

    if (-not $Condition) { throw $Message }
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
    }
}

function Get-ApiFact {
    param([Parameter(Mandatory)] [string]$RelativePath)

    $path = Join-Path $repositoryRoot $RelativePath
    $lines = @(Get-Content -LiteralPath $path)
    Assert-True ($lines.Count -gt 0 -and $lines[0] -ceq '#nullable enable') `
        "API 文件缺少 nullable 头：$RelativePath。"
    return [ordered]@{
        entries = @($lines | Select-Object -Skip 1).Count
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
}

function Get-TrxCounts {
    param([Parameter(Mandatory)] [string]$Path)

    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "缺少 TRX：$Path。"
    [xml]$trx = Get-Content -Raw -LiteralPath $Path
    $counters = $trx.TestRun.ResultSummary.Counters
    Assert-True ([int]$counters.failed -eq 0) "TRX 存在失败测试：$Path。"
    Assert-True ([int]$counters.notExecuted -eq 0) "TRX 存在跳过测试：$Path。"
    return [ordered]@{
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        skipped = [int]$counters.notExecuted
    }
}

function Assert-PatternAbsent {
    param(
        [Parameter(Mandatory)] [string]$Pattern,
        [Parameter(Mandatory)] [string[]]$Paths,
        [Parameter(Mandatory)] [string]$Message
    )

    & rg --quiet $Pattern @Paths -g '*.cs'
    if ($LASTEXITCODE -eq 0) { throw $Message }
    if ($LASTEXITCODE -gt 1) { throw "结构扫描失败：$Pattern。" }
}

Assert-True ($resultRoot.StartsWith(
        $allowedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) `
    'G2 结果目录越过 artifacts/test-results 边界。'

if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null

Push-Location $repositoryRoot
try {
    # 先复用完整开发门禁执行 tool/locked restore、Release 零警告构建、Host 三层测试、
    # SDK/API、四插件真实包、覆盖率和文档验证。后面的 --no-build/--no-restore 只复用
    # 这一轮已经验证的输入，不会掩盖 lock file 漂移。
    Invoke-Checked pwsh @(
        '-NoProfile', '-File',
        (Join-Path $PSScriptRoot 'Test-HostV4DevelopmentGate.ps1'),
        '-Stage', 'G7', '-Configuration', $Configuration)

    $targetedRoot = Join-Path $resultRoot 'targeted'
    New-Item -ItemType Directory -Path $targetedRoot -Force | Out-Null
    Invoke-Checked dotnet @(
        'test',
        'Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj',
        '-c', $Configuration,
        '--no-build', '--no-restore',
        '--filter', 'FullyQualifiedName~WorkbenchCommand',
        '--results-directory', $targetedRoot,
        '--logger', 'trx;LogFileName=WorkbenchCommandG2.trx')
    $targeted = Get-TrxCounts (Join-Path $targetedRoot 'WorkbenchCommandG2.trx')
    Assert-True ([int]$targeted.passed -ge 31) `
        'G2 Workbench Command 定向测试数量低于实现时基线 31。'

    $hostSummaryPath = Join-Path $repositoryRoot `
        'artifacts\test-results\HostV4\G7\summary.json'
    Assert-True (Test-Path -LiteralPath $hostSummaryPath -PathType Leaf) `
        'Host V4 G7 没有生成 summary.json。'
    $hostSummary = Get-Content -Raw -LiteralPath $hostSummaryPath | ConvertFrom-Json
    Assert-True ([bool]$hostSummary.passed) 'Host V4 G7 摘要不是通过状态。'
    Assert-True ([double]$hostSummary.hostLineCoverage -ge 85.45) `
        'Host 行覆盖率低于 Workbench Command G0/G1 的 85.45%。'
    Assert-True ([double]$hostSummary.hostBranchCoverage -ge 71.14) `
        'Host 分支覆盖率低于 Workbench Command G0/G1 的 71.14%。'

    [xml]$versions = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot 'Directory.Version.props')
    $properties = $versions.Project.PropertyGroup
    Assert-True ([string]$properties.MyAvaloniaPluginSdkVersion -ceq '3.2.0') `
        'G2 不得提升 Core/UI SDK 版本。'
    Assert-True ([string]$properties.MyAvaloniaProductVersion -ceq '3.0.0') `
        'G2 不得提升 Host 产品版本。'
    Assert-True (
        [string]$properties.MyAvaloniaV2ManifestSchemaVersion -ceq '2' -and
        [string]$properties.MyAvaloniaV2DocumentEnvelopeSchemaVersion -ceq '2' -and
        [string]$properties.MyAvaloniaV2LayoutSchemaVersion -ceq '2' -and
        [string]$properties.MyAvaloniaV2LayoutFileName -ceq 'layout-v2.json' -and
        [string]$properties.MyAvaloniaHostDataRootGeneration -ceq 'v2') `
        'G2 改变了 manifest、Document、layout 或数据根协议。'

    $api = [ordered]@{
        coreShipped = Get-ApiFact `
            'Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v3/PublicAPI.Shipped.txt'
        coreUnshipped = Get-ApiFact `
            'Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v3/PublicAPI.Unshipped.txt'
        uiShipped = Get-ApiFact `
            'Host/MyAvaloniaManagement.PluginSdk.UI/ApiCompatibility/v3/PublicAPI.Shipped.txt'
        uiUnshipped = Get-ApiFact `
            'Host/MyAvaloniaManagement.PluginSdk.UI/ApiCompatibility/v3/PublicAPI.Unshipped.txt'
    }
    Assert-True (
        $api.coreShipped.entries -eq 127 -and
        $api.coreShipped.sha256 -ceq `
            '063BCB5852827612B0501C135D23FECD015069A6F7DDB409547157E4FA00F80F') `
        'Core v3 Shipped 被改写。'
    Assert-True (
        $api.coreUnshipped.entries -eq 91 -and
        $api.coreUnshipped.sha256 -ceq `
            'D80D43C3F4EE6A2214A0DD3B5682402CC6FC6B62FD321E48A2608A4370DDD7AA') `
        'Core v3 Unshipped 不再等于 G1 候选基线。'
    Assert-True (
        $api.uiShipped.entries -eq 45 -and
        $api.uiShipped.sha256 -ceq `
            'B11FBE768C3AD04CA65CBF5128BF6FCE8C00058EBB24052D51FE5464A65AD803') `
        'UI v3 Shipped 被改写。'
    Assert-True (
        $api.uiUnshipped.entries -eq 66 -and
        $api.uiUnshipped.sha256 -ceq `
            'C8B831D64C25615291FBFB99740EC633F07EA53B20326FA8CCD222EE6B564932') `
        'UI v3 Unshipped 不再等于 G1 候选基线。'

    $commandRoot = Join-Path $repositoryRoot `
        'Host\MyAvaloniaManagement\Business\Commands'
    Assert-PatternAbsent `
        'Avalonia\.Controls|MenuItem|KeyBinding|ICommand|IServiceProvider|IServiceScope|WorkflowAction' `
        @($commandRoot) `
        'G2 Command 内核依赖了 UI 控件、服务定位器、Scope 或 Workflow Runtime。'

    $mainViewModel = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot 'Host\MyAvaloniaManagement\ViewModels\MainWindowViewModel.cs')
    $menu = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot 'Host\MyAvaloniaManagement\Views\MenuView.axaml')
    $window = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot 'Host\MyAvaloniaManagement\Views\MainWindow.axaml')
    Assert-True (
        $mainViewModel.Contains('public async Task OpenDocument()', [StringComparison]::Ordinal) -and
        $mainViewModel.Contains('public async Task SaveDocument()', [StringComparison]::Ordinal) -and
        $menu.Contains('OpenDocumentCommand', [StringComparison]::Ordinal) -and
        $menu.Contains('SaveDocumentCommand', [StringComparison]::Ordinal) -and
        $window.Contains('Gesture="Control+S" Command="{Binding SaveDocumentCommand}"',
            [StringComparison]::Ordinal)) `
        'G2 不得提前迁移 MainWindow 菜单或 Ctrl+S。'

    Invoke-Checked pwsh @(
        '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-Documentation.ps1'))

    # 专项目录复制原始证据快照，确保 summary 中的数量可追溯到真实 TRX/Cobertura。
    $evidenceRoot = Join-Path $resultRoot 'evidence'
    $evidenceSources = [ordered]@{
        Host = Join-Path $allowedRoot 'MyAvaloniaManagement'
        MyPlugTest = Join-Path $allowedRoot 'MyPlugTestV3'
        DaTangAccountingHelpPlug = Join-Path $allowedRoot 'DaTangAccountingHelpPlugV3'
        MySmallTools = Join-Path $allowedRoot 'MySmallToolsV3'
        BiliDownloader = Join-Path $allowedRoot 'BiliDownloaderV3'
    }
    foreach ($source in $evidenceSources.GetEnumerator()) {
        Assert-True (Test-Path -LiteralPath $source.Value -PathType Container) `
            "缺少 $($source.Key) 的原始测试结果目录。"
        $sourceRoot = [IO.Path]::GetFullPath($source.Value)
        foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Where-Object {
                     $_.Extension -ceq '.trx' -or
                     $_.Name -match '^(coverage.*\.xml|Cobertura\.xml|Summary\.json|summary\.json)$'
                 }) {
            $relativePath = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
            $destination = Join-Path (Join-Path $evidenceRoot $source.Key) $relativePath
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) `
                -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        }
    }
    $evidenceTrxCount = @(
        Get-ChildItem -LiteralPath $evidenceRoot -Recurse -Filter '*.trx').Count
    $evidenceCoverageCount = @(
        Get-ChildItem -LiteralPath $evidenceRoot -Recurse -File |
            Where-Object { $_.Name -match '^(coverage.*\.xml|Cobertura\.xml)$' }).Count
    Assert-True ($evidenceTrxCount -ge 27) `
        'G2 专项目录没有收集完整的 Host/四插件 TRX。'
    Assert-True ($evidenceCoverageCount -gt 0) `
        'G2 专项目录没有收集真实覆盖率文件。'

    $pluginPassed = 0
    foreach ($plugin in $hostSummary.plugins.PSObject.Properties) {
        Assert-True ([int]$plugin.Value.failed -eq 0) "$($plugin.Name) 存在失败测试。"
        Assert-True ([int]$plugin.Value.skipped -eq 0) "$($plugin.Name) 存在跳过测试。"
        $pluginPassed += [int]$plugin.Value.passed
    }

    $summary = [ordered]@{
        schemaVersion = 1
        stage = 'G2'
        configuration = $Configuration
        inputCommit = 'e4278fec31271c72467a52c5f309af984bb53354'
        targeted = $targeted
        hostPassed = [int]$hostSummary.hostPassed
        pluginPassed = $pluginPassed
        hostLineCoverage = [double]$hostSummary.hostLineCoverage
        hostBranchCoverage = [double]$hostSummary.hostBranchCoverage
        api = $api
        sdkVersion = [string]$properties.MyAvaloniaPluginSdkVersion
        productVersion = [string]$properties.MyAvaloniaProductVersion
        evidenceTrxFiles = $evidenceTrxCount
        evidenceCoverageFiles = $evidenceCoverageCount
        passed = $true
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        published = $false
        uploaded = $false
        tagCreated = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))

    Write-Host (
        "Workbench Command G2 通过：定向 $($targeted.passed) 项，" +
        "Host $($summary.hostPassed) 项，四插件聚合 $pluginPassed 项，" +
        "覆盖率 $($summary.hostLineCoverage)% / $($summary.hostBranchCoverage)%。")
}
finally {
    Pop-Location
}
