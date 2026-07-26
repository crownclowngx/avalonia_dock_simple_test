param(
    [switch]$AllowDirty,
    [switch]$SkipPlaybackGate,
    [int]$SmallMemoryMiB = 64,
    [int]$LargeMemoryMiB = 512,
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()

# 本脚本是 G4 唯一正式发布入口。刻意串行执行构建和测试，因为两个测试工程都引用
# MyAvaloniaManagementCommon；并行构建会争用同一个 obj 输出，产生与产品无关的 CS2012 文件锁。
# 每一步都使用非零退出码作为门禁，最后才生成可对外分发的验收摘要。
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pluginProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools\MySmallTools.csproj'
$unitTestProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj'
$hostTestProject = Join-Path $workspace 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
$acceptanceProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools.ReleaseAcceptance\MySmallTools.ReleaseAcceptance.csproj'
$playbackProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools.Playback.IntegrationHarness\MySmallTools.Playback.IntegrationHarness.csproj'
$deployedPlugin = Join-Path $workspace 'Host\MyAvaloniaManagement\bin\Release\net9.0\Controls\SmallTools'
$artifactRoot = Join-Path $workspace 'artifacts\MySmallTools\p0-win-x64'
$stageRoot = Join-Path $artifactRoot '.staging'
$stagedPlugin = Join-Path $stageRoot 'Controls\SmallTools'
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

function Get-StableRelativePath {
    param(
        [Parameter(Mandatory)] [string]$BasePath,
        [Parameter(Mandatory)] [string]$Path
    )

    # Windows PowerShell 5.1 所在的 .NET Framework 没有 Path.GetRelativePath。
    # 使用 Uri 只做路径裁剪，并立即反转义、统一为 ZIP/Manifest 规定的正斜杠。
    $baseFull = [IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $pathFull = [IO.Path]::GetFullPath($Path)
    $baseUri = [Uri]::new($baseFull)
    $pathUri = [Uri]::new($pathFull)
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString())
}

if ($env:OS -ne 'Windows_NT' -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'G4 正式发布只允许在 Windows x64 进程中执行。'
}

$dotnetVersion = (& dotnet --version).Trim()
if (-not $dotnetVersion.StartsWith('9.')) {
    throw "需要 .NET 9 SDK，当前版本为 $dotnetVersion。"
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
New-Item -ItemType Directory -Path $stagedPlugin -Force | Out-Null
if ($resolvedEvidenceRoot) {
    New-Item -ItemType Directory -Path $resolvedEvidenceRoot -Force | Out-Null
}
Copy-Item -Path (Join-Path $deployedPlugin '*') -Destination $stagedPlugin -Recurse -Force

# Manifest 的 files 不包含 Manifest 自身，避免出现“文件哈希依赖自身内容”的递归定义。
# 发布验证要求除 mysmalltools.release.json 外没有额外文件，因此清单仍然是完整且封闭的。
$payloadFiles = Get-ChildItem -LiteralPath $stagedPlugin -File -Recurse |
    Sort-Object { Get-StableRelativePath $stagedPlugin $_.FullName }
$fileEntries = foreach ($file in $payloadFiles) {
    $relative = Get-StableRelativePath $stagedPlugin $file.FullName
    [ordered]@{
        path = $relative
        length = $file.Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash
    }
}

$mySmallToolsAssembly = [Reflection.AssemblyName]::GetAssemblyName(
    (Join-Path $stagedPlugin 'MySmallTools.dll'))
$libVlcSharpAssembly = [Reflection.AssemblyName]::GetAssemblyName(
    (Join-Path $stagedPlugin 'LibVLCSharp.dll'))
$libVlcFileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo(
    (Join-Path $stagedPlugin 'native\win-x64\libvlc\libvlc.dll')).FileVersion

$manifest = [ordered]@{
    schemaVersion = 1
    pluginId = 'MySmallTools'
    release = 'p0'
    targetFramework = 'net9.0'
    runtimeIdentifier = 'win-x64'
    sourceRevision = $revision
    publishable = -not $isDirty
    versions = [ordered]@{
        mySmallTools = $mySmallToolsAssembly.Version.ToString()
        libVLCSharp = $libVlcSharpAssembly.Version.ToString()
        libVLC = $libVlcFileVersion
    }
    files = @($fileEntries)
}
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$manifestJson = $manifest | ConvertTo-Json -Depth 8
$internalManifest = Join-Path $stagedPlugin 'mysmalltools.release.json'
[IO.File]::WriteAllText($internalManifest, $manifestJson, $utf8NoBom)

$baseName = "MySmallTools-p0-win-x64-$revision"
$sidecarManifest = Join-Path $artifactRoot "$baseName.manifest.json"
[IO.File]::WriteAllText($sidecarManifest, $manifestJson, $utf8NoBom)
$zipPath = Join-Path $artifactRoot "$baseName.zip"

# Compress-Archive 会继承本机文件时间，导致相同输入产生不同 ZIP。
# 这里直接使用 ZipArchive，按稳定相对路径排序，并把时间统一为 ZIP 可表示的最早安全日期。
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipStream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
try {
    $archive = [IO.Compression.ZipArchive]::new(
        $zipStream,
        [IO.Compression.ZipArchiveMode]::Create,
        $false)
    try {
        $zipFiles = Get-ChildItem -LiteralPath $stageRoot -File -Recurse |
            Sort-Object { Get-StableRelativePath $stageRoot $_.FullName }
        foreach ($file in $zipFiles) {
            $relative = Get-StableRelativePath $stageRoot $file.FullName
            $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $entryStream = $entry.Open()
            try {
                $input = [IO.File]::OpenRead($file.FullName)
                try { $input.CopyTo($entryStream) } finally { $input.Dispose() }
            } finally {
                $entryStream.Dispose()
            }
        }
    } finally {
        $archive.Dispose()
    }
} finally {
    $zipStream.Dispose()
}

# 不能只相信打包前目录：必须从最终 ZIP 解压并重新验证哈希和部署探针，
# 这样才能覆盖漏打文件、相对路径错误和 ZIP 写入损坏。
Assert-SafeGeneratedPath $validationRoot
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
[IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $validationRoot)
$validatedPlugin = Join-Path $validationRoot 'Controls\SmallTools'
$validatedManifestPath = Join-Path $validatedPlugin 'mysmalltools.release.json'
$validatedManifest = Get-Content -Raw -LiteralPath $validatedManifestPath | ConvertFrom-Json
foreach ($entry in $validatedManifest.files) {
    $path = Join-Path $validatedPlugin $entry.path
    if (-not (Test-Path $path)) {
        throw "ZIP 缺少清单文件：$($entry.path)"
    }
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    if ($actualHash -ne $entry.sha256) {
        throw "ZIP 文件哈希不匹配：$($entry.path)"
    }
}
$actualPayload = @(Get-ChildItem -LiteralPath $validatedPlugin -File -Recurse |
    Where-Object Name -ne 'mysmalltools.release.json')
if ($actualPayload.Count -ne $validatedManifest.files.Count) {
    throw 'ZIP 存在未纳入 Manifest 的额外文件。'
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

Remove-Item -LiteralPath $stageRoot -Recurse -Force
Remove-Item -LiteralPath $validationRoot -Recurse -Force

Write-Host "`n[G4] 发布基线通过"
Write-Host "ZIP: $zipPath"
Write-Host "Manifest: $sidecarManifest"
Write-Host "Acceptance: $acceptancePath"
if ($resolvedEvidenceRoot) {
    Write-Host "Evidence: $resolvedEvidenceRoot"
}
