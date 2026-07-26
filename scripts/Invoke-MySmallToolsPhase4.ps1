param(
    [string]$EvidenceRoot = '',
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $workspace 'TestResults\Phase4'
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null

if ($env:OS -ne 'Windows_NT' -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw '阶段 4 必须在交互式 Windows x64 桌面执行。'
}
$dotnetVersion = (& dotnet --version).Trim()
if (-not $dotnetVersion.StartsWith('10.')) {
    throw "阶段 4 需要 .NET 10 SDK，当前版本为 $dotnetVersion。"
}
$status = @(& git -C $workspace status --porcelain)
if ($status.Count -gt 0 -and -not $AllowDirty) {
    throw '阶段 4 正式证据必须绑定 clean worktree；排错时才可显式使用 -AllowDirty。'
}

# 包图先写入 gitignore 覆盖的构建目录，避免它先于真实窗口 Harness
# 污染工作区状态；运行时报告落盘后再复制到正式证据目录。
$packageStage = Join-Path $workspace 'artifacts\MySmallTools\phase4\packages'
& (Join-Path $PSScriptRoot 'Test-MySmallToolsUpgradeDependencyGraph.ps1') `
    -EvidenceRoot $packageStage
if ($LASTEXITCODE -ne 0) {
    throw "依赖图闸门失败，退出码 $LASTEXITCODE。"
}

$solution = Join-Path $workspace 'MyAvaloniaManagement.sln'
& dotnet build $solution -c Release --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) {
    throw "Release 严格构建失败，退出码 $LASTEXITCODE。"
}
& dotnet test $solution -c Release --no-build --no-restore `
    --logger 'console;verbosity=minimal'
if ($LASTEXITCODE -ne 0) {
    throw "全解决方案测试失败，退出码 $LASTEXITCODE。"
}

$harness = Join-Path $workspace (
    'Plugins\MySmallTools\MySmallTools.Playback.IntegrationHarness\' +
    'bin\x64\Release\net10.0-windows\win-x64\' +
    'MySmallTools.Playback.IntegrationHarness.dll')
$report = Join-Path $EvidenceRoot 'avalonia12-libvlcsharp.json'
& dotnet $harness --suite phase4 --cycles 100 --dock-switches 20 `
    --media-switches 30 --queue-items 100 --library-items 1000 --report $report
if ($LASTEXITCODE -ne 0) {
    throw "阶段 4 运行时闸门失败，退出码 $LASTEXITCODE。"
}

$result = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
if (-not [bool]$result.success -or
    [string]$result.manualSignoff -ne 'pending' -or
    [string]$result.hwnd.descriptor -ne 'HWND' -or
    -not [bool]$result.hwnd.nonZero) {
    throw '阶段 4 报告没有满足自动 GO 条件。'
}
if (-not $AllowDirty -and -not [bool]$result.worktreeClean) {
    throw '阶段 4 报告未绑定 clean commit。'
}

$packageEvidence = Join-Path $EvidenceRoot 'packages'
New-Item -ItemType Directory -Path $packageEvidence -Force | Out-Null
Get-ChildItem -LiteralPath $packageStage | Copy-Item `
    -Destination $packageEvidence -Recurse -Force

Write-Host '[阶段 4] 自动化通过；manualSignoff 仍为 pending，尚未形成 GO。'
Write-Host "Report: $report"
