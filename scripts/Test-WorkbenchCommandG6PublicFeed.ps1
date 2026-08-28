param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

# 本入口只验证已经上传到 NuGet.org 的公开字节，不接受候选目录作为还原源。
# 发布动作与公开消费验证刻意分离：即使上传 API 返回成功，只要公共 CDN、模板安装、锁定还原、
# Release 构建、测试或确定性插件包任一失败，本入口就不会生成绿色摘要。
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$candidateFeed = Join-Path $repositoryRoot 'artifacts/test-results/WorkbenchCommandG6/candidate-feed'
$resultRoot = Join-Path $repositoryRoot 'artifacts/test-results/WorkbenchCommandG6PublicFeed'
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$temporaryRoot = Join-Path $temporaryParent ("myavalonia-workbench-command-g6-public-{0}" -f [Guid]::NewGuid().ToString('N'))
$generatedRoot = Join-Path $temporaryRoot 'PublicCommandTemplateProbe'
$templateHive = Join-Path $temporaryRoot 'template-hive'
$isolatedPackages = Join-Path $temporaryRoot 'nuget-packages'
$isolatedCliHome = Join-Path $temporaryRoot 'dotnet-home'
$originalPackages = [Environment]::GetEnvironmentVariable('NUGET_PACKAGES', 'Process')
$originalCliHome = [Environment]::GetEnvironmentVariable('DOTNET_CLI_HOME', 'Process')

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-ChildPath {
    param([string]$Path, [string]$AllowedRoot, [string]$Purpose)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\', '/')
    Assert-True ($fullPath.StartsWith(
            $fullRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) "$Purpose 越过允许根：$fullPath"
}

function Invoke-DotNet {
    param([string[]]$Arguments, [string]$WorkingDirectory = $repositoryRoot)
    Push-Location $WorkingDirectory
    try {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet 命令失败，退出码 $LASTEXITCODE：dotnet $($Arguments -join ' ')"
        }
    }
    finally { Pop-Location }
}

function Write-PublicNuGetConfig {
    param([string]$Root)
    $path = Join-Path $Root 'NuGet.Config'
    $text = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
'@
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
    return $path
}

function Get-TrxCounts {
    param([string]$Path)
    [xml]$trx = Get-Content -Raw -LiteralPath $Path
    $counters = $trx.TestRun.ResultSummary.Counters
    return [ordered]@{
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        skipped = [int]$counters.notExecuted
    }
}

function Get-UnsignedPackageContentHash {
    param([string]$Path)

    # NuGet.org 会在已接收的 nupkg 中追加仓库签名 `.signature.p7s`，所以公开文件的整体哈希
    # 不会等于上传前候选。这里对其余 ZIP 项按稳定路径、长度和内容哈希生成聚合摘要，既保留
    # 仓库签名，又能证明实际消费的 nuspec/lib/ref 等内容没有被重新打包或替换。
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $lines = @($archive.Entries |
            Where-Object { $_.FullName -cne '.signature.p7s' } |
            Sort-Object FullName |
            ForEach-Object {
                $stream = $_.Open()
                try {
                    $hash = [Convert]::ToHexString(
                        [Security.Cryptography.SHA256]::HashData($stream))
                    return "$($_.FullName)`t$($_.Length)`t$hash"
                }
                finally { $stream.Dispose() }
            })
        $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
        return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    }
    finally { $archive.Dispose() }
}

