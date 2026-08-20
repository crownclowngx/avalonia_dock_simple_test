param(
    [switch]$AllowDirty,
    [switch]$SkipLiveAcceptance,
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$requiredDotnetMajor = 10
$targetFramework = 'net10.0'
$runtimeIdentifier = 'win-x64'

# 本脚本是 BiliDownloader P0 的唯一发布编排入口。它只负责环境约束、命令顺序、
# 路径安全、打包与证据汇总；真实下载、敏感扫描和包内容规则由可独立测试的
# ReleaseAcceptance 项目实现，避免 PowerShell 与生产业务逻辑形成两套事实。
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pluginProject = Join-Path $workspace 'Plugins\BiliDownloader\BiliDownloader\BiliDownloader.csproj'
$testProject = Join-Path $workspace 'Plugins\BiliDownloader\BiliDownloader.Tests\BiliDownloader.Tests.csproj'
$hostPluginTestProject = Join-Path $workspace 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
$acceptanceProject = Join-Path $workspace 'Plugins\BiliDownloader\BiliDownloader.ReleaseAcceptance\BiliDownloader.ReleaseAcceptance.csproj'
$solution = Join-Path $workspace 'MyAvaloniaManagement.sln'
$deployedPlugin = Join-Path $workspace "Host\MyAvaloniaManagement\bin\Release\$targetFramework\Controls\BiliDownloader"
$artifactRoot = Join-Path $workspace 'artifacts\BiliDownloader\p0-win-x64'
$artifactParent = Join-Path $workspace 'artifacts\BiliDownloader'
$stageRoot = Join-Path $artifactRoot '.staging'
$validationRoot = Join-Path $artifactRoot '.validation'
$liveSandbox = Join-Path $artifactRoot '.live'
$reportRoot = Join-Path $artifactRoot 'reports'
$pluginTestResults = Join-Path $reportRoot 'plugin-tests'
$solutionTestResults = Join-Path $reportRoot 'solution-tests'

function Invoke-Gate {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$ArgumentList,
        [hashtable]$Environment
    )

    Write-Host "`n[G8] $Name"
    $previous = @{}
    if ($Environment) {
        foreach ($entry in $Environment.GetEnumerator()) {
            $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key)
            [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value)
        }
    }

    try {
        & $FilePath @ArgumentList
        if ($LASTEXITCODE -ne 0) {
            throw "$Name 失败，退出码 $LASTEXITCODE。"
        }
    }
    finally {
        # 临时插件目录等信息只在子进程调用期间存在，避免污染用户后续的终端会话。
        foreach ($entry in $previous.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
        }
    }
}

