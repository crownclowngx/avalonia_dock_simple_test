#requires -Version 7.0
<#
.SYNOPSIS
从固定上游和受版本控制的补丁构建 Host 专用 Dock.Avalonia 包。
.DESCRIPTION
源码、构建和包源均放在主仓 artifacts 中。以输入指纹隔离不同补丁，先测试再打包，
仅复用身份及包摘要完全一致的成功结果。脚本不修改全局配置、不发布包、不删除目录。
NuGet ZIP 的时间戳和 core-properties 随机文件名统一规范化，保证锁文件内容哈希可重建。
#>
[CmdletBinding()]
param([switch]$ForceRebuild)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$metadataPath = Join-Path $repositoryRoot 'patches/dock-area-fill/baseline.json'
$patchPath = Join-Path $repositoryRoot 'patches/dock-area-fill/area-fill.patch'
$baseline = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$patchHash = (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash.ToLowerInvariant()
$scriptHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
$identity = "$($baseline.repository)|$($baseline.commit)|$($baseline.packageVersion)|$($baseline.assemblyVersion)|$($baseline.sdkVersion)|$($baseline.packageTimestampUtc)|$patchHash|$scriptHash"
$identityHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identity))).ToLowerInvariant()
$taskRoot = Join-Path $repositoryRoot 'artifacts/dock-area-fill'
$buildRoot = Join-Path $taskRoot ('build/' + $identityHash.Substring(0, 16))
if ($ForceRebuild) {
    # 重建使用全新检出路径，顺便检验 DLL/PDB 未泄漏本机路径；旧包仍只允许同哈希复用。
    $buildRoot += '-rebuild-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
}
$sourceRoot = Join-Path $buildRoot 'source'
$feedRoot = Join-Path $taskRoot 'feed'
$packagePath = Join-Path $feedRoot "Dock.Avalonia.$($baseline.packageVersion).nupkg"
$receiptPath = Join-Path $buildRoot 'receipt.json'

function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable 执行失败，退出码 $LASTEXITCODE。" }
}

function Assert-PackageHash([string]$Path, [string]$Expected) {
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Expected -and $actual -ne $Expected) { throw "补丁包摘要不一致：$actual；预期 $Expected。" }
    return $actual
}

function Write-CanonicalPackage([string]$InputPath, [string]$OutputPath, [DateTimeOffset]$Timestamp) {
    # 规范化只改变 ZIP 元数据；DLL、XML 文档和许可证等内容逐字节保留。
    $inputArchive = [IO.Compression.ZipFile]::OpenRead($InputPath)
    try {
        $coreEntry = @($inputArchive.Entries | Where-Object FullName -Like '*.psmdcp')
        if ($coreEntry.Count -ne 1) { throw 'NuGet core-properties 数量异常。' }
        $oldCoreName = $coreEntry[0].FullName
        $newCoreName = 'package/services/metadata/core-properties/dock-area-fill.psmdcp'
        $outputStream = [IO.File]::Open($OutputPath, [IO.FileMode]::Create, [IO.FileAccess]::Write)
        $outputArchive = [IO.Compression.ZipArchive]::new($outputStream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($entry in ($inputArchive.Entries | Sort-Object FullName -CaseSensitive)) {
                $entryName = if ($entry.FullName -eq $oldCoreName) { $newCoreName } else { $entry.FullName }
                $created = $outputArchive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
                # 每个版本固定但不同的时间戳：不同 DLL 可能大小相同，不能让 MSBuild 的
                # SkipUnchangedFiles 因相同大小和时间而保留旧补丁。构建后门禁另核对实际 DLL 哈希。
                $created.LastWriteTime = $Timestamp
                $from = $entry.Open()
                $to = $created.Open()
                try {
                    if ($entry.FullName -eq '_rels/.rels') {
                        $reader = [IO.StreamReader]::new($from)
                        $value = $reader.ReadToEnd().Replace($oldCoreName, $newCoreName)
                        # OPC 关系 ID 同样由 NuGet 随机生成，只需要在本关系文件内稳定唯一。
                        [xml]$relationships = $value
                        $index = 0
                        foreach ($relationship in $relationships.DocumentElement.ChildNodes) {
                            $relationship.SetAttribute('Id', 'R' + (++$index))
                        }
                        $bytes = [Text.Encoding]::UTF8.GetBytes($relationships.OuterXml)
                        $to.Write($bytes, 0, $bytes.Length)
                    }
                    else { $from.CopyTo($to) }
                }
                finally { $from.Dispose(); $to.Dispose() }
            }
        }
        finally { $outputArchive.Dispose(); $outputStream.Dispose() }
    }
    finally { $inputArchive.Dispose() }
}

Push-Location $repositoryRoot
try {
    $installedSdks = & dotnet --list-sdks
    if ($LASTEXITCODE -ne 0 -or -not ($installedSdks | Where-Object { $_.StartsWith($baseline.sdkVersion + ' [') })) {
        throw "重建补丁需要安装固定 SDK $($baseline.sdkVersion)。"
    }
}
finally { Pop-Location }

if (-not $ForceRebuild -and (Test-Path -LiteralPath $receiptPath) -and (Test-Path -LiteralPath $packagePath)) {
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($receipt.identity -eq $identityHash -and $receipt.testsPassed -ge $baseline.minimumTests -and
        $receipt.testsFailed -eq 0 -and $receipt.testsSkipped -eq 0) {
        $null = Assert-PackageHash $packagePath $receipt.packageSha256
        $null = Assert-PackageHash $packagePath $baseline.packageSha256
        Write-Output "Dock 区域停靠补丁已准备，复用已测试包：$packagePath"
        exit 0
    }
}

