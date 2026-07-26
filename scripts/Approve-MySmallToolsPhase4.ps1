param(
    [Parameter(Mandatory)] [string]$Approver,
    [switch]$ConfirmPicture,
    [switch]$ConfirmAudio,
    [switch]$ConfirmFullscreen,
    [switch]$ConfirmDockRestore,
    [string]$EvidenceRoot = ''
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $workspace 'TestResults\Phase4'
}
$reportPath = Join-Path $EvidenceRoot 'avalonia12-libvlcsharp.json'
if (-not ($ConfirmPicture -and $ConfirmAudio -and
          $ConfirmFullscreen -and $ConfirmDockRestore)) {
    throw '必须逐项确认画面、声音、全屏和 Dock 恢复。'
}
if (-not (Test-Path -LiteralPath $reportPath)) {
    throw '缺少阶段 4 自动报告。'
}
$report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
$revision = (& git -C $workspace rev-parse HEAD).Trim()
if (-not [bool]$report.success -or
    -not [bool]$report.worktreeClean -or
    [string]$report.sourceRevision -ne $revision) {
    throw '自动报告未通过、不是 clean commit，或与当前源码提交不一致。'
}
# 自动运行后只允许阶段 4 证据发生变化；产品代码变化会使人工观察失去绑定关系。
$relativeEvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
foreach ($line in @(& git -C $workspace status --porcelain)) {
    if ($line.Length -lt 4 -or $line.Substring(0, 2) -eq '??' -and $line.Length -lt 4) {
        throw "无法识别 Git 状态：$line"
    }
    $changedPath = [IO.Path]::GetFullPath(
        (Join-Path $workspace $line.Substring(3).Replace('/', '\')))
    if (-not $changedPath.StartsWith(
            $relativeEvidenceRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "阶段 4 自动运行后存在证据目录以外的变化：$changedPath"
    }
}
$name = $Approver.Trim()
if ($name.Length -lt 2 -or $name.Length -gt 100 -or
    $name.IndexOfAny([char[]]"`r`n`t") -ge 0) {
    throw '验收人姓名必须为 2～100 个字符且不能包含控制字符。'
}

$signoff = [ordered]@{
    schemaVersion = 1
    kind = 'avalonia12-libvlcsharp-manual-signoff'
    sourceRevision = $revision
    automaticGate = 'passed'
    manualSignoff = 'approved'
    decision = 'GO'
    picture = 'confirmed'
    audio = 'confirmed'
    fullscreen = 'confirmed'
    dockRestore = 'confirmed'
    approver = $name
    approvedUtc = [DateTime]::UtcNow.ToString('O')
}
$path = Join-Path $EvidenceRoot 'avalonia12-libvlcsharp-go.json'
[IO.File]::WriteAllText(
    $path,
    (($signoff | ConvertTo-Json -Depth 6) -replace "`r`n", "`n") + "`n",
    [Text.UTF8Encoding]::new($false))
Write-Host "[阶段 4] GO 已记录：$path"
