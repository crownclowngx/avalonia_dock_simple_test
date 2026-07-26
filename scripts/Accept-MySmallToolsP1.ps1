param(
    [switch]$AllowDirty,
    [switch]$SkipWindowGates,
    [int]$QueueItems = 100,
    [int]$LibraryItems = 1000,
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$requiredDotnetMajor = 10

# 两个测试项目共享 MyAvaloniaManagementCommon/obj，必须串行运行，
# 避免把编译器文件锁误报为产品回归。
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pluginProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools\MySmallTools.csproj'
$unitTestProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj'
$hostTestProject = Join-Path $workspace 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
$harnessProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools.Playback.IntegrationHarness\MySmallTools.Playback.IntegrationHarness.csproj'
$artifactRoot = Join-Path $workspace 'artifacts\MySmallTools\p1-acceptance'
$evidenceRoot = if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    Join-Path $workspace 'TestResults\G8'
}
elseif ([IO.Path]::IsPathRooted($EvidenceRoot)) {
    [IO.Path]::GetFullPath($EvidenceRoot)
}
else {
    [IO.Path]::GetFullPath((Join-Path $workspace $EvidenceRoot))
}
$usesCustomEvidenceRoot = -not [string]::IsNullOrWhiteSpace($EvidenceRoot)
$utf8NoBom = [Text.UTF8Encoding]::new($false)

function Assert-SafeArtifactPath {
    param([Parameter(Mandatory)] [string]$Path)

    $full = [IO.Path]::GetFullPath($Path)
    $allowed = [IO.Path]::GetFullPath(
        (Join-Path $workspace 'artifacts\MySmallTools'))
    if (-not $full.StartsWith(
            $allowed + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside the P1 artifact root: $full"
    }
}

function Assert-SafeEvidencePath {
    param([Parameter(Mandatory)] [string]$Path)

    # 自定义证据目录仅供 G11 在 ignored artifacts 下编排阶段门禁，
    # 防止前一阶段写入 TestResults 后把后续阶段误判为 dirty worktree。
    $fullEvidencePath = [IO.Path]::GetFullPath($Path)
    $allowed = [IO.Path]::GetFullPath(
        (Join-Path $workspace 'artifacts\MySmallTools'))
    if ([string]::IsNullOrWhiteSpace($fullEvidencePath) -or
        -not $fullEvidencePath.StartsWith(
            $allowed + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write G8 evidence outside artifacts/MySmallTools: $fullEvidencePath"
    }
}

function Invoke-Gate {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string[]]$ArgumentList
    )

    Write-Host "`n[G8] $Name"
    & dotnet @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Copy-JsonEvidence {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination
    )

    # 可提交 JSON 统一为 UTF-8/LF，避免真实窗口进程的 CRLF 造成整份证据行尾漂移。
    $content = [IO.File]::ReadAllText($Source)
    $normalized = $content.Replace("`r`n", "`n").Replace("`r", "`n")
    [IO.File]::WriteAllText(
        $Destination,
        $normalized.TrimEnd([char[]]"`r`n") + "`n",
        $utf8NoBom)
}

if ($env:OS -ne 'Windows_NT' -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'G8 P1 acceptance requires a Windows x64 process.'
}
if ($QueueItems -le 0 -or $LibraryItems -le 0) {
    throw 'QueueItems and LibraryItems must be positive integers.'
}

$dotnetVersion = (& dotnet --version).Trim()
if (-not $dotnetVersion.StartsWith("$requiredDotnetMajor.")) {
    throw "需要 .NET $requiredDotnetMajor SDK，当前版本为 $dotnetVersion。"
}

$revision = (& git -C $workspace rev-parse --short=12 HEAD).Trim()
$dirtyLines = @(& git -C $workspace status --porcelain)
$wasDirty = $dirtyLines.Count -gt 0
if ($wasDirty -and -not $AllowDirty) {
    throw 'Formal P1 acceptance requires a clean worktree. Use -AllowDirty for local validation.'
}
if ($usesCustomEvidenceRoot) {
    Assert-SafeEvidencePath $evidenceRoot
}