New-Item -ItemType Directory -Path $buildRoot, $feedRoot -Force | Out-Null
# 在隔离构建根固定 SDK，不修改主仓允许 latestPatch 的选择，也不受上游 global.json 影响。
@{ sdk = @{ version = $baseline.sdkVersion; rollForward = 'disable'; allowPrerelease = $false } } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $buildRoot 'global.json') -Encoding utf8NoBOM
Push-Location $buildRoot
try {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot '.git'))) {
        Invoke-Checked git @('init', '--quiet', $sourceRoot)
        Invoke-Checked git @('-C', $sourceRoot, 'config', 'core.longpaths', 'true')
        Invoke-Checked git @('-C', $sourceRoot, 'remote', 'add', 'origin', $baseline.repository)
        Invoke-Checked git @('-C', $sourceRoot, 'fetch', '--depth', '1', 'origin', $baseline.commit)
        Invoke-Checked git @('-C', $sourceRoot, 'checkout', '--detach', 'FETCH_HEAD')
    }
    $revision = (& git -C $sourceRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $revision -ne $baseline.commit) { throw '上游源码身份不匹配。' }
    $status = & git -C $sourceRoot status --porcelain
    if (-not $status) {
        Invoke-Checked git @('-C', $sourceRoot, 'apply', '--check', $patchPath)
        Invoke-Checked git @('-C', $sourceRoot, 'apply', $patchPath)
    }
    else {
        # 重试仅允许已经完整应用同一补丁的构建目录，额外修改一律拒绝。
        Invoke-Checked git @('-C', $sourceRoot, 'apply', '--reverse', '--check', $patchPath)
    }
    # git diff 也纳入新文件，供完整源码差异比对；仅改动隔离的上游检出索引。
    Invoke-Checked git @('-C', $sourceRoot, 'add', '--intent-to-add', '.')
    $actualPatch = Join-Path $buildRoot 'actual.patch'
    Invoke-Checked git @('-C', $sourceRoot, 'diff', '--binary', '--full-index', '--no-ext-diff', "--output=$actualPatch")
    if ((Get-FileHash -LiteralPath $actualPatch).Hash.ToLowerInvariant() -ne $patchHash) {
        throw '隔离源码包含补丁之外的差异；保留目录供排查，不覆盖该源码。'
    }
    $testProject = Join-Path $sourceRoot 'tests/Dock.Avalonia.HeadlessTests/Dock.Avalonia.HeadlessTests.csproj'
    # 独立上游依赖必须使用自己的源配置，避免继承主仓的 Dock.Avalonia 包源映射。
    $restoreConfig = Join-Path $sourceRoot 'NuGet.Config'
    Invoke-Checked dotnet @('test', $testProject, '-c', 'Release', '-m:1', '--nologo',
        "-p:RestoreConfigFile=$restoreConfig",
        '--filter', 'FullyQualifiedName~DockAreaFill|FullyQualifiedName~DockHelpers|FullyQualifiedName~DockTargetTests|FullyQualifiedName~HostWindowStateTests|FullyQualifiedName~DockControlStateTests',
        '--logger', 'trx;LogFileName=dock-area-fill.trx', '--results-directory', (Join-Path $buildRoot 'tests'))
    [xml]$testReport = Get-Content -LiteralPath (Join-Path $buildRoot 'tests/dock-area-fill.trx') -Raw
    $counters = $testReport.TestRun.ResultSummary.Counters
    if ([int]$counters.passed -lt [int]$baseline.minimumTests -or [int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0) {
        throw '补丁测试必须实际执行且无失败、无跳过。'
    }
    $rawFeed = Join-Path $buildRoot 'raw-feed'
    $project = Join-Path $sourceRoot 'src/Dock.Avalonia/Dock.Avalonia.csproj'
    Invoke-Checked dotnet @('pack', $project, '-c', 'Release', '-m:1', '--nologo', '-warnaserror',
        "-p:RestoreConfigFile=$restoreConfig",
        '-p:DockAreaFillPackage=true', "-p:PackageVersion=$($baseline.packageVersion)",
        "-p:AssemblyVersion=$($baseline.assemblyVersion)", "-p:FileVersion=$($baseline.assemblyVersion)",
        "-p:InformationalVersion=$($baseline.packageVersion)", '-p:IncludeSourceRevisionInInformationalVersion=false',
        '-p:IncludeSymbols=false', '-p:Deterministic=true', '-p:ContinuousIntegrationBuild=true',
        '-p:DeterministicSourcePaths=true', '-o', $rawFeed)
    $candidatePath = Join-Path $buildRoot 'canonical.nupkg'
    Write-CanonicalPackage (Join-Path $rawFeed "Dock.Avalonia.$($baseline.packageVersion).nupkg") $candidatePath ([DateTimeOffset]::Parse($baseline.packageTimestampUtc))
    $packageHash = Assert-PackageHash $candidatePath $baseline.packageSha256
    if (Test-Path -LiteralPath $packagePath) {
        $null = Assert-PackageHash $packagePath $packageHash
    }
    else { Copy-Item -LiteralPath $candidatePath -Destination $packagePath }
    [ordered]@{ identity = $identityHash; upstream = $revision; patchSha256 = $patchHash;
        packageVersion = $baseline.packageVersion; packageSha256 = $packageHash;
        testsPassed = [int]$counters.passed; testsFailed = 0; testsSkipped = 0 } |
        ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
    Write-Output "补丁构建通过：$packagePath；SHA256=$packageHash"
}
finally { Pop-Location }
