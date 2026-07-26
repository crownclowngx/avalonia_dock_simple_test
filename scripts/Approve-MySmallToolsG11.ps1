param(
    [Parameter(Mandatory)] [string]$Approver,
    [switch]$ConfirmAllManualChecks
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$requiredDotnetMajor = 10

$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resultRoot = Join-Path $workspace 'TestResults\G11'
$technicalPath = Join-Path $resultRoot 'g11-technical-acceptance.json'
$utf8NoBom = [Text.UTF8Encoding]::new($false)

if (-not $ConfirmAllManualChecks) {
    throw '必须真实完成 G11 手册中的全部人工检查，并显式传入 -ConfirmAllManualChecks。'
}
$approverName = $Approver.Trim()
if ($approverName.Length -lt 2 -or $approverName.Length -gt 100 -or
    $approverName.IndexOfAny([char[]]"`r`n`t") -ge 0) {
    throw '验收人姓名必须为 2～100 个字符，且不能包含控制字符。'
}
if (-not (Test-Path -LiteralPath $technicalPath)) {
    throw '缺少 G11 技术验收报告，请先执行 Accept-MySmallToolsG11.ps1。'
}

$technical = Get-Content -Raw -LiteralPath $technicalPath | ConvertFrom-Json
if (-not ([string]$technical.dotnetSdk).StartsWith("$requiredDotnetMajor.")) {
    throw "G11 技术证据不是由 .NET $requiredDotnetMajor SDK 生成，拒绝签字。"
}
$revision = (& git -C $workspace rev-parse --short=12 HEAD).Trim()
if ([string]$technical.sourceRevision -ne $revision) {
    throw 'G11 技术证据不属于当前源码版本，必须重新执行技术验收。'
}
if (-not [bool]$technical.technicalAcceptancePassed -or
    -not [bool]$technical.formalSignoffReady -or
    -not [bool]$technical.worktreeWasClean) {
    throw '技术报告不是来自 clean worktree 的完整正式验收，拒绝签字。'
}

# 技术验收后只允许新增或更新 G11 证据；任何产品或文档变化都会使报告过期。
$dirtyLines = @(& git -C $workspace status --porcelain)
foreach ($line in $dirtyLines) {
    if ($line.Length -lt 4) {
        throw "无法识别 Git 状态：$line"
    }
    $path = $line.Substring(3).Replace('\', '/')
    if ($path.Contains(' -> ') -or
        ($path -ne 'TestResults/G11' -and
         -not $path.StartsWith('TestResults/G11/', [StringComparison]::Ordinal))) {
        throw "存在 G11 证据目录之外的工作树变化：$path"
    }
}

$approvedUtc = [DateTime]::UtcNow.ToString('O')
$manual = [ordered]@{
    schemaVersion = 1
    kind = 'g11-manual-signoff'
    sourceRevision = $revision
    checklistVersion = 'g11-guide-v1'
    approved = $true
    approver = $approverName
    approvedUtc = $approvedUtc
}
$final = [ordered]@{
    schemaVersion = 1
    kind = 'g11-final-acceptance'
    sourceRevision = $revision
    platform = 'windows-x64'
    technicalAcceptance = 'passed'
    manualSignoff = 'approved'
    finalAcceptancePassed = $true
    approver = $approverName
    approvedUtc = $approvedUtc
    evidenceFiles = @(
        'g11-technical-acceptance.json'
        'g11-manual-signoff.json'
        'g4-acceptance.json'
        'g8-acceptance.json'
        'g10-acceptance.json'
        'g10-sensitive-scan.json'
    )
}

[IO.File]::WriteAllText(
    (Join-Path $resultRoot 'g11-manual-signoff.json'),
    (($manual | ConvertTo-Json -Depth 6) -replace "`r`n", "`n") + "`n",
    $utf8NoBom)
[IO.File]::WriteAllText(
    (Join-Path $resultRoot 'g11-final-acceptance.json'),
    (($final | ConvertTo-Json -Depth 6) -replace "`r`n", "`n") + "`n",
    $utf8NoBom)

Write-Host '[G11] 人工签字已记录，最终验收通过。'
Write-Host "Approver: $approverName"
Write-Host "Revision: $revision"
