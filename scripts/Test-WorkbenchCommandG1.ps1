[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\test-results\WorkbenchCommandG1'))
$allowedRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\test-results'))

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )

    if (-not $Condition) { throw $Message }
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

Assert-True ($resultRoot.StartsWith(
        $allowedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) `
    'G1 结果目录越过 artifacts/test-results 边界。'

if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null

Push-Location $repositoryRoot
try {
    # G1 复用已经签署的 V4 G7 开发门禁，取得 Host 三层、SDK/API、四插件、
    # 覆盖率、真实包消费和文档证据。该入口明确不调用 Windows 或发布类门禁。
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-HostV4DevelopmentGate.ps1') `
        -Stage G7 -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Workbench Command G1 聚合开发门禁失败。' }

    $hostSummaryPath = Join-Path $repositoryRoot `
        'artifacts\test-results\HostV4\G7\summary.json'
    Assert-True (Test-Path -LiteralPath $hostSummaryPath -PathType Leaf) `
        'Host V4 G7 没有生成 summary.json。'
    $hostSummary = Get-Content -Raw -LiteralPath $hostSummaryPath | ConvertFrom-Json
    Assert-True ([bool]$hostSummary.passed) 'Host V4 G7 摘要不是通过状态。'
    Assert-True ([double]$hostSummary.hostLineCoverage -ge 85.45) `
        'Host 行覆盖率低于 Workbench Command G0 的 85.45%。'
    Assert-True ([double]$hostSummary.hostBranchCoverage -ge 71.14) `
        'Host 分支覆盖率低于 Workbench Command G0 的 71.14%。'

    [xml]$versions = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot 'Directory.Version.props')
    $properties = $versions.Project.PropertyGroup
    Assert-True ([string]$properties.MyAvaloniaPluginSdkVersion -ceq '3.2.0') `
        'G1 不得提前提升 Core/UI SDK 版本。'
    Assert-True ([string]$properties.MyAvaloniaProductVersion -ceq '3.0.0') `
        'G1 不得提升 Host 产品版本。'
    Assert-True (
        [string]$properties.MyAvaloniaV2ManifestSchemaVersion -ceq '2' -and
        [string]$properties.MyAvaloniaV2DocumentEnvelopeSchemaVersion -ceq '2' -and
        [string]$properties.MyAvaloniaV2LayoutSchemaVersion -ceq '2' -and
        [string]$properties.MyAvaloniaV2LayoutFileName -ceq 'layout-v2.json' -and
        [string]$properties.MyAvaloniaHostDataRootGeneration -ceq 'v2') `
        'G1 改变了 manifest、Document、layout 或数据根协议。'

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
        $api.uiShipped.entries -eq 45 -and
        $api.uiShipped.sha256 -ceq `
            'B11FBE768C3AD04CA65CBF5128BF6FCE8C00058EBB24052D51FE5464A65AD803') `
        'UI v3 Shipped 被改写。'
    Assert-True (
        $api.coreUnshipped.entries -eq 91 -and
        $api.uiUnshipped.entries -eq 66) `
        'Workbench Command G1 的 Core/UI Unshipped 条目数不正确。'

    $pluginPassed = 0
    foreach ($plugin in $hostSummary.plugins.PSObject.Properties) {
        Assert-True ([int]$plugin.Value.failed -eq 0) "$($plugin.Name) 存在失败测试。"
        Assert-True ([int]$plugin.Value.skipped -eq 0) "$($plugin.Name) 存在跳过测试。"
        $pluginPassed += [int]$plugin.Value.passed
    }

    # 聚合入口的原始 TRX/覆盖率由既有 Host 与四插件门禁生成。G1 再按来源复制一份证据快照，
    # 使专项目录自身即可审计“摘要中的数字来自哪些真实结果”，而不是只留下二次汇总 JSON。
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
            $destinationDirectory = Split-Path -Parent $destination
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        }
    }
    $evidenceTrxCount = @(Get-ChildItem -LiteralPath $evidenceRoot -Recurse -Filter '*.trx').Count
    $evidenceCoverageCount = @(Get-ChildItem -LiteralPath $evidenceRoot -Recurse -File |
        Where-Object { $_.Name -match '^(coverage.*\.xml|Cobertura\.xml)$' }).Count
    Assert-True ($evidenceTrxCount -ge 27) 'G1 专项目录没有收集完整的 Host/四插件 TRX。'
    Assert-True ($evidenceCoverageCount -gt 0) 'G1 专项目录没有收集真实覆盖率文件。'

    $nonReleaseFacts = [ordered]@{
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        published = $false
        uploaded = $false
        tagCreated = $false
    }
    Assert-True (@($nonReleaseFacts.GetEnumerator() | Where-Object Value).Count -eq 0) `
        'G1 非发布事实必须全部为 false。'

    $summary = [ordered]@{
        schemaVersion = 1
        stage = 'G1'
        configuration = $Configuration
        inputCommit = '9aa5c89'
        hostPassed = [int]$hostSummary.hostPassed
        pluginPassed = $pluginPassed
        hostLineCoverage = [double]$hostSummary.hostLineCoverage
        hostBranchCoverage = [double]$hostSummary.hostBranchCoverage
        api = $api
        sdkVersion = [string]$properties.MyAvaloniaPluginSdkVersion
        productVersion = [string]$properties.MyAvaloniaProductVersion
        manifestSchema = [int]$properties.MyAvaloniaV2ManifestSchemaVersion
        documentEnvelopeSchema = [int]$properties.MyAvaloniaV2DocumentEnvelopeSchemaVersion
        layoutSchema = [int]$properties.MyAvaloniaV2LayoutSchemaVersion
        layoutFile = [string]$properties.MyAvaloniaV2LayoutFileName
        dataRoot = [string]$properties.MyAvaloniaHostDataRootGeneration
        evidenceTrxFiles = $evidenceTrxCount
        evidenceCoverageFiles = $evidenceCoverageCount
        passed = $true
        aiflow = $nonReleaseFacts.aiflow
        windowsCi = $nonReleaseFacts.windowsCi
        windowsSmoke = $nonReleaseFacts.windowsSmoke
        releaseAcceptance = $nonReleaseFacts.releaseAcceptance
        releaseGate = $nonReleaseFacts.releaseGate
        publishable = $nonReleaseFacts.publishable
        published = $nonReleaseFacts.published
        uploaded = $nonReleaseFacts.uploaded
        tagCreated = $nonReleaseFacts.tagCreated
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))

    Write-Host (
        "Workbench Command G1 通过：Host $($summary.hostPassed) 项，" +
        "四插件聚合 $pluginPassed 项，覆盖率 " +
        "$($summary.hostLineCoverage)% / $($summary.hostBranchCoverage)%。")
}
finally {
    Pop-Location
}