function Assert-PathUnder {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$AllowedRoot
    )

    # 所有递归清理和证据写入先比较规范化绝对路径。变量为空或路径拼接错误时必须
    # 安全失败，绝不允许把工作区根目录或用户目录当成发布临时目录。
    $full = [IO.Path]::GetFullPath($Path)
    $allowed = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\')
    if (-not $full.StartsWith($allowed + '\', [StringComparison]::OrdinalIgnoreCase) `
        -and -not $full.Equals($allowed, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝操作允许目录之外的路径：$full"
    }
}

function Write-JsonUtf8 {
    param([Parameter(Mandatory)] [string]$Path, [Parameter(Mandatory)] $Value)

    $json = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
}

function Get-TrxCounts {
    param([Parameter(Mandatory)] [string]$Directory)

    $totals = [ordered]@{ total = 0; passed = 0; failed = 0; skipped = 0 }
    $files = @(Get-ChildItem -LiteralPath $Directory -Filter '*.trx' -File -Recurse)
    if ($files.Count -eq 0) {
        throw "测试成功退出但未在 $Directory 生成 TRX 证据。"
    }

    foreach ($file in $files) {
        [xml]$document = Get-Content -LiteralPath $file.FullName -Raw
        $counters = $document.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
        if ($null -eq $counters) {
            throw "TRX 缺少结果计数：$($file.FullName)"
        }

        $totals.total += [int]$counters.total
        $totals.passed += [int]$counters.passed
        $totals.failed += [int]$counters.failed
        # VSTest 用 notExecuted 表示跳过；脚本把它作为独立发布失败条件，而不只依赖退出码。
        $totals.skipped += [int]$counters.notExecuted
    }

    if ($totals.failed -ne 0 -or $totals.skipped -ne 0 -or $totals.total -ne $totals.passed) {
        throw "测试证据不满足 0 失败、0 跳过：$($totals | ConvertTo-Json -Compress)"
    }
    return $totals
}

if ($env:OS -ne 'Windows_NT' -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'G8 Windows 插件发布只允许在 Windows x64 进程中执行。'
}

$dotnetVersion = (& dotnet --version).Trim()
if (-not $dotnetVersion.StartsWith("$requiredDotnetMajor.")) {
    throw "需要 .NET $requiredDotnetMajor SDK，当前版本为 $dotnetVersion。"
}

# 立即把临时凭据从父 PowerShell 的进程环境移除。后续 build/test 子进程不应继承 Cookie；
# 只有 live 与最终 scan 两个明确门禁会通过 Invoke-Gate 的临时环境重新获得该值。
$liveBvid = $env:BILIDOWNLOADER_G8_TEST_BVID
$liveCookie = $env:BILIDOWNLOADER_G8_COOKIE
[Environment]::SetEnvironmentVariable('BILIDOWNLOADER_G8_TEST_BVID', $null)
[Environment]::SetEnvironmentVariable('BILIDOWNLOADER_G8_COOKIE', $null)

$revision = (& git -C $workspace rev-parse --short=12 HEAD).Trim()
$dirtyLines = @(& git -C $workspace status --porcelain)
$isDirty = $dirtyLines.Count -gt 0
if ($isDirty -and -not $AllowDirty) {
    throw '正式发布拒绝 dirty worktree；本地验证请显式使用 -AllowDirty。'
}

if (-not $SkipLiveAcceptance) {
    if ([string]::IsNullOrWhiteSpace($liveBvid) -or
        [string]::IsNullOrWhiteSpace($liveCookie)) {
        throw '正式发布必须通过环境变量提供 BILIDOWNLOADER_G8_TEST_BVID 与 BILIDOWNLOADER_G8_COOKIE。'
    }
}

Assert-PathUnder $artifactRoot $artifactParent
if (Test-Path $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stageRoot, $validationRoot, $reportRoot, $pluginTestResults, $solutionTestResults -Force | Out-Null

Invoke-Gate 'BiliDownloader Release 构建（警告即失败）' 'dotnet' @(
    'build', $pluginProject, '-c', 'Release', '-warnaserror', '-p:SkipPluginDeploy=false'
)
Invoke-Gate 'ReleaseAcceptance 构建（警告即失败）' 'dotnet' @(
    'build', $acceptanceProject, '-c', 'Release', '-warnaserror', '-p:SkipPluginDeploy=true'
)
Invoke-Gate 'BiliDownloader 测试项目构建（警告即失败）' 'dotnet' @(
    'build', $testProject, '-c', 'Release', '-warnaserror', '-p:SkipPluginDeploy=true'
)
Invoke-Gate 'BiliDownloader 自动化测试' 'dotnet' @(
    'test', $testProject, '-c', 'Release', '--no-build', '--no-restore', '--nologo',
    '--logger', 'trx;LogFilePrefix=bilidownloader', '--results-directory', $pluginTestResults
)
$pluginCounts = Get-TrxCounts $pluginTestResults

Invoke-Gate '全解决方案 Release 构建（警告即失败）' 'dotnet' @(
    'build', $solution, '-c', 'Release', '--no-restore', '-warnaserror'
)
Invoke-Gate '全解决方案自动化测试' 'dotnet' @(
    'test', $solution, '-c', 'Release', '--no-build', '--no-restore', '--nologo',
    '--logger', 'trx;LogFilePrefix=solution', '--results-directory', $solutionTestResults
)
$solutionCounts = Get-TrxCounts $solutionTestResults
Invoke-Gate 'Git 空白错误检查' 'git' @('-C', $workspace, 'diff', '--check')

if (-not (Test-Path $deployedPlugin)) {
    throw "构建后未找到插件部署目录：$deployedPlugin"
}

# 插件根目录只接收插件自身及其私有托管依赖。显式拒绝宿主共享程序集，既是包边界，
# 也是 PluginLoadContext 身份一致性的前置条件；SQLite 原生资产则只选择 win-x64。
$forbiddenSharedAssembly = '^(?:Avalonia(?:\.|$)|MyAvaloniaManagementCommon\.dll$)'
Get-ChildItem -LiteralPath $deployedPlugin -File | ForEach-Object {
    if ($_.Name -match $forbiddenSharedAssembly) {
        throw "部署目录意外包含宿主共享程序集：$($_.Name)"
    }
    Copy-Item -LiteralPath $_.FullName -Destination $stageRoot
}

$winRuntime = Join-Path $deployedPlugin 'runtimes\win-x64'
if (-not (Test-Path $winRuntime)) {
    throw '部署目录缺少 runtimes/win-x64 SQLite 原生资产。'
}
$winRuntimeTarget = Join-Path $stageRoot 'runtimes\win-x64'
New-Item -ItemType Directory -Path $winRuntimeTarget -Force | Out-Null
Copy-Item -Path (Join-Path $winRuntime '*') -Destination $winRuntimeTarget -Recurse -Force

Invoke-Gate '宿主按候选目录加载插件' 'dotnet' @(
    'test', $hostPluginTestProject, '-c', 'Release', '--no-build', '--no-restore', '--nologo',
    '--filter', 'FullyQualifiedName~BiliDownloaderReleasePackageTests'
) @{ BILIDOWNLOADER_G8_PLUGIN_ROOT = $stageRoot }

$liveReport = Join-Path $reportRoot 'g8-live.json'
if (-not $SkipLiveAcceptance) {
    Invoke-Gate '真实 Bilibili、ffmpeg 与 Range 恢复验收' 'dotnet' @(
        'run', '--project', $acceptanceProject, '-c', 'Release', '--no-build', '--',
        'live', '--sandbox', $liveSandbox, '--report', $liveReport
    ) @{
        BILIDOWNLOADER_G8_TEST_BVID = $liveBvid
        BILIDOWNLOADER_G8_COOKIE = $liveCookie
    }
}
else {
    Write-JsonUtf8 $liveReport ([ordered]@{
        schemaVersion = 1
        passed = $false
        skipped = $true
        summary = '本地候选显式跳过真实网络与 ffmpeg 门禁，不具备发布资格。'
    })
}

# 宽松开关本身就是“本地候选”声明；即使调用时工作树碰巧干净，使用 -AllowDirty
# 也不能得到可发布标志，避免同一命令在不同机器上产生语义不同的正式结果。
$publishable = (-not $AllowDirty) -and (-not $isDirty) -and (-not $SkipLiveAcceptance)

# G12 以后，专项入口不再复制通用暂存、ZIP 和哈希算法。统一打包脚本以单个项目为
# 输入并只生成该插件的 Controls/BiliDownloader/；G8 仍只负责联网、敏感信息与业务探针。
& (Join-Path $PSScriptRoot 'Build-ManagedPluginPackage.ps1') `
    -Project $pluginProject `
    -Configuration Release `
    -OutputDirectory $artifactRoot
if ($LASTEXITCODE -ne 0) {
    throw "G12 统一插件打包失败，退出码 $LASTEXITCODE。"
}

[xml]$pluginProjectXml = Get-Content -Raw -LiteralPath $pluginProject
$pluginVersion = ([string]$pluginProjectXml.Project.PropertyGroup.PluginVersion).Trim()
$baseName = "BiliDownloader-$pluginVersion-$runtimeIdentifier"
$zipPath = Join-Path $artifactRoot "$baseName.zip"
$sidecarManifest = Join-Path $artifactRoot "$baseName.manifest.json"

$packageReport = Join-Path $reportRoot 'g8-package.json'
Invoke-Gate 'ZIP 封闭清单与摘要复验' 'dotnet' @(
    'run', '--project', $acceptanceProject, '-c', 'Release', '--no-build', '--',
    'verify-package', '--package', $zipPath, '--manifest', $sidecarManifest,
    '--sandbox', $validationRoot, '--report', $packageReport
)

$scanReport = Join-Path $reportRoot 'g8-sensitive-scan.json'
Invoke-Gate '发布目录敏感信息扫描' 'dotnet' @(
    'run', '--project', $acceptanceProject, '-c', 'Release', '--no-build', '--',
    'scan', '--root', $artifactRoot, '--sandbox', $validationRoot, '--report', $scanReport
) $(if ([string]::IsNullOrWhiteSpace($liveCookie)) { @{} } else {
    @{ BILIDOWNLOADER_G8_COOKIE = $liveCookie }
})

$acceptance = [ordered]@{
    schemaVersion = 1
    pluginId = 'BiliDownloader'
    release = 'p0'
    runtimeIdentifier = $runtimeIdentifier
    sourceRevision = $revision
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    cleanWorktree = -not $isDirty
    liveAcceptance = -not $SkipLiveAcceptance
    publishable = $publishable
    tests = [ordered]@{
        plugin = $pluginCounts
        solution = $solutionCounts
    }
    gates = [ordered]@{
        releaseBuild = 'passed'
        pluginTests = 'passed'
        solutionBuild = 'passed'
        solutionTests = 'passed'
        hostPluginLoad = 'passed'
        live = if ($SkipLiveAcceptance) { 'skipped' } else { 'passed' }
        packageIntegrity = 'passed'
        sensitiveScan = 'passed'
        gitDiffCheck = 'passed'
    }
    artifact = [ordered]@{
        file = [IO.Path]::GetFileName($zipPath)
        length = (Get-Item -LiteralPath $zipPath).Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash
    }
}
$acceptancePath = Join-Path $artifactRoot "$baseName.acceptance.json"
Write-JsonUtf8 $acceptancePath $acceptance

if (-not [string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $resolvedEvidence = if ([IO.Path]::IsPathRooted($EvidenceRoot)) {
        [IO.Path]::GetFullPath($EvidenceRoot)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $workspace $EvidenceRoot))
    }
    $allowedEvidence = Join-Path $workspace 'TestResults\BiliDownloader\G8'
    Assert-PathUnder $resolvedEvidence $allowedEvidence
    New-Item -ItemType Directory -Path $resolvedEvidence -Force | Out-Null
    Copy-Item -LiteralPath $acceptancePath, $sidecarManifest, $liveReport, $packageReport, $scanReport `
        -Destination $resolvedEvidence -Force
}

Write-Host "`nG8 候选生成完成。"
Write-Host "ZIP: $zipPath"
Write-Host "Acceptance: $acceptancePath"
Write-Host "Publishable: $publishable"
