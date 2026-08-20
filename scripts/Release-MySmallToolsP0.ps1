param(
    [switch]$AllowDirty,
    [switch]$SkipPlaybackGate,
    [int]$SmallMemoryMiB = 64,
    [int]$LargeMemoryMiB = 512,
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$requiredDotnetMajor = 10
$targetFramework = 'net10.0'

# 本脚本是 G4 唯一正式发布入口。刻意串行执行构建和测试，因为两个测试工程都引用
# MyAvaloniaManagementCommon；并行构建会争用同一个 obj 输出，产生与产品无关的 CS2012 文件锁。
# 每一步都使用非零退出码作为门禁，最后才生成可对外分发的验收摘要。
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pluginProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools\MySmallTools.csproj'
$unitTestProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj'
$hostTestProject = Join-Path $workspace 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
$acceptanceProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools.ReleaseAcceptance\MySmallTools.ReleaseAcceptance.csproj'
$playbackProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools.Playback.IntegrationHarness\MySmallTools.Playback.IntegrationHarness.csproj'
$deployedPlugin = Join-Path $workspace "Host\MyAvaloniaManagement\bin\Release\$targetFramework\Controls\SmallTools"
$artifactRoot = Join-Path $workspace 'artifacts\MySmallTools\p0-win-x64'
$validationRoot = Join-Path $artifactRoot '.validation'
$resolvedEvidenceRoot = if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $null
}
elseif ([IO.Path]::IsPathRooted($EvidenceRoot)) {
    [IO.Path]::GetFullPath($EvidenceRoot)
}
else {
    [IO.Path]::GetFullPath((Join-Path $workspace $EvidenceRoot))
}

function Invoke-Gate {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$ArgumentList
    )

    Write-Host "`n[G4] $Name"
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$Name 失败，退出码 $LASTEXITCODE。"
    }
}

