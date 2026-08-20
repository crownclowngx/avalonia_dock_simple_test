param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$documentPath = Join-Path $repositoryRoot 'docs\plan-history\host-v1\g12-unified-plugin-build-and-deployment.md'
$hostSummaryPath = Join-Path $repositoryRoot 'artifacts\test-results\MyAvaloniaManagement\summary.json'
$packageSummaryPath = Join-Path $repositoryRoot 'artifacts\test-results\ManagedPluginPackages\summary.json'
$hostResultsRoot = Join-Path $repositoryRoot 'artifacts\test-results\MyAvaloniaManagement'

foreach ($requiredPath in $documentPath, $hostSummaryPath, $packageSummaryPath) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "更新 G12 文档前缺少证据文件：$requiredPath"
    }
}

$hostEvidence = Get-Content -Raw -LiteralPath $hostSummaryPath | ConvertFrom-Json
$packages = Get-Content -Raw -LiteralPath $packageSummaryPath | ConvertFrom-Json

# 测试数量只从本轮 TRX 读取。脚本不保存预期常量，新增测试不会迫使维护者修改门禁代码。
$suiteRows = foreach ($suite in 'Unit', 'UI', 'Plugin') {
    $trxPath = Join-Path $hostResultsRoot "$suite\$suite.trx"
    if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
        throw "缺少宿主测试 TRX：$trxPath"
    }
    [xml]$trx = Get-Content -Raw -LiteralPath $trxPath
    $counters = $trx.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
    if ($null -eq $counters) {
        throw "TRX 缺少 Counters：$trxPath"
    }
    "| $suite | $($counters.passed) | $($counters.failed) | $($counters.notExecuted) |"
}

$packageRows = foreach ($plugin in $packages.plugins) {
    "| $($plugin.pluginId) | $($plugin.files) | $($plugin.archive.length) | ``$($plugin.archive.sha256)`` |"
}

$generatedUtc = [DateTimeOffset]::Parse($hostEvidence.generatedAtUtc).ToString('yyyy-MM-dd HH:mm:ssZ')
$evidence = @"
<!-- G12_EVIDENCE_BEGIN -->
生成时间：$generatedUtc

| 宿主测试套件 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
$($suiteRows -join "`n")

宿主合计 **$($hostEvidence.passed)** 项；行覆盖率 **$($hostEvidence.lineCoverage)%**，分支覆盖率 **$($hostEvidence.branchCoverage)%**；Windows Smoke：**$($hostEvidence.windowsSmoke)**。

| 独立插件包 | 文件数 | ZIP 字节数 | SHA-256 |
| --- | ---: | ---: | --- |
$($packageRows -join "`n")

包数量 **$($packages.plugins.Count)**；每插件隔离构建 **$($packages.gates.deterministicBuildsPerPlugin)** 次，摘要一致；构建契约负例 **$($packages.gates.contractNegativeCases)** 个；最终 ZIP 宿主加载：**$($packages.gates.finalZipHostLoad)**。
<!-- G12_EVIDENCE_END -->
"@

$document = Get-Content -Raw -LiteralPath $documentPath
$pattern = '(?s)<!-- G12_EVIDENCE_BEGIN -->.*?<!-- G12_EVIDENCE_END -->'
$evidenceRegex = [Text.RegularExpressions.Regex]::new($pattern)
if ($evidenceRegex.Matches($document).Count -ne 1) {
    throw 'G12 文档缺少唯一证据标记，拒绝覆盖其他内容。'
}
$updated = $evidenceRegex.Replace($document, $evidence, 1)
[IO.File]::WriteAllText($documentPath, $updated, [Text.UTF8Encoding]::new($false))
Write-Host "G12 文档证据已从本轮结果更新：$documentPath"
