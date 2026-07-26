param(
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$requiredDotnetMajor = 10

$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$g4Script = Join-Path $PSScriptRoot 'Release-MySmallToolsP0.ps1'
$g8Script = Join-Path $PSScriptRoot 'Accept-MySmallToolsP1.ps1'
$g10Script = Join-Path $PSScriptRoot 'Accept-MySmallToolsG10.ps1'
$dependencyGraphScript = Join-Path $PSScriptRoot 'Test-MySmallToolsUpgradeDependencyGraph.ps1'
$solution = Join-Path $workspace 'MyAvaloniaManagement.sln'
$phase4Root = Join-Path $workspace 'TestResults\Phase4'
$phase4AutomaticPath = Join-Path $phase4Root 'avalonia12-libvlcsharp.json'
$phase4GoPath = Join-Path $phase4Root 'avalonia12-libvlcsharp-go.json'
$unitTestProject = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj'
$productionRoot = Join-Path $workspace 'Plugins\MySmallTools\MySmallTools'
$documentRoot = Join-Path $productionRoot 'docs\secret-video-player'
$baselinePath = Join-Path $documentRoot (
    'benchmarks\g10-windows-x64-net10-avalonia12-dock12-reference.json')
$artifactRoot = Join-Path $workspace 'artifacts\MySmallTools\g11'
$stageRoot = Join-Path $artifactRoot 'stages'
$resultRoot = Join-Path $workspace 'TestResults\G11'
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$startedUtc = [DateTime]::UtcNow

function Assert-SafeArtifactPath {
    param([Parameter(Mandatory)] [string]$Path)

    # G11 会递归清理自己的临时目录，因此先把绝对路径锁定在唯一允许的根下。
    $full = [IO.Path]::GetFullPath($Path)
    $allowed = [IO.Path]::GetFullPath(
        (Join-Path $workspace 'artifacts\MySmallTools\g11'))
    if (-not $full.Equals($allowed, [StringComparison]::OrdinalIgnoreCase) -and
        -not $full.StartsWith(
            $allowed + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理 G11 临时目录之外的路径：$full"
    }
}

function Invoke-StageScript {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [hashtable]$Parameters
    )

    Write-Host "`n[G11] $Name"
    & $Path @Parameters
    if ($LASTEXITCODE -ne 0) {
        throw "$Name 失败，退出码 $LASTEXITCODE。"
    }
}

function Assert-SourceRevision {
    param(
        [Parameter(Mandatory)] $Report,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$ExpectedRevision
    )

    if ([string]$Report.sourceRevision -ne $ExpectedRevision) {
        throw "$Name 的源码版本不是本次 G11 快照。"
    }
}

function Test-AllGateValuesPassed {
    param([Parameter(Mandatory)] $Gates)

    foreach ($property in $Gates.PSObject.Properties) {
        if (-not ([string]$property.Value).StartsWith(
                'passed',
                [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }
    return $true
}

function Test-RequiredDocuments {
    $required = @(
        'README.md'
        'ROADMAP.md'
        'architecture-design.md'
        'integration-and-conventions.md'
        'secvid03-format.md'
        'G10-PERFORMANCE-REDACTED-DIAGNOSTICS.md'
        'G11-FINAL-ACCEPTANCE-AND-TEST-GUIDE.md'
    )
    foreach ($name in $required) {
        if (-not (Test-Path (Join-Path $documentRoot $name))) {
            throw "缺少 G11 必需文档：$name"
        }
    }

    # 只检查本地 Markdown 链接；代码链接和外部 URL 由各自工具维护。
    foreach ($file in Get-ChildItem -LiteralPath $documentRoot -Filter '*.md' -File) {
        $content = Get-Content -Raw -LiteralPath $file.FullName
        $matches = [regex]::Matches(
            $content,
            '\]\((?<target>[^)]+\.md(?:#[^)]*)?)\)')
        foreach ($match in $matches) {
            $target = $match.Groups['target'].Value.Trim('<', '>')
            if ($target.Contains('://')) {
                continue
            }
            $pathPart = ($target -split '#', 2)[0]
            $resolved = [IO.Path]::GetFullPath(
                (Join-Path $file.DirectoryName $pathPart))
            if (-not (Test-Path -LiteralPath $resolved)) {
                throw "文档链接失效：$($file.Name) -> $target"
            }
        }
    }
}

function Copy-JsonEvidence {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination
    )

    # G11 最终摘要统一采用 UTF-8/LF；阶段脚本内部仍可保留自己的原始报告格式。
    $content = [IO.File]::ReadAllText($Source)
    $normalized = $content.Replace("`r`n", "`n").Replace("`r", "`n")
    [IO.File]::WriteAllText(
        $Destination,
        $normalized.TrimEnd([char[]]"`r`n") + "`n",
        $utf8NoBom)
}

function Get-VulnerabilityEntries {
    param($Node)

    if ($null -eq $Node -or $Node -is [string]) {
        return
    }
    if ($Node -is [Collections.IEnumerable] -and
        $Node -isnot [Management.Automation.PSCustomObject]) {
        foreach ($item in $Node) {
            Get-VulnerabilityEntries $item
        }
        return
    }
    foreach ($property in $Node.PSObject.Properties) {
        if ($property.Name -eq 'vulnerabilities') {
            foreach ($entry in @($property.Value)) {
                $entry
            }
        }
        else {
            Get-VulnerabilityEntries $property.Value
        }
    }
}

if ($env:OS -ne 'Windows_NT' -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'G11 最终验收只允许在 Windows x64 进程中执行。'
}

$dotnetVersion = (& dotnet --version).Trim()
if (-not $dotnetVersion.StartsWith("$requiredDotnetMajor.")) {
    throw "G11 需要 .NET $requiredDotnetMajor SDK，当前版本为 $dotnetVersion。"
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'G11 需要 Git 读取源码快照和工作树状态。'
}
if (-not (Test-Path -LiteralPath $baselinePath)) {
    throw '缺少经审核的 G10 Windows x64 性能基线，请先执行 G10 -UpdateBaseline。'
}

$revision = (& git -C $workspace rev-parse --short=12 HEAD).Trim()
$initialStatus = @(& git -C $workspace status --porcelain)
$wasDirty = $initialStatus.Count -gt 0
if ($wasDirty -and -not $AllowDirty) {
    throw '正式 G11 验收要求 clean worktree；开发排错请显式使用 -AllowDirty。'
}

Assert-SafeArtifactPath $artifactRoot
if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
$g4Evidence = Join-Path $stageRoot 'g4'
$g8Evidence = Join-Path $stageRoot 'g8'
$g10Evidence = Join-Path $stageRoot 'g10'
New-Item -ItemType Directory -Path $g4Evidence, $g8Evidence, $g10Evidence -Force |
    Out-Null

if (-not (Test-Path -LiteralPath $phase4AutomaticPath) -or
    -not (Test-Path -LiteralPath $phase4GoPath)) {
    throw '缺少阶段 4 自动报告或人工 GO 决议。'
}
$phase4Automatic = Get-Content -Raw -LiteralPath $phase4AutomaticPath | ConvertFrom-Json
$phase4Go = Get-Content -Raw -LiteralPath $phase4GoPath | ConvertFrom-Json
if (-not [bool]$phase4Automatic.success -or
    -not [bool]$phase4Automatic.worktreeClean -or
    [string]$phase4Automatic.manualSignoff -ne 'pending' -or
    [string]$phase4Go.decision -ne 'GO' -or
    -not ([string]$phase4Automatic.sourceRevision).StartsWith($revision) -or
    -not ([string]$phase4Go.sourceRevision).StartsWith($revision)) {
    throw '阶段 4 证据未通过，或不属于当前 clean commit。'
}

Write-Host "`n[G11] 全解决方案 locked restore、严格构建与测试"
& $dependencyGraphScript -EvidenceRoot (Join-Path $stageRoot 'packages')
if ($LASTEXITCODE -ne 0) {
    throw "依赖图闸门失败，退出码 $LASTEXITCODE。"
}
& dotnet build $solution -c Release --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) {
    throw "全解决方案严格构建失败，退出码 $LASTEXITCODE。"
}
$trxRoot = Join-Path $stageRoot 'solution-tests'
New-Item -ItemType Directory -Path $trxRoot -Force | Out-Null
& dotnet test $solution -c Release --no-build --no-restore `
    --results-directory $trxRoot --logger 'trx'
if ($LASTEXITCODE -ne 0) {
    throw "全解决方案测试失败，退出码 $LASTEXITCODE。"
}
$testCounters = [ordered]@{
    total = 0
    passed = 0
    failed = 0
    notExecuted = 0
}
foreach ($trxFile in Get-ChildItem -LiteralPath $trxRoot -Filter '*.trx' -File -Recurse) {
    [xml]$trx = Get-Content -Raw -LiteralPath $trxFile.FullName
    $counters = $trx.TestRun.ResultSummary.Counters
    $testCounters.total += [int]$counters.total
    $testCounters.passed += [int]$counters.passed
    $testCounters.failed += [int]$counters.failed
    $testCounters.notExecuted += [int]$counters.notExecuted
}
if ($testCounters.total -lt 240 -or
    $testCounters.passed -ne $testCounters.total -or
    $testCounters.failed -ne 0 -or
    $testCounters.notExecuted -ne 0) {
    throw "全项目测试计数不满足要求：$($testCounters | ConvertTo-Json -Compress)"
}

Write-Host "`n[G11] NuGet 漏洞与过期包审计"
$vulnerabilityJson = @(
    & dotnet list $solution package --vulnerable --include-transitive `
        --format json --output-version 1
) -join "`n"
if ($LASTEXITCODE -ne 0) {
    throw "NuGet 漏洞审计失败，退出码 $LASTEXITCODE。"
}
$vulnerabilityPath = Join-Path $stageRoot 'nuget-vulnerabilities.json'
[IO.File]::WriteAllText($vulnerabilityPath, $vulnerabilityJson, $utf8NoBom)
$vulnerabilityReport = $vulnerabilityJson | ConvertFrom-Json
$blockingVulnerabilities = @(
    Get-VulnerabilityEntries $vulnerabilityReport |
        Where-Object {
            [string]$_.severity -in @('High', 'Critical')
        }
)
if ($blockingVulnerabilities.Count -ne 0) {
    throw "发现 $($blockingVulnerabilities.Count) 个高危或严重 NuGet 漏洞。"
}
$outdatedJson = @(
    & dotnet list $solution package --outdated --include-transitive `
        --format json --output-version 1
) -join "`n"
if ($LASTEXITCODE -ne 0) {
    throw "NuGet 过期包审计失败，退出码 $LASTEXITCODE。"
}
[IO.File]::WriteAllText(
    (Join-Path $stageRoot 'nuget-outdated.json'),
    $outdatedJson,
    $utf8NoBom)

$g4Parameters = @{ EvidenceRoot = $g4Evidence }
$g8Parameters = @{ EvidenceRoot = $g8Evidence }
$g10Parameters = @{ EvidenceRoot = $g10Evidence }
if ($AllowDirty) {
    $g4Parameters.AllowDirty = $true
    $g8Parameters.AllowDirty = $true
    $g10Parameters.AllowDirty = $true
}
Invoke-StageScript 'G4/P0 完整发布门禁' $g4Script $g4Parameters
Invoke-StageScript 'G8/P1 完整集成门禁' $g8Script $g8Parameters

Write-Host "`n[G11] G9 平台抽象回归"
& dotnet test $unitTestProject -c Release --no-build --no-restore --filter `
    'FullyQualifiedName~G9PlatformAbstractionTests'
if ($LASTEXITCODE -ne 0) {
    throw "G9 平台抽象回归失败，退出码 $LASTEXITCODE。"
}
$hwndAssignments = @(
    Get-ChildItem -LiteralPath $productionRoot -Filter '*.cs' -File -Recurse |
        Select-String -Pattern '\.Hwnd\s*='
)
if ($hwndAssignments.Count -ne 1 -or
    $hwndAssignments[0].Path -notlike '*\Views\SecretVideoPlayer\EmbeddedVideoSurface.cs') {
    throw 'HWND 绑定没有唯一收口在 EmbeddedVideoSurface Windows 适配边界。'
}

Invoke-StageScript 'G10 性能、资源与脱敏诊断门禁' $g10Script $g10Parameters

$g4AcceptancePath = @(
    Get-ChildItem -LiteralPath $g4Evidence -Filter '*.acceptance.json' -File
)
if ($g4AcceptancePath.Count -ne 1) {
    throw 'G4 阶段没有生成唯一验收摘要。'
}
$g4 = Get-Content -Raw -LiteralPath $g4AcceptancePath[0].FullName | ConvertFrom-Json
$g8 = Get-Content -Raw -LiteralPath (
    Join-Path $g8Evidence 'g8-acceptance.json') | ConvertFrom-Json
$g10 = Get-Content -Raw -LiteralPath (
    Join-Path $g10Evidence 'g10-acceptance.json') | ConvertFrom-Json

Assert-SourceRevision $g4 'G4' $revision
Assert-SourceRevision $g8 'G8' $revision
Assert-SourceRevision $g10 'G10' $revision

$g4Passed = (Test-AllGateValuesPassed $g4.gates) -and
    @($g4.memoryInputMiB)[0] -eq 64 -and
    @($g4.memoryInputMiB)[1] -eq 512
$g8Passed = [bool]$g8.technicalAcceptancePassed -and
    (Test-AllGateValuesPassed $g8.gates) -and
    [int]$g8.queueItems -eq 100 -and [int]$g8.libraryItems -eq 1000
$g10Passed = [bool]$g10.technicalAcceptancePassed -and
    (Test-AllGateValuesPassed $g10.gates) -and
    [string]$g10.timingGate -eq 'passed'

[xml]$centralPackages = Get-Content -Raw -LiteralPath (
    Join-Path $workspace 'Directory.Packages.props')
$centralVersions = @{}
foreach ($packageVersion in $centralPackages.Project.ItemGroup.PackageVersion) {
    $centralVersions[[string]$packageVersion.Include] = [string]$packageVersion.Version
}
$g4ManifestFiles = @(
    Get-ChildItem -LiteralPath $g4Evidence -Filter '*.manifest.json' -File
)
if ($g4ManifestFiles.Count -ne 1) {
    throw 'G4 没有生成唯一发布 Manifest。'
}
$releaseManifest = Get-Content -Raw -LiteralPath $g4ManifestFiles[0].FullName |
    ConvertFrom-Json
if ([string]$releaseManifest.requiredHost.avalonia -ne $centralVersions['Avalonia'] -or
    [string]$releaseManifest.requiredHost.dock -ne $centralVersions['Dock.Avalonia'] -or
    [string]$releaseManifest.requiredHost.targetFramework -ne 'net10.0' -or
    -not ([string]$releaseManifest.versions.libVLCSharp).StartsWith(
        $centralVersions['LibVLCSharp'])) {
    throw '发布 Manifest 与中央 Avalonia、Dock、LibVLCSharp 或 TFM 版本不一致。'
}
if (-not ([string]$phase4Automatic.avaloniaVersion).StartsWith(
        $centralVersions['Avalonia']) -or
    -not ([string]$phase4Automatic.dockVersion).StartsWith(
        $centralVersions['Dock.Avalonia']) -or
    -not ([string]$phase4Automatic.libVlcSharpVersion).StartsWith(
        $centralVersions['LibVLCSharp'])) {
    throw '阶段 4 运行程序集版本与中央版本不一致。'
}

Test-RequiredDocuments

# 阶段脚本只能写 ignored artifacts；源码状态出现任何新增变化都视为快照失效。
$finalStatus = @(& git -C $workspace status --porcelain)
$statusDifference = @(Compare-Object $initialStatus $finalStatus)
if ($statusDifference.Count -ne 0) {
    throw 'G11 执行期间工作树状态发生变化，拒绝生成最终技术证据。'
}

$technicalPassed = $g4Passed -and $g8Passed -and $g10Passed
$technicalPassed = $technicalPassed -and
    [bool]$phase4Automatic.success -and
    [string]$phase4Go.decision -eq 'GO' -and
    $blockingVulnerabilities.Count -eq 0
$formalSignoffReady = $technicalPassed -and (-not $wasDirty) -and
    [bool]$g4.publishable -and [bool]$g8.cleanTechnicalEvidenceReady -and
    [bool]$g10.worktreeWasClean
if (-not $technicalPassed) {
    throw 'G11 至少一个技术阶段未通过。'
}

# 只有全部阶段完成且源码快照未变化后，才写入可提交证据。
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null
$staleSignoffFiles = @(
    Join-Path $resultRoot 'g11-manual-signoff.json'
    Join-Path $resultRoot 'g11-final-acceptance.json'
)
foreach ($staleFile in $staleSignoffFiles) {
    if (Test-Path -LiteralPath $staleFile) {
        Remove-Item -LiteralPath $staleFile -Force
    }
}

$copiedEvidence = [ordered]@{
    phase4Automatic = 'avalonia12-libvlcsharp.json'
    phase4Go = 'avalonia12-libvlcsharp-go.json'
    g4 = 'g4-acceptance.json'
    g8 = 'g8-acceptance.json'
    g10 = 'g10-acceptance.json'
    sensitiveScan = 'g10-sensitive-scan.json'
    packageGraph = 'package-graph-summary.json'
    vulnerabilities = 'nuget-vulnerabilities.json'
    outdated = 'nuget-outdated.json'
    releaseManifest = 'mysmalltools-release-manifest.json'
}
Copy-JsonEvidence $phase4AutomaticPath (
    Join-Path $resultRoot $copiedEvidence.phase4Automatic)
Copy-JsonEvidence $phase4GoPath (
    Join-Path $resultRoot $copiedEvidence.phase4Go)
Copy-JsonEvidence $g4AcceptancePath[0].FullName (
    Join-Path $resultRoot $copiedEvidence.g4)
Copy-JsonEvidence (Join-Path $g8Evidence 'g8-acceptance.json') (
    Join-Path $resultRoot $copiedEvidence.g8)
Copy-JsonEvidence (Join-Path $g10Evidence 'g10-acceptance.json') (
    Join-Path $resultRoot $copiedEvidence.g10)
Copy-JsonEvidence (Join-Path $g10Evidence 'g10-sensitive-scan.json') (
    Join-Path $resultRoot $copiedEvidence.sensitiveScan)
Copy-JsonEvidence (Join-Path $stageRoot 'packages\package-graph-summary.json') (
    Join-Path $resultRoot $copiedEvidence.packageGraph)
Copy-JsonEvidence $vulnerabilityPath (
    Join-Path $resultRoot $copiedEvidence.vulnerabilities)
Copy-JsonEvidence (Join-Path $stageRoot 'nuget-outdated.json') (
    Join-Path $resultRoot $copiedEvidence.outdated)
Copy-JsonEvidence $g4ManifestFiles[0].FullName (
    Join-Path $resultRoot $copiedEvidence.releaseManifest)

$technical = [ordered]@{
    schemaVersion = 1
    kind = 'g11-technical-acceptance'
    sourceRevision = $revision
    startedUtc = $startedUtc.ToString('O')
    completedUtc = [DateTime]::UtcNow.ToString('O')
    platform = 'windows-x64'
    dotnetSdk = $dotnetVersion
    worktreeWasClean = -not $wasDirty
    technicalAcceptancePassed = $technicalPassed
    formalSignoffReady = $formalSignoffReady
    manualSignoff = 'pending'
    stages = [ordered]@{
        g4P0 = if ($g4Passed) { 'passed' } else { 'failed' }
        phase4Avalonia12LibVlc = 'passed-go'
        g8P1 = if ($g8Passed) { 'passed' } else { 'failed' }
        g9PlatformAbstraction = 'passed'
        g10PerformanceDiagnostics = if ($g10Passed) { 'passed' } else { 'failed' }
        documents = 'passed'
        sourceIntegrity = 'passed'
        dependencyGraph = 'passed'
        vulnerabilities = 'passed-no-high-or-critical'
        solutionTests = "passed-$($testCounters.total)-zero-skipped"
    }
    evidenceFiles = $copiedEvidence
}
$technicalPath = Join-Path $resultRoot 'g11-technical-acceptance.json'
[IO.File]::WriteAllText(
    $technicalPath,
    (($technical | ConvertTo-Json -Depth 8) -replace "`r`n", "`n") + "`n",
    $utf8NoBom)

Write-Host "`n[G11] 技术验收完成"
Write-Host "Technical evidence: $technicalPath"
if ($formalSignoffReady) {
    Write-Host '请按 G11 手册完成人工检查后执行 Approve-MySmallToolsG11.ps1。'
}
else {
    Write-Host '本次为 dirty-worktree 开发验证，不能进入正式人工签字。'
}