function Assert-SafeGeneratedPath {
    param([Parameter(Mandatory)] [string]$Path)

    # 所有递归清理都必须被限制在 artifacts/MySmallTools/p0-win-x64 下。
    # 这里先解析父目录并比较绝对路径，避免变量为空或路径拼接错误时误删工作区。
    $full = [IO.Path]::GetFullPath($Path)
    $allowed = [IO.Path]::GetFullPath($artifactRoot)
    if (-not $full.StartsWith($allowed + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) `
        -and -not $full.Equals($allowed, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理非发布目录：$full"
    }
}

function Assert-SafeEvidencePath {
    param([Parameter(Mandatory)] [string]$Path)

    # G11 只允许把阶段证据重定向到 Git 忽略的 artifacts/MySmallTools 子目录。
    # 默认不传参数时完全保留 G4 原有行为，不额外复制任何证据。
    $full = [IO.Path]::GetFullPath($Path)
    $allowed = [IO.Path]::GetFullPath(
        (Join-Path $workspace 'artifacts\MySmallTools'))
    if (-not $full.StartsWith(
            $allowed + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝把 G4 证据写入 artifacts/MySmallTools 之外：$full"
    }
}

if ($env:OS -ne 'Windows_NT' -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'G4 正式发布只允许在 Windows x64 进程中执行。'
}

$dotnetVersion = (& dotnet --version).Trim()
if (-not $dotnetVersion.StartsWith("$requiredDotnetMajor.")) {
    throw "需要 .NET $requiredDotnetMajor SDK，当前版本为 $dotnetVersion。"
}

$revision = (& git -C $workspace rev-parse --short=12 HEAD).Trim()
$dirtyLines = @(& git -C $workspace status --porcelain)
$isDirty = $dirtyLines.Count -gt 0
if ($isDirty -and -not $AllowDirty) {
    throw '正式发布拒绝 dirty worktree；本地验证请显式使用 -AllowDirty。'
}
if ($resolvedEvidenceRoot) {
    Assert-SafeEvidencePath $resolvedEvidenceRoot
}

Invoke-Gate 'MySmallTools Release 独立构建（警告即失败）' 'dotnet' @(
    'build', $pluginProject, '-c', 'Release', '-warnaserror'
)
Invoke-Gate 'MySmallTools 自动化测试' 'dotnet' @(
    'test', $unitTestProject, '-c', 'Release', '--no-restore'
)
Invoke-Gate '宿主插件自动化测试' 'dotnet' @(
    'test', $hostTestProject, '-c', 'Release', '--no-restore'
)
Invoke-Gate 'ReleaseAcceptance 构建' 'dotnet' @(
    'build', $acceptanceProject, '-c', 'Release', '-warnaserror'
)

if (-not (Test-Path $deployedPlugin)) {
    throw "构建后未找到插件部署目录：$deployedPlugin"
}

Assert-SafeGeneratedPath $artifactRoot
if (Test-Path $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
if ($resolvedEvidenceRoot) {
    New-Item -ItemType Directory -Path $resolvedEvidenceRoot -Force | Out-Null
}

# G12 统一入口负责单插件构建、严格清单、稳定 ZIP 与最终 ZIP 哈希复验。
# 本专项脚本只在通用包通过后继续执行 LibVLC 的生产加载、内存和真实播放门禁。
& (Join-Path $PSScriptRoot 'Build-ManagedPluginPackage.ps1') `
    -Project $pluginProject `
    -Configuration Release `
    -OutputDirectory $artifactRoot
if ($LASTEXITCODE -ne 0) {
    throw "G12 统一插件打包失败，退出码 $LASTEXITCODE。"
}

[xml]$pluginProjectXml = Get-Content -Raw -LiteralPath $pluginProject
$pluginVersion = ([string]$pluginProjectXml.Project.PropertyGroup.PluginVersion).Trim()
$baseName = "MySmallTools-$pluginVersion-win-x64"
$sidecarManifest = Join-Path $artifactRoot "$baseName.manifest.json"
$zipPath = Join-Path $artifactRoot "$baseName.zip"
$utf8NoBom = [Text.UTF8Encoding]::new($false)

# 部署探针必须从最终 ZIP 解压目录运行，不能回退到普通 build 的 Host 部署目录。
# 通用入口已经逐文件验证外置清单；这里解压只为给生产加载探针提供候选根目录。
Add-Type -AssemblyName System.IO.Compression.FileSystem
Assert-SafeGeneratedPath $validationRoot
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
[IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $validationRoot)
$validatedPlugin = Join-Path $validationRoot 'Controls\SmallTools'
if (-not (Test-Path -LiteralPath $validatedPlugin -PathType Container)) {
    throw 'G12 独立 ZIP 缺少 Controls/SmallTools 插件目录。'
}

$probeReport = Join-Path $artifactRoot 'deployment-probe.json'
Invoke-Gate '解压后生产部署探针' 'dotnet' @(
    'run', '--project', $acceptanceProject, '-c', 'Release', '--no-build', '--',
    '--probe', $validatedPlugin, '--report', $probeReport
)

$memoryReport = Join-Path $artifactRoot 'memory-gate.json'
Invoke-Gate '大文件流式内存门禁' 'dotnet' @(
    'run', '--project', $acceptanceProject, '-c', 'Release', '--no-build', '--',
    '--memory', '--small-mib', $SmallMemoryMiB, '--large-mib', $LargeMemoryMiB,
    '--report', $memoryReport
)

$playbackReports = @()
if (-not $SkipPlaybackGate) {
    for ($run = 1; $run -le 2; $run++) {
        $playbackReport = Join-Path $artifactRoot "playback-run$run.json"
        Invoke-Gate "真实播放与 Dock 门禁第 $run 轮" 'dotnet' @(
            'run', '--project', $playbackProject, '-c', 'Release', '--',
            '--report', $playbackReport
        )
        $playbackReports += [IO.Path]::GetFileName($playbackReport)
    }
}

$acceptance = [ordered]@{
    schemaVersion = 1
    pluginId = 'MySmallTools'
    release = 'p0'
    sourceRevision = $revision
    publishable = (-not $isDirty) -and (-not $SkipPlaybackGate) `
        -and $SmallMemoryMiB -eq 64 -and $LargeMemoryMiB -eq 512
    platform = 'windows-x64'
    dotnetSdk = $dotnetVersion
    gates = [ordered]@{
        mySmallToolsBuild = 'passed'
        mySmallToolsTests = 'passed'
        hostPluginTests = 'passed'
        deploymentProbe = 'passed'
        manifestHashes = 'passed'
        memory = 'passed'
        playback = if ($SkipPlaybackGate) { 'skipped' } else { 'passed-two-runs' }
    }
    memoryInputMiB = @($SmallMemoryMiB, $LargeMemoryMiB)
    playbackReports = $playbackReports
    artifact = [ordered]@{
        file = [IO.Path]::GetFileName($zipPath)
        length = (Get-Item -LiteralPath $zipPath).Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash
    }
}
$acceptancePath = Join-Path $artifactRoot "$baseName.acceptance.json"
[IO.File]::WriteAllText(
    $acceptancePath,
    ($acceptance | ConvertTo-Json -Depth 8),
    $utf8NoBom)

if ($resolvedEvidenceRoot) {
    $evidenceFiles = @(
        $sidecarManifest
        $probeReport
        $memoryReport
        $acceptancePath
    )
    foreach ($playbackReportName in $playbackReports) {
        $evidenceFiles += Join-Path $artifactRoot $playbackReportName
    }
    foreach ($evidenceFile in $evidenceFiles) {
        Copy-Item -LiteralPath $evidenceFile -Destination (
            Join-Path $resolvedEvidenceRoot ([IO.Path]::GetFileName($evidenceFile))) -Force
    }
}

Remove-Item -LiteralPath $validationRoot -Recurse -Force

Write-Host "`n[G4] 发布基线通过"
Write-Host "ZIP: $zipPath"
Write-Host "Manifest: $sidecarManifest"
Write-Host "Acceptance: $acceptancePath"
if ($resolvedEvidenceRoot) {
    Write-Host "Evidence: $resolvedEvidenceRoot"
}