Assert-SafeArtifactPath $artifactRoot
if (Test-Path $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null

Invoke-Gate 'MySmallTools Release build (warnings are errors)' @(
    'build', $pluginProject, '-c', 'Release', '-warnaserror'
)
Invoke-Gate 'IntegrationHarness Release build (warnings are errors)' @(
    'build', $harnessProject, '-c', 'Release', '-warnaserror',
    '-p:SkipPluginDeploy=true'
)
Invoke-Gate 'MySmallTools automated tests' @(
    'test', $unitTestProject, '-c', 'Release', '--no-restore'
)
Invoke-Gate 'Host plugin automated tests' @(
    'test', $hostTestProject, '-c', 'Release', '--no-restore'
)

$windowReports = @()
$runtimeCanaries = [Collections.Generic.List[string]]::new()
if (-not $SkipWindowGates) {
    $g3Report = Join-Path $artifactRoot 'g3-compatibility.json'
    Invoke-Gate 'G3 default-command compatibility gate' @(
        'run', '--project', $harnessProject, '-c', 'Release', '--no-build', '--',
        '--cycles', '1', '--dock-switches', '2', '--media-switches', '2',
        '--report', $g3Report
    )

    for ($run = 1; $run -le 2; $run++) {
        $runCanary = [Guid]::NewGuid().ToString('N')
        $runValues = @(
            "G8-PASSWORD-A-$runCanary!"
            "G8-PASSWORD-B-$runCanary!"
            "G8-QUEUE-A-$runCanary!"
            "G8-QUEUE-B-$runCanary!"
            "G8-PUBLIC-DESCRIPTION-$runCanary"
        )
        foreach ($value in $runValues) {
            $runtimeCanaries.Add($value)
        }

        $report = Join-Path $artifactRoot "g8-window-run$run.json"
        $env:MYSMALLTOOLS_G8_RUN_CANARY = $runCanary
        try {
            Invoke-Gate "G8 real-window composition run $run" @(
                'run', '--project', $harnessProject, '-c', 'Release', '--no-build', '--',
                '--suite', 'g8',
                '--queue-items', $QueueItems,
                '--library-items', $LibraryItems,
                '--report', $report
            )
        }
        finally {
            Remove-Item Env:\MYSMALLTOOLS_G8_RUN_CANARY -ErrorAction SilentlyContinue
        }
        Copy-JsonEvidence $report (
            Join-Path $evidenceRoot "g8-window-run$run.json")
        $windowReports += "g8-window-run$run.json"
    }
}

# Scan persisted reports and user data with the same runtime canaries.
# Only finding counts are emitted so a failed report does not copy sensitive text.
$canaries = @(
    'G8-Player-A-Canary!'
    'G8-Player-B-Canary!'
    'G8-QUEUE-A-CANARY!'
    'G8-QUEUE-B-CANARY!'
    'G8-PUBLIC-DESCRIPTION-CANARY'
    'G8-DERIVED-KEY-CANARY'
    'G8-PLAINTEXT-CANARY'
)
$canaries += $runtimeCanaries
$scanFiles = @(
    Get-ChildItem -LiteralPath $artifactRoot -File -Recurse |
        Where-Object Extension -in '.json', '.log', '.txt', '.md'
)
if (Test-Path $evidenceRoot) {
    $scanFiles += @(
        Get-ChildItem -LiteralPath $evidenceRoot -File -Recurse |
            Where-Object Extension -in '.json', '.log', '.txt', '.md'
    )
}
$userDataPath = Join-Path $env:LOCALAPPDATA (
    'MyAvaloniaManagement\MySmallTools\secret-video-player\user-data-v1.json')
if (Test-Path $userDataPath) {
    $scanFiles += Get-Item -LiteralPath $userDataPath
}

$findings = 0
foreach ($file in $scanFiles | Sort-Object FullName -Unique) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop
    foreach ($canary in $canaries) {
        if ($content.IndexOf($canary, [StringComparison]::Ordinal) -ge 0) {
            $findings++
        }
    }
    if ($file.Extension -eq '.json' -and
        $content -match '"(password|derivedKey|authenticationContext)"\s*:') {
        $findings++
    }
}

$sensitiveReport = [ordered]@{
    schemaVersion = 1
    kind = 'g8-sensitive-scan'
    scannedFileCount = @($scanFiles | Sort-Object FullName -Unique).Count
    findingCount = $findings
    passed = $findings -eq 0
}
$sensitivePath = Join-Path $artifactRoot 'g8-sensitive-scan.json'
[IO.File]::WriteAllText(
    $sensitivePath,
    (($sensitiveReport | ConvertTo-Json -Depth 4) -replace "`r`n", "`n"),
    $utf8NoBom)
Copy-JsonEvidence $sensitivePath (
    Join-Path $evidenceRoot 'g8-sensitive-scan.json')
if ($findings -ne 0) {
    throw "Sensitive-data scan found $findings findings."
}

$technicalPassed = (-not $SkipWindowGates) -and $findings -eq 0
$acceptance = [ordered]@{
    schemaVersion = 1
    kind = 'g8-p1-acceptance'
    sourceRevision = $revision
    platform = 'windows-x64'
    dotnetSdk = $dotnetVersion
    worktreeWasClean = -not $wasDirty
    cleanTechnicalEvidenceReady = $technicalPassed -and (-not $wasDirty)
    technicalAcceptancePassed = $technicalPassed
    # Formal signoff cannot be inferred from automation; a person must complete the checklist.
    formalSignoffReady = $false
    manualSignoff = 'pending'
    queueItems = $QueueItems
    libraryItems = $LibraryItems
    gates = [ordered]@{
        mySmallToolsBuild = 'passed'
        integrationHarnessBuild = 'passed'
        mySmallToolsTests = 'passed'
        hostPluginTests = 'passed'
        g3Compatibility = if ($SkipWindowGates) { 'skipped' } else { 'passed' }
        g8Window = if ($SkipWindowGates) { 'skipped' } else { 'passed-two-runs' }
        sensitiveScan = 'passed'
    }
    windowReports = $windowReports
}
$acceptancePath = Join-Path $artifactRoot 'g8-acceptance.json'
[IO.File]::WriteAllText(
    $acceptancePath,
    (($acceptance | ConvertTo-Json -Depth 8) -replace "`r`n", "`n"),
    $utf8NoBom)
Copy-JsonEvidence $acceptancePath (
    Join-Path $evidenceRoot 'g8-acceptance.json')

Write-Host "`n[G8] P1 technical acceptance passed"
Write-Host "Artifacts: $artifactRoot"
Write-Host "Evidence: $evidenceRoot"
if ($wasDirty) {
    Write-Host 'Evidence was produced from a dirty worktree; rerun without -AllowDirty after commit.'
}
Write-Host 'Manual window checks still require signoff in TestResults/G8/README.md.'
