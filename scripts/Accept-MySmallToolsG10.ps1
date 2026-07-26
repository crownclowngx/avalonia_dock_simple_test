param(
    [switch]$AllowDirty,
    [switch]$SkipWindowGates,
    [switch]$AllowNonComparable,
    [switch]$UpdateBaseline,
    [int]$Runs = 3,
    [int]$SmallMiB = 64,
    [int]$LargeMiB = 512,
    [int]$LibrarySmall = 100,
    [int]$LibraryLarge = 1000,
    [int]$StormEvents = 256,
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$requiredDotnetMajor = 10

$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pluginProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools\MySmallTools.csproj'
$benchmarkProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools.SecurityBenchmarks\MySmallTools.SecurityBenchmarks.csproj'
$harnessProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools.Playback.IntegrationHarness\MySmallTools.Playback.IntegrationHarness.csproj'
$unitTestProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj'
$hostTestProject = Join-Path $workspace 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
$artifactRoot = Join-Path $workspace 'artifacts\MySmallTools\g10'
$evidenceRoot = if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    Join-Path $workspace 'TestResults\G10'
}
elseif ([IO.Path]::IsPathRooted($EvidenceRoot)) {
    [IO.Path]::GetFullPath($EvidenceRoot)
}
else {
    [IO.Path]::GetFullPath((Join-Path $workspace $EvidenceRoot))
}
$usesCustomEvidenceRoot = -not [string]::IsNullOrWhiteSpace($EvidenceRoot)
$baselinePath = Join-Path $workspace (
    'Plugins\MySmallTools\MySmallTools\docs\secret-video-player\benchmarks\' +
    'g10-windows-x64-net10-avalonia12-dock12-reference.json')
$utf8NoBom = [Text.UTF8Encoding]::new($false)

