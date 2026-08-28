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
$resultRoot = [IO.Path]::GetFullPath((Join-Path $allowedRoot 'WorkbenchCommandG9'))
$inputCommit = 'af5ed4da562a6bfaca97a7a5c8989fee41a60c03'

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

function Assert-ApiFile {
    param(
        [Parameter(Mandatory)] [string]$RelativePath,
        [Parameter(Mandatory)] [int]$Entries,
        [Parameter(Mandatory)] [string]$Sha256
    )
    $path = Join-Path $repositoryRoot $RelativePath
    $lines = @(Get-Content -LiteralPath $path)
    Assert-True ($lines.Count -gt 0 -and $lines[0] -ceq '#nullable enable') `
        "API 文件缺少 nullable 头：$RelativePath。"
    Assert-True (@($lines | Select-Object -Skip 1).Count -eq $Entries) `
        "G9 改变了 API 条目数量：$RelativePath。"
    Assert-True ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $Sha256) `
        "G9 改写了 API 基线：$RelativePath。"
}

$prefix = $allowedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
Assert-True ($resultRoot.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) `
    'G9 结果目录越过 artifacts/test-results 边界。'

if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null

Push-Location $repositoryRoot
try {
    & git merge-base --is-ancestor $inputCommit HEAD
    Assert-True ($LASTEXITCODE -eq 0) '当前 HEAD 不是 G9 输入提交的后继。'

    # 本入口只组合开发期本地验证。Release 是编译配置，不表示发布批准；不得从这里
    # 调用 AIFLOW、Windows CI/Smoke、Release Acceptance、Host Release Gate、上传或 tag。
    if (-not $ReuseVerifiedBaseGate) {
        Invoke-Checked pwsh @(
            '-NoProfile', '-File',
            (Join-Path $PSScriptRoot 'Test-HostV4DevelopmentGate.ps1'),
            '-Stage', 'G7', '-Configuration', $Configuration)
    }

    # 复用模式只接受同一工作树上由 Host 三层覆盖率门禁生成的机器摘要；它不是“跳过验证”。
    # 这样可以在已取得 Unit/UI/Plugin 与覆盖率证据后，避免重复进入四插件媒体 Harness，
    # 同时仍由下面的数量、覆盖率和 G9 定向测试断言阻止陈旧或不完整结果被误用。
    $hostSummaryPath = if ($ReuseVerifiedBaseGate) {
        Join-Path $repositoryRoot 'artifacts\test-results\MyAvaloniaManagement\summary.json'
    }
    else {
        Join-Path $repositoryRoot 'artifacts\test-results\HostV4\G7\summary.json'
    }
    Assert-True (Test-Path -LiteralPath $hostSummaryPath -PathType Leaf) `
        '缺少可复用的 Host 本地开发门禁 summary.json。'
    $hostSummary = Get-Content -Raw -LiteralPath $hostSummaryPath | ConvertFrom-Json
    $hostPassed = if ($ReuseVerifiedBaseGate) { [int]$hostSummary.passed } else { [int]$hostSummary.hostPassed }
    $hostLineCoverage = if ($ReuseVerifiedBaseGate) { [double]$hostSummary.lineCoverage } else { [double]$hostSummary.hostLineCoverage }
    $hostBranchCoverage = if ($ReuseVerifiedBaseGate) { [double]$hostSummary.branchCoverage } else { [double]$hostSummary.hostBranchCoverage }
    if ($ReuseVerifiedBaseGate) {
        Assert-True ($hostPassed -ge 584) '复用的 Host 三层测试数量低于 G9 实现基线 584。'
        Assert-True (-not [bool]$hostSummary.windowsSmoke) '复用证据不得来自 Windows Smoke。'
    }
    else {
        Assert-True ([bool]$hostSummary.passed) 'Host V4 G7 本地开发门禁摘要不是通过状态。'
    }
    Assert-True ($hostLineCoverage -ge 86.98) `
        'G9 Host 行覆盖率低于 G8 的 86.98%。'
    Assert-True ($hostBranchCoverage -ge 72.42) `
        'G9 Host 分支覆盖率低于 G8 的 72.42%。'

    $targetedRoot = Join-Path $resultRoot 'targeted'
    New-Item -ItemType Directory -Path $targetedRoot -Force | Out-Null
    Invoke-Checked dotnet @(
        'test', 'Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj',
        '-c', $Configuration, '--no-build', '--no-restore', '-m:1',
        '--filter', 'FullyQualifiedName~WorkbenchCommandProjectionTests',
        '--results-directory', $targetedRoot,
        '--logger', 'trx;LogFileName=WorkbenchCommandG9.Unit.trx')
    $unitTargeted = Get-TrxCounts (Join-Path $targetedRoot 'WorkbenchCommandG9.Unit.trx')
    Assert-True ([int]$unitTargeted.passed -ge 13) `
        'G9 Palette 单元定向测试数量低于实现基线 13。'

    Invoke-Checked dotnet @(
        'test', 'Host/MyAvaloniaManagement.UiTests/MyAvaloniaManagement.UiTests.csproj',
        '-c', $Configuration, '--no-build', '--no-restore', '-m:1',
        '--filter', 'FullyQualifiedName~WorkbenchCommandPresentationUiTests',
        '--results-directory', $targetedRoot,
        '--logger', 'trx;LogFileName=WorkbenchCommandG9.Ui.trx')
    $uiTargeted = Get-TrxCounts (Join-Path $targetedRoot 'WorkbenchCommandG9.Ui.trx')
    Assert-True ([int]$uiTargeted.passed -ge 10) `
        'G9 Palette Headless UI 定向测试数量低于实现基线 10。'

    $coveragePath = Join-Path $repositoryRoot `
        'artifacts\test-results\MyAvaloniaManagement\coverage\Cobertura.xml'
    Assert-True (Test-Path -LiteralPath $coveragePath -PathType Leaf) `
        'Host 本地开发门禁没有生成 Cobertura.xml。'
    [xml]$coverage = Get-Content -Raw -LiteralPath $coveragePath
    $paletteCoverage = Get-FileLineCoverage $coverage `
        'Business/Presentation/Commands/WorkbenchCommandPaletteProjection.cs'
    Assert-True ($paletteCoverage -ge 90.0) `
        "Command Palette 投影行覆盖率 $paletteCoverage% 低于 90%。"

    Assert-ApiFile `
        'Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v3/PublicAPI.Shipped.txt' `
        127 '063BCB5852827612B0501C135D23FECD015069A6F7DDB409547157E4FA00F80F'
    Assert-ApiFile `
        'Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v3/PublicAPI.Unshipped.txt' `
        91 '6805C1C131B7420CE1C7A601A06694B1910FA225D6063B38594D6FAF4D1E05EF'
    Assert-ApiFile `
        'Host/MyAvaloniaManagement.PluginSdk.UI/ApiCompatibility/v3/PublicAPI.Shipped.txt' `
        45 'B11FBE768C3AD04CA65CBF5128BF6FCE8C00058EBB24052D51FE5464A65AD803'
    Assert-ApiFile `
        'Host/MyAvaloniaManagement.PluginSdk.UI/ApiCompatibility/v3/PublicAPI.Unshipped.txt' `
        66 'AACE9EF4878E209FABDB1D49DF7657C7DD38A2D54753C1BD5E560CF0272E1FD8'

    [xml]$versions = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props')
    $properties = $versions.Project.PropertyGroup
    Assert-True (
        [string]$properties.MyAvaloniaPluginSdkVersion -ceq '3.3.0' -and
        [string]$properties.MyAvaloniaProductVersion -ceq '3.0.0') `
        'G9 不得提升 SDK 或 Host 产品版本。'
    Assert-True (
        [string]$properties.MyAvaloniaV2ManifestSchemaVersion -ceq '2' -and
        [string]$properties.MyAvaloniaV2DocumentEnvelopeSchemaVersion -ceq '2' -and
        [string]$properties.MyAvaloniaV2LayoutSchemaVersion -ceq '2' -and
        [string]$properties.MyAvaloniaV2LayoutFileName -ceq 'layout-v2.json' -and
        [string]$properties.MyAvaloniaHostDataRootGeneration -ceq 'v2') `
        'G9 改变了 manifest、Document、layout 或数据根协议。'

    $paletteSource = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot `
        'Host\MyAvaloniaManagement\Business\Presentation\Commands\WorkbenchCommandPaletteProjection.cs')
    foreach ($forbidden in @('IServiceProvider', 'IServiceScope', 'Dock.Model', 'WorkflowAction')) {
        Assert-True (-not $paletteSource.Contains($forbidden, [StringComparison]::Ordinal)) `
            "Palette 投影泄漏了禁止依赖：$forbidden。"
    }
    $hostCatalog = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot `
        'Host\MyAvaloniaManagement\Business\Commands\Catalog\HostWorkbenchCommandCatalog.cs')
    Assert-True (-not $hostCatalog.Contains('CommandPalette', [StringComparison]::Ordinal)) `
        'Palette 打开行为不得伪造成 Catalog Command。'

    Invoke-Checked pwsh @(
        '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-Documentation.ps1'))

    $summary = [ordered]@{
        schemaVersion = 1
        stage = 'WorkbenchCommandG9'
        configuration = $Configuration
        inputCommit = $inputCommit
        baseGateReused = [bool]$ReuseVerifiedBaseGate
        unitTargeted = $unitTargeted
        uiTargeted = $uiTargeted
        baseGateEvidence = [IO.Path]::GetRelativePath($repositoryRoot, $hostSummaryPath).Replace('\', '/')
        hostPassed = $hostPassed
        hostLineCoverage = $hostLineCoverage
        hostBranchCoverage = $hostBranchCoverage
        paletteProjectionLineCoverage = $paletteCoverage
        sdkVersion = [string]$properties.MyAvaloniaPluginSdkVersion
        productVersion = [string]$properties.MyAvaloniaProductVersion
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
        "Workbench Command G9 门禁通过：Unit $($unitTargeted.passed) 项，" +
        "Headless UI $($uiTargeted.passed) 项，Host $($summary.hostPassed) 项，" +
        "Palette 投影覆盖率 $paletteCoverage%。")
}
finally {
    Pop-Location
}