try {
    Assert-ChildPath $resultRoot (Join-Path $repositoryRoot 'artifacts') '公开源结果目录'
    if (Test-Path -LiteralPath $resultRoot) {
        Remove-Item -LiteralPath $resultRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resultRoot, $temporaryRoot, $isolatedPackages, $isolatedCliHome -Force | Out-Null
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $isolatedPackages, 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_CLI_HOME', $isolatedCliHome, 'Process')
    $publicConfig = Write-PublicNuGetConfig $temporaryRoot

    # Flat Container 返回的是公共消费者真正取得的 nupkg。NuGet.org 会追加仓库签名，因此整包哈希分别
    # 留证，并以“签名有效 + 排除签名后的内容摘要相等”防止同版本内容替换；符号包不属于普通
    # V3 Flat Container 下载面，单独记录本地冻结哈希。
    $publicPackages = [ordered]@{
        'MyAvaloniaManagement.PluginSdk.3.3.0.nupkg' = 'myavaloniamanagement.pluginsdk'
        'MyAvaloniaManagement.PluginSdk.UI.3.3.0.nupkg' = 'myavaloniamanagement.pluginsdk.ui'
        'MyAvaloniaManagement.Plugin.Templates.1.3.0.nupkg' = 'myavaloniamanagement.plugin.templates'
    }
    $publicHashes = [ordered]@{}
    $candidateHashes = [ordered]@{}
    $contentHashes = [ordered]@{}
    foreach ($entry in $publicPackages.GetEnumerator()) {
        $candidatePath = Join-Path $candidateFeed $entry.Key
        Assert-True (Test-Path -LiteralPath $candidatePath -PathType Leaf) "冻结候选不存在：$candidatePath"
        $version = if ($entry.Key -like '*Templates*') { '1.3.0' } else { '3.3.0' }
        $lowerFile = $entry.Key.ToLowerInvariant()
        $url = "https://api.nuget.org/v3-flatcontainer/$($entry.Value)/$version/$lowerFile"
        $downloadPath = Join-Path $temporaryRoot $entry.Key
        Invoke-WebRequest -Uri $url -OutFile $downloadPath
        $candidateHash = (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash
        $publicHash = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash
        Invoke-DotNet @('nuget', 'verify', $downloadPath, '--all') $temporaryRoot
        $candidateContentHash = Get-UnsignedPackageContentHash $candidatePath
        $publicContentHash = Get-UnsignedPackageContentHash $downloadPath
        Assert-True ($candidateContentHash -ceq $publicContentHash) `
            "公开包的非签名内容与冻结候选不一致：$($entry.Key)"
        $candidateHashes[$entry.Key] = $candidateHash
        $publicHashes[$entry.Key] = $publicHash
        $contentHashes[$entry.Key] = $publicContentHash
    }

    Invoke-DotNet @(
        'new', 'install', 'MyAvaloniaManagement.Plugin.Templates@1.3.0',
        '--debug:custom-hive', $templateHive, '--force') $temporaryRoot
    Invoke-DotNet @(
        'new', 'myavalonia-plugin', '-n', 'PublicCommandTemplateProbe',
        '--plugin-id', 'myavalonia.plugin.public-command-template-probe',
        '-o', $generatedRoot, '--debug:custom-hive', $templateHive, '--no-update-check') $temporaryRoot
    $generatedConfig = Write-PublicNuGetConfig $generatedRoot
    $solution = Join-Path $generatedRoot 'PublicCommandTemplateProbe.slnx'
    Invoke-DotNet @(
        'restore', $solution, '--locked-mode', '--configfile', $generatedConfig,
        '--packages', $isolatedPackages, '--nologo') $generatedRoot
    Invoke-DotNet @(
        'build', $solution, '-c', $Configuration, '--no-restore', '--nologo',
        '-warnaserror', '-p:SkipPluginDeploy=true') $generatedRoot
    $trxPath = Join-Path $resultRoot 'PublicCommandTemplateProbe.trx'
    Invoke-DotNet @(
        'test', $solution, '-c', $Configuration, '--no-build', '--no-restore', '--nologo',
        '--results-directory', $resultRoot,
        '--logger', 'trx;LogFileName=PublicCommandTemplateProbe.trx') $generatedRoot
    $testCounts = Get-TrxCounts $trxPath
    Assert-True ($testCounts.passed -eq 4 -and $testCounts.failed -eq 0 -and $testCounts.skipped -eq 0) `
        '公开模板生成项目必须通过 4/4 单元测试。'

    $pluginProject = Join-Path $generatedRoot 'src/PublicCommandTemplateProbe.Plugin/PublicCommandTemplateProbe.Plugin.csproj'
    $packageHashes = @()
    foreach ($run in 1..2) {
        $output = Join-Path $resultRoot "package-$run"
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        Invoke-DotNet @(
            'msbuild', $pluginProject, '-t:BuildManagedPluginPackage',
            "-p:Configuration=$Configuration", "-p:ManagedPluginPackageOutput=$output",
            "-p:RestoreConfigFile=$generatedConfig", '--nologo') $generatedRoot
        $zip = @(Get-ChildItem -LiteralPath $output -Filter '*.zip' -File)
        Assert-True ($zip.Count -eq 1) "第 $run 轮没有生成唯一插件 ZIP。"
        $packageHashes += (Get-FileHash -LiteralPath $zip[0].FullName -Algorithm SHA256).Hash
    }
    Assert-True ($packageHashes[0] -ceq $packageHashes[1]) '公开源模板生成的两轮插件 ZIP 不确定。'

    $symbolHashes = [ordered]@{}
    foreach ($symbolName in @(
            'MyAvaloniaManagement.PluginSdk.3.3.0.snupkg',
            'MyAvaloniaManagement.PluginSdk.UI.3.3.0.snupkg')) {
        $symbolPath = Join-Path $candidateFeed $symbolName
        Assert-True (Test-Path -LiteralPath $symbolPath -PathType Leaf) "冻结符号包不存在：$symbolPath"
        $symbolHashes[$symbolName] = (Get-FileHash -LiteralPath $symbolPath -Algorithm SHA256).Hash
    }

    $summary = [ordered]@{
        schemaVersion = 1
        stage = 'G6PublicFeed'
        configuration = $Configuration
        sdkVersion = '3.3.0'
        templateVersion = '1.3.0'
        source = 'https://api.nuget.org/v3/index.json'
        candidatePackageSha256 = $candidateHashes
        publicPackageSha256 = $publicHashes
        unsignedContentSha256 = $contentHashes
        nugetRepositorySignatureVerified = $true
        candidateSymbolSha256 = $symbolHashes
        generatedTemplateTests = $testCounts
        deterministicPluginZipSha256 = $packageHashes[0]
        deterministicRuns = 2
        passed = $true
        uploaded = $true
        published = $true
        publicOnlyVerification = $true
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        hostReleaseGate = $false
        hostProductPublished = $false
        tagCreated = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 12),
        [Text.UTF8Encoding]::new($false))
    Write-Host '[Workbench Command G6 Public Feed] 公开包字节、模板安装、锁定还原、测试与确定性打包全部通过。'
}
finally {
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $originalPackages, 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_CLI_HOME', $originalCliHome, 'Process')
    if (Test-Path -LiteralPath $temporaryRoot) {
        Assert-ChildPath $temporaryRoot $temporaryParent '公开源临时清理'
        & dotnet build-server shutdown | Out-Host
        foreach ($attempt in 1..3) {
            try {
                Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction Stop
                break
            }
            catch {
                if ($attempt -eq 3) { Write-Warning "公开源临时目录暂未完全清理：$temporaryRoot。" }
                else { Start-Sleep -Milliseconds 500 }
            }
        }
    }
}