function Assert-SafeArtifactPath {
    param([Parameter(Mandatory)] [string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    $allowed = [IO.Path]::GetFullPath(
        (Join-Path $workspace 'artifacts\MySmallTools'))
    if (-not $full.StartsWith(
            $allowed + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean outside artifacts/MySmallTools: $full"
    }
}

function Assert-SafeEvidencePath {
    param([Parameter(Mandatory)] [string]$Path)
    $fullEvidencePath = [IO.Path]::GetFullPath($Path)
    $allowed = [IO.Path]::GetFullPath(
        (Join-Path $workspace 'artifacts\MySmallTools'))
    if ([string]::IsNullOrWhiteSpace($fullEvidencePath) -or
        -not $fullEvidencePath.StartsWith(
            $allowed + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write G10 evidence outside artifacts/MySmallTools: $fullEvidencePath"
    }
}

function Invoke-Gate {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string[]]$Arguments
    )
    Write-Host "`n[G10] $Name"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Copy-JsonEvidence {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination
    )

    # Harness 可能按 Windows CRLF 写报告；提交证据统一使用 UTF-8/LF，
    # 使数值变化可审查，而不是被整份行尾差异淹没。
    $content = [IO.File]::ReadAllText($Source)
    $normalized = $content.Replace("`r`n", "`n").Replace("`r", "`n")
    [IO.File]::WriteAllText(
        $Destination,
        $normalized.TrimEnd([char[]]"`r`n") + "`n",
        $utf8NoBom)
}

function Get-Delta {
    param($Final, $Baseline, [string]$Property)
    return [Math]::Max(0L, [long]$Final.$Property - [long]$Baseline.$Property)
}

function Assert-WindowReport {
    param(
        [Parameter(Mandatory)] $Report,
        [Parameter(Mandatory)] [string]$Name
    )
    if (-not $Report.success -or $Report.failures.Count -ne 0) {
        throw "$Name reported a functional or heartbeat failure."
    }
    foreach ($resource in $Report.finalResources.PSObject.Properties) {
        if ([long]$resource.Value -ne 0) {
            throw "$Name retained playback resource $($resource.Name)."
        }
    }
    if ([long]$Report.aliveClosedDocuments -ne 0 -or
        [long]$Report.aliveClosedViews -ne 0 -or
        [long]$Report.aliveDisposedEncryptedStreams -ne 0) {
        throw "$Name retained a closed Document, View, or encrypted stream."
    }
    if ([long]$Report.stageDurationsMs.stopUiHeartbeatTicks -le 0 -or
        [long]$Report.stageDurationsMs.mediaSwitchUiHeartbeatTicks -le 0) {
        throw "$Name did not advance the UI heartbeat."
    }
    if ([long]$Report.nativeOutputGeneration -le 0 -or
        [long]$Report.surfaceGenerationBeforeMediaSwitch -ne
        [long]$Report.surfaceGenerationAfterMediaSwitch) {
        throw "$Name replaced the document output or surface during media switching."
    }
    # 与阶段 4 使用同一发布硬门槛，避免趋势比较掩盖单次运行的绝对泄漏。
    if ((Get-Delta $Report.processFinal $Report.processBaseline 'HandleCount') -gt 10 -or
        (Get-Delta $Report.processFinal $Report.processBaseline 'PrivateBytes') -gt
        (64L * 1024 * 1024)) {
        throw "$Name exceeded the Phase 4 handle or private-memory hard gate."
    }
}

if ($env:OS -ne 'Windows_NT' -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'G10 acceptance requires a Windows x64 process.'
}
if ($Runs -lt 3) {
    throw '正式 G10 候选基线至少需要三轮同机测量。'
}
if ($SmallMiB -lt 8 -or $LargeMiB -le $SmallMiB -or
    $LibrarySmall -le 0 -or $LibraryLarge -le $LibrarySmall -or
    $StormEvents -le 0) {
    throw 'Invalid G10 scale parameters.'
}

$dotnetVersion = (& dotnet --version).Trim()
if (-not $dotnetVersion.StartsWith("$requiredDotnetMajor.")) {
    throw "需要 .NET $requiredDotnetMajor SDK，当前版本为 $dotnetVersion。"
}

$revision = (& git -C $workspace rev-parse --short=12 HEAD).Trim()
$dirtyLines = @(& git -C $workspace status --porcelain)
$wasDirty = $dirtyLines.Count -gt 0
if ($wasDirty -and -not $AllowDirty) {
    throw 'Formal G10 acceptance requires a clean worktree; use -AllowDirty locally.'
}
if ($UpdateBaseline -and ($wasDirty -or $AllowDirty)) {
    throw '-UpdateBaseline 只允许在 clean worktree 上提升三轮正式候选。'
}
$formalScale = $SmallMiB -eq 64 -and $LargeMiB -eq 512 -and
    $LibrarySmall -eq 100 -and $LibraryLarge -eq 1000 -and
    $StormEvents -eq 256
if ($UpdateBaseline -and (-not $formalScale -or $SkipWindowGates)) {
    throw '-UpdateBaseline requires the default full scale and all window gates.'
}
if ($usesCustomEvidenceRoot) {
    # 正式 G11 运行期间只写 ignored artifacts，确保三个阶段共享同一 clean 源码快照。
    Assert-SafeEvidencePath $evidenceRoot
}

Assert-SafeArtifactPath $artifactRoot
if (Test-Path $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null

# 测试项目共享 MyAvaloniaManagementCommon/obj，因此门禁保持串行执行。
Invoke-Gate 'MySmallTools Release build (warnings are errors)' @(
    'build', $pluginProject, '-c', 'Release', '-warnaserror'
)
Invoke-Gate 'G10 benchmark Release build (warnings are errors)' @(
    'build', $benchmarkProject, '-c', 'Release', '-warnaserror',
    '-p:SkipPluginDeploy=true'
)
Invoke-Gate 'Window harness Release build (warnings are errors)' @(
    'build', $harnessProject, '-c', 'Release', '-warnaserror',
    '-p:SkipPluginDeploy=true'
)
Invoke-Gate 'MySmallTools full automated tests' @(
    'test', $unitTestProject, '-c', 'Release', '--no-restore'
)
Invoke-Gate 'Host plugin full automated tests' @(
    'test', $hostTestProject, '-c', 'Release', '--no-restore'
)

$performanceReports = @()
$windowReports = @()
$diagnosticReports = @()
for ($run = 1; $run -le $Runs; $run++) {
    $performance = Join-Path $artifactRoot "g10-performance-run$run.json"
    Invoke-Gate "Performance baseline run $run" @(
        'run', '--project', $benchmarkProject, '-c', 'Release', '--no-build', '--',
        '--suite', 'g10',
        '--small-mib', $SmallMiB,
        '--large-mib', $LargeMiB,
        '--library-small', $LibrarySmall,
        '--library-large', $LibraryLarge,
        '--storm-events', $StormEvents,
        '--output', $performance
    )
    $performanceReports += $performance

    if (-not $SkipWindowGates) {
        $short = Join-Path $artifactRoot "g10-playback-short-run$run.json"
        Invoke-Gate "Window short trend run $run" @(
            'run', '--project', $harnessProject, '-c', 'Release', '--no-build', '--',
            '--suite', 'g10',
            '--cycles', '20',
            '--dock-switches', '10',
            '--media-switches', '10',
            '--report', $short
        )

        $long = Join-Path $artifactRoot "g10-playback-long-run$run.json"
        $diagnostic = Join-Path $artifactRoot "g10-diagnostics-run$run.json"
        Invoke-Gate "Window long trend run $run" @(
            'run', '--project', $harnessProject, '-c', 'Release', '--no-build', '--',
            '--suite', 'g10',
            '--cycles', '100',
            '--dock-switches', '50',
            '--media-switches', '50',
            '--report', $long,
            '--diagnostic-report', $diagnostic
        )
        $windowReports += $short, $long
        $diagnosticReports += $diagnostic

        $shortJson = Get-Content -Raw -LiteralPath $short | ConvertFrom-Json
        $longJson = Get-Content -Raw -LiteralPath $long | ConvertFrom-Json
        Assert-WindowReport $shortJson "Window short trend run $run"
        Assert-WindowReport $longJson "Window long trend run $run"
        $windowGate = [ordered]@{
            schemaVersion = 1
            kind = 'g10-window-scale-gate'
            run = $run
            passed = $true
            limits = [ordered]@{
                handleDelta = 10
                threadDelta = 4
                pendingThreadPoolDelta = 4
                managedBytes = 32L * 1024 * 1024
                workingSetBytes = 128L * 1024 * 1024
                privateBytes = 64L * 1024 * 1024
            }
        }
        $pairs = @(
            @('HandleCount', 'handleDelta', 10L),
            @('ThreadCount', 'threadDelta', 4L),
            @('PendingThreadPoolItems', 'pendingThreadPoolDelta', 4L),
            @('ManagedHeapBytes', 'managedBytes', (32L * 1024 * 1024)),
            @('WorkingSetBytes', 'workingSetBytes', (128L * 1024 * 1024)),
            @('PrivateBytes', 'privateBytes', (64L * 1024 * 1024))
        )
        $measurements = [ordered]@{}
        foreach ($pair in $pairs) {
            $property = $pair[0]
            $name = $pair[1]
            $allowance = [long]$pair[2]
            $shortDelta = Get-Delta $shortJson.processFinal $shortJson.processBaseline $property
            $longDelta = Get-Delta $longJson.processFinal $longJson.processBaseline $property
            $limit = $shortDelta + $allowance
            $metricPassed = $longDelta -le $limit
            $measurements[$name] = [ordered]@{
                shortDelta = $shortDelta
                longDelta = $longDelta
                limit = $limit
                passed = $metricPassed
            }
            if (-not $metricPassed) {
                $windowGate.passed = $false
            }
        }
        $windowGatePath = Join-Path $artifactRoot "g10-window-scale-run$run.json"
        $windowGate.measurements = $measurements
        [IO.File]::WriteAllText(
            $windowGatePath,
            (($windowGate | ConvertTo-Json -Depth 8) -replace "`r`n", "`n") + "`n",
            $utf8NoBom)
        $windowReports += $windowGatePath
        if (-not $windowGate.passed) {
            throw "Window resource trend run $run exceeded its scale gate."
        }
    }
}

$candidatePath = Join-Path $artifactRoot 'g10-performance-candidate.json'
$aggregateArguments = @(
    'run', '--project', $benchmarkProject, '-c', 'Release', '--no-build', '--',
    '--g10-aggregate'
)
foreach ($performanceReport in $performanceReports) {
    $aggregateArguments += '--input', $performanceReport
}
$aggregateArguments += '--output', $candidatePath
Invoke-Gate "Aggregate $Runs performance runs" $aggregateArguments

$comparisonPath = Join-Path $artifactRoot 'g10-performance-comparison.json'
$timingGate = 'not-run'
if ($UpdateBaseline) {
    Copy-Item -LiteralPath $candidatePath -Destination $baselinePath -Force
    $timingGate = 'baseline-updated'
}
elseif (Test-Path $baselinePath) {
    & dotnet run --project $benchmarkProject -c Release --no-build -- `
        --g10-compare --baseline $baselinePath --candidate $candidatePath `
        --output $comparisonPath
    $comparisonExit = $LASTEXITCODE
    if ($comparisonExit -eq 3 -and $AllowNonComparable) {
        $timingGate = 'not-comparable-local-only'
    }
    elseif ($comparisonExit -ne 0) {
        throw "G10 performance comparison failed with exit code $comparisonExit."
    }
    else {
        $timingGate = 'passed'
    }
}
elseif ($AllowNonComparable) {
    $timingGate = 'baseline-missing-local-only'
}
else {
    throw 'No reviewed baseline exists; use -UpdateBaseline for the first formal run.'
}

# The sensitive scan emits counts only and never copies matched text into evidence.
$scanFiles = @(
    Get-ChildItem -LiteralPath $artifactRoot -File -Recurse |
        Where-Object Extension -in '.json', '.log', '.txt', '.md'
)
$canaries = @(
    'G3-Integration-Public-Password!'
    'G10 benchmark password'
    'G10 warmup password'
    'G10 library password'
    'G3 integration fixture'
    'derivedKey'
    'authenticationContext'
)
$privateRoots = @($env:USERPROFILE, $env:TEMP) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\', '/') } |
    Select-Object -Unique
$findings = 0
foreach ($file in $scanFiles) {
    $content = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($canary in $canaries) {
        if ($content.IndexOf($canary, [StringComparison]::Ordinal) -ge 0) {
            $findings++
        }
    }
    foreach ($privateRoot in $privateRoots) {
        if ($content.IndexOf($privateRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $findings++
        }
    }
    if ($content -match '[A-Za-z]:\\Users\\' -or
        $content.IndexOf('ftypisom', [StringComparison]::Ordinal) -ge 0) {
        $findings++
    }
}
foreach ($diagnostic in $diagnosticReports) {
    $content = Get-Content -Raw -LiteralPath $diagnostic
    if ($content -match '"(password|derivedKey|authenticationContext|publicDescription|filePath)"\s*:') {
        $findings++
    }
    if ($content -match '[A-Za-z]:\\' -or
        $content -match 'synthetic-[^"]+\.secvid' -or
        $content.IndexOf('ftypisom', [StringComparison]::Ordinal) -ge 0) {
        $findings++
    }
    if ([Text.Encoding]::UTF8.GetByteCount($content) -gt 64KB) {
        $findings++
    }
}

$sensitiveReport = [ordered]@{
    schemaVersion = 1
    kind = 'g10-sensitive-scan'
    scannedFileCount = $scanFiles.Count
    findingCount = $findings
    passed = $findings -eq 0
}
$sensitivePath = Join-Path $artifactRoot 'g10-sensitive-scan.json'
[IO.File]::WriteAllText(
    $sensitivePath,
    (($sensitiveReport | ConvertTo-Json -Depth 4) -replace "`r`n", "`n") + "`n",
    $utf8NoBom)
if ($findings -ne 0) {
    throw "G10 sensitive scan found $findings findings."
}

$technicalPassed = $formalScale -and (-not $SkipWindowGates) -and
    $timingGate -in 'passed', 'baseline-updated' -and
    $findings -eq 0
$acceptance = [ordered]@{
    schemaVersion = 1
    kind = 'g10-acceptance'
    sourceRevision = $revision
    platform = 'windows-x64'
    dotnetSdk = $dotnetVersion
    worktreeWasClean = -not $wasDirty
    technicalAcceptancePassed = $technicalPassed
    formalSignoffReady = $false
    manualSignoff = 'pending'
    timingGate = $timingGate
    gates = [ordered]@{
        builds = 'passed'
        unitTests = 'passed'
        hostTests = 'passed'
        performanceHardGates = "passed-$Runs-runs"
        playbackResourceTrend =
            if ($SkipWindowGates) { 'skipped' } else { "passed-$Runs-short-long-pairs" }
        sensitiveScan = 'passed'
    }
}
$acceptancePath = Join-Path $artifactRoot 'g10-acceptance.json'
[IO.File]::WriteAllText(
    $acceptancePath,
    (($acceptance | ConvertTo-Json -Depth 8) -replace "`r`n", "`n") + "`n",
    $utf8NoBom)

$evidenceFiles = @(
    $performanceReports
    $windowReports
    $diagnosticReports
    $candidatePath
    $comparisonPath
    $sensitivePath
    $acceptancePath
) | Where-Object { $_ -and (Test-Path $_) }
foreach ($file in $evidenceFiles) {
    Copy-JsonEvidence $file (
        Join-Path $evidenceRoot ([IO.Path]::GetFileName($file)))
}

Write-Host "`n[G10] Technical acceptance flow completed"
Write-Host "Artifacts: $artifactRoot"
Write-Host "Evidence: $evidenceRoot"
if (-not $technicalPassed) {
    Write-Host 'This local or window-skipped run is not formal technical evidence.'
}
Write-Host 'Manual export interaction still requires signoff in TestResults/G10/README.md.'
