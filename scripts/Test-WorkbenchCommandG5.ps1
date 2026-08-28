[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$ReuseVerifiedBaseGate
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\test-results'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $allowedRoot 'WorkbenchCommandG5'))
$inputCommit = 'e233196d3c7c70bfb99ca69e9151b14bf158dc33'
$inputTree = 'cee16d78489cf66374652fe5cc3156aa3ea16d30'

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

function Get-FileLineCoverage {
    param(
        [Parameter(Mandatory)] [xml]$Coverage,
        [Parameter(Mandatory)] [string]$RelativePath
    )

    $suffix = $RelativePath.Replace('/', '\')
    $hits = @{}
    foreach ($class in $Coverage.coverage.packages.package.classes.class) {
        $filename = ([string]$class.filename).Replace('/', '\')
        if (-not $filename.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        foreach ($line in $class.lines.line) {
            $number = [int]$line.number
            $hit = [int]$line.hits
            if (-not $hits.ContainsKey($number) -or $hit -gt $hits[$number]) {
                $hits[$number] = $hit
            }
        }
    }
    Assert-True ($hits.Count -gt 0) "覆盖率报告缺少关键文件：$RelativePath。"
    $covered = @($hits.Values | Where-Object { $_ -gt 0 }).Count
    return [Math]::Round(100.0 * $covered / $hits.Count, 2)
}

function Assert-PatternAbsent {
    param(
        [Parameter(Mandatory)] [string]$Pattern,
        [Parameter(Mandatory)] [string[]]$Paths,
        [Parameter(Mandatory)] [string]$Message
    )

    & rg --quiet $Pattern @Paths
    if ($LASTEXITCODE -eq 0) { throw $Message }
    if ($LASTEXITCODE -gt 1) { throw "结构扫描失败：$Pattern。" }
}

Assert-True ($resultRoot.StartsWith(
        $allowedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) `
    'G5 结果目录越过 artifacts/test-results 边界。'

if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null

Push-Location $repositoryRoot
try {
    $resolvedInputTree = (& git rev-parse "$inputCommit`^{tree}").Trim()
    Assert-True ($LASTEXITCODE -eq 0 -and $resolvedInputTree -ceq $inputTree) `
        'G5 输入 commit/tree 与签署基线不一致。'
    & git merge-base --is-ancestor $inputCommit HEAD
    Assert-True ($LASTEXITCODE -eq 0) `
        '当前 HEAD 不是 G5 签署输入提交的后继。'

    # 这里只调用仓内既有的本地开发门禁。G5 不调用 Windows CI/Smoke，
    # 也不调用发布验收、发布门禁、签名、上传或 tag 入口。
    if (-not $ReuseVerifiedBaseGate) {
        Invoke-Checked pwsh @(
            '-NoProfile', '-File',
            (Join-Path $PSScriptRoot 'Test-HostV4DevelopmentGate.ps1'),
            '-Stage', 'G7', '-Configuration', $Configuration)
    }

    $targetedRoot = Join-Path $resultRoot 'targeted'
    New-Item -ItemType Directory -Path $targetedRoot -Force | Out-Null
    Invoke-Checked dotnet @(
        'test',
        'Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj',
        '-c', $Configuration,
        '--no-build', '--no-restore',
        '--filter', 'FullyQualifiedName~WorkbenchCommand',
        '--results-directory', $targetedRoot,
        '--logger', 'trx;LogFileName=WorkbenchCommandG5.Unit.trx')
    $unitTargeted = Get-TrxCounts (Join-Path $targetedRoot 'WorkbenchCommandG5.Unit.trx')
    Assert-True ([int]$unitTargeted.passed -ge 57) `
        'G5 Workbench Command 单元定向测试数量低于实现时基线 57。'

    Invoke-Checked dotnet @(
        'test',
        'Host/MyAvaloniaManagement.UiTests/MyAvaloniaManagement.UiTests.csproj',
        '-c', $Configuration,
        '--no-build', '--no-restore',
        '--filter', 'FullyQualifiedName~WorkbenchCommandPresentationUiTests',
        '--results-directory', $targetedRoot,
        '--logger', 'trx;LogFileName=WorkbenchCommandG5.Ui.trx')
    $uiTargeted = Get-TrxCounts (Join-Path $targetedRoot 'WorkbenchCommandG5.Ui.trx')
    Assert-True ([int]$uiTargeted.passed -ge 7) `
        'G5 Workbench Command Headless UI 定向测试数量低于实现时基线 7。'

    $hostSummaryPath = Join-Path $repositoryRoot 'artifacts\test-results\HostV4\G7\summary.json'
    Assert-True (Test-Path -LiteralPath $hostSummaryPath -PathType Leaf) `
        'Host V4 G7 没有生成 summary.json。'
    $hostSummary = Get-Content -Raw -LiteralPath $hostSummaryPath | ConvertFrom-Json
    Assert-True ([bool]$hostSummary.passed) 'Host V4 G7 摘要不是通过状态。'

    $coveragePath = Join-Path $repositoryRoot `
        'artifacts\test-results\MyAvaloniaManagement\coverage\Cobertura.xml'
    Assert-True (Test-Path -LiteralPath $coveragePath -PathType Leaf) `
        'Host 覆盖率门禁没有生成 Cobertura.xml。'
    [xml]$coverage = Get-Content -Raw -LiteralPath $coveragePath
    $hostLineCoverage = [Math]::Round([double]$coverage.coverage.'line-rate' * 100, 2)
    $hostBranchCoverage = [Math]::Round([double]$coverage.coverage.'branch-rate' * 100, 2)
    Assert-True ($hostLineCoverage -ge 86.55) 'G5 Host 行覆盖率低于 G4 的 86.55%。'
    Assert-True ($hostBranchCoverage -ge 72.03) 'G5 Host 分支覆盖率低于 G4 的 72.03%。'

    $criticalCoverage = [ordered]@{}
    foreach ($relativePath in @(
            'Business/Presentation/Commands/HostWorkbenchCommandPresentation.cs',
            'Business/Presentation/Commands/WorkbenchCommandProjection.cs',
            'Business/Lifecycle/PluginLifecycleStateStore.cs')) {
        $rate = Get-FileLineCoverage $coverage $relativePath
        Assert-True ($rate -ge 90.0) `
            "$relativePath 行覆盖率低于 G5 关键文件阈值 90%。"
        $criticalCoverage[$relativePath] = $rate
    }

    [xml]$versions = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props')
    $properties = $versions.Project.PropertyGroup
    Assert-True ([string]$properties.MyAvaloniaPluginSdkVersion -ceq '3.2.0') `
        'G5 不得提升 Core/UI SDK 版本。'
    Assert-True ([string]$properties.MyAvaloniaProductVersion -ceq '3.0.0') `
        'G5 不得提升 Host 产品版本。'
    Assert-True (
        [string]$properties.MyAvaloniaV2ManifestSchemaVersion -ceq '2' -and
        [string]$properties.MyAvaloniaV2DocumentEnvelopeSchemaVersion -ceq '2' -and
        [string]$properties.MyAvaloniaV2LayoutSchemaVersion -ceq '2' -and
        [string]$properties.MyAvaloniaV2LayoutFileName -ceq 'layout-v2.json' -and
        [string]$properties.MyAvaloniaHostDataRootGeneration -ceq 'v2') `
        'G5 改变了 manifest、Document、layout 或数据根协议。'

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
    Assert-True ($api.coreShipped.entries -eq 127 -and $api.coreShipped.sha256 -ceq `
        '063BCB5852827612B0501C135D23FECD015069A6F7DDB409547157E4FA00F80F') `
        'Core v3 Shipped 被改写。'
    Assert-True ($api.coreUnshipped.entries -eq 91 -and $api.coreUnshipped.sha256 -ceq `
        'D80D43C3F4EE6A2214A0DD3B5682402CC6FC6B62FD321E48A2608A4370DDD7AA') `
        'Core v3 Unshipped 被改写。'
    Assert-True ($api.uiShipped.entries -eq 45 -and $api.uiShipped.sha256 -ceq `
        'B11FBE768C3AD04CA65CBF5128BF6FCE8C00058EBB24052D51FE5464A65AD803') `
        'UI v3 Shipped 被改写。'
    Assert-True ($api.uiUnshipped.entries -eq 66 -and $api.uiUnshipped.sha256 -ceq `
        'C8B831D64C25615291FBFB99740EC633F07EA53B20326FA8CCD222EE6B564932') `
        'UI v3 Unshipped 被改写。'

    $sdkAndRegistry = @(
        (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginSdk'),
        (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginSdk.UI'),
        (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement\Business\Plugins\Registration\PluginRegistry.cs'))
    Assert-PatternAbsent `
        'new\s+(MenuItem|KeyBinding)\b|Avalonia\.Controls\.MenuItem|Avalonia\.Input\.KeyBinding' `
        $sdkAndRegistry `
        'SDK 或 Registry 创建/持有了 MenuItem 或 KeyBinding。'

    $projectionRoot = Join-Path $repositoryRoot `
        'Host\MyAvaloniaManagement\Business\Presentation\Commands'
    Assert-PatternAbsent `
        'Avalonia\.Controls|new\s+(MenuItem|Separator|KeyBinding)\b|DataContext|IServiceProvider|IServiceScope|Dock\.Model|WorkflowAction' `
        @($projectionRoot) `
        '投影内核持有 UI 控件、DataContext、Provider、Dock 或 Workflow Runtime。'

    $menuPath = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement\Views\MenuView.axaml'
    $windowPath = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement\Views\MainWindow.axaml'
    $menu = Get-Content -Raw -LiteralPath $menuPath
    $window = Get-Content -Raw -LiteralPath $windowPath
    foreach ($name in @('FileMenu', 'ViewMenu', 'ToolsMenu', 'HelpMenu')) {
        Assert-True ($menu.Contains("x:Name=`"$name`"", [StringComparison]::Ordinal)) `
            "缺少 Host 顶级菜单容器：$name。"
    }
    Assert-True ($menu.Contains('Header="主题"', [StringComparison]::Ordinal)) `
        'View 菜单的 Host 静态主题项丢失。'
    Assert-PatternAbsent `
        'WorkbenchCommands\.(Open|Save)|Header="(打开|保存)"' `
        @($menuPath) `
        'MenuView 重新出现静态打开/保存双入口。'
    Assert-PatternAbsent `
        '<Window\.KeyBindings>|Gesture="(Control|Ctrl)\+S"|WorkbenchCommands\.Save' `
        @($windowPath) `
        'MainWindow 重新出现静态 Ctrl+S 双入口。'

    $hostCatalogPath = Join-Path $projectionRoot 'HostWorkbenchCommandPresentation.cs'
    $hostCatalog = Get-Content -Raw -LiteralPath $hostCatalogPath
    foreach ($identity in @(
            'myavalonia.host.command-placement.menu.file.open-document',
            'myavalonia.host.command-placement.menu.file.save-document',
            'myavalonia.host.command-placement.key-binding.save-document')) {
        Assert-True ($hostCatalog.Contains($identity, [StringComparison]::Ordinal)) `
            "缺少 G5 Host 内建投影身份：$identity。"
    }

    Invoke-Checked pwsh @(
        '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-Documentation.ps1'))

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
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        }
    }
    $evidenceTrxCount = @(Get-ChildItem -LiteralPath $evidenceRoot -Recurse -Filter '*.trx').Count
    $evidenceCoverageCount = @(
        Get-ChildItem -LiteralPath $evidenceRoot -Recurse -File |
            Where-Object { $_.Name -match '^(coverage.*\.xml|Cobertura\.xml)$' }).Count
    Assert-True ($evidenceTrxCount -ge 27) `
        'G5 专项目录没有收集完整的 Host/四插件 TRX。'
    Assert-True ($evidenceCoverageCount -gt 0) `
        'G5 专项目录没有收集真实覆盖率文件。'

    $pluginPassed = 0
    foreach ($plugin in $hostSummary.plugins.PSObject.Properties) {
        Assert-True ([int]$plugin.Value.failed -eq 0) "$($plugin.Name) 存在失败测试。"
        Assert-True ([int]$plugin.Value.skipped -eq 0) "$($plugin.Name) 存在跳过测试。"
        $pluginPassed += [int]$plugin.Value.passed
    }

    $summary = [ordered]@{
        schemaVersion = 1
        stage = 'G5'
        configuration = $Configuration
        inputCommit = $inputCommit
        inputTree = $inputTree
        baseGateReused = [bool]$ReuseVerifiedBaseGate
        unitTargeted = $unitTargeted
        uiTargeted = $uiTargeted
        hostPassed = [int]$hostSummary.hostPassed
        pluginPassed = $pluginPassed
        hostLineCoverage = $hostLineCoverage
        hostBranchCoverage = $hostBranchCoverage
        criticalLineCoverage = $criticalCoverage
        api = $api
        sdkVersion = [string]$properties.MyAvaloniaPluginSdkVersion
        productVersion = [string]$properties.MyAvaloniaProductVersion
        schema = [ordered]@{
            manifest = [string]$properties.MyAvaloniaV2ManifestSchemaVersion
            documentEnvelope = [string]$properties.MyAvaloniaV2DocumentEnvelopeSchemaVersion
            layout = [string]$properties.MyAvaloniaV2LayoutSchemaVersion
            layoutFileName = [string]$properties.MyAvaloniaV2LayoutFileName
            dataRoot = [string]$properties.MyAvaloniaHostDataRootGeneration
        }
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
        signed = $false
        tagCreated = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))

    Write-Host (
        "Workbench Command G5 通过：Unit 定向 $($unitTargeted.passed) 项，" +
        "UI 定向 $($uiTargeted.passed) 项，Host $($summary.hostPassed) 项，" +
        "四插件聚合 $pluginPassed 项，覆盖率 " +
        "$($summary.hostLineCoverage)% / $($summary.hostBranchCoverage)%。")
}
finally {
    Pop-Location
}
