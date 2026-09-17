#requires -Version 7.0
<#
.SYNOPSIS
构建并验证固定 Avalonia Base 布局补丁，仅为本仓宿主提供运行时资产。
.DESCRIPTION
保留官方 NuGet 与 SDK 精确依赖，不覆盖包缓存。每份补丁有独立身份、源码及测试收据。
候选先通过独立 Headless 测试，再安装到 artifacts/runtime，供 MSBuild 在打包前选择。
#>
[CmdletBinding()]
param([switch]$ForceRebuild, [switch]$IsolatedRestore)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$patchRoot = Join-Path $repositoryRoot 'patches/avalonia-cross-window-layout'
$baseline = Get-Content (Join-Path $patchRoot 'baseline.json') -Raw | ConvertFrom-Json
$taskRoot = Join-Path $repositoryRoot 'artifacts/avalonia-cross-window-layout'
$runtimeRoot = Join-Path $taskRoot 'runtime'
$inputs = @((Join-Path $patchRoot 'baseline.json'), (Join-Path $patchRoot 'layout-ownership.patch'),
    (Join-Path $patchRoot 'runtime-build.targets'), $PSCommandPath,
    (Join-Path $repositoryRoot 'Host/MyAvaloniaManagement.UiTests/AvaloniaLayoutOwnershipUiTests.cs')) +
    @(Get-ChildItem (Join-Path $patchRoot 'validation') -File | Sort-Object Name | ForEach-Object FullName) +
    @(Get-ChildItem (Join-Path $patchRoot 'restore') -Recurse -File | Sort-Object FullName | ForEach-Object FullName)
$fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::UTF8.GetBytes((($inputs | ForEach-Object { (Get-FileHash $_).Hash }) -join '|'))))

function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable 执行失败：$LASTEXITCODE。" }
}
function Assert-Hash([string]$Path, [string]$Expected) {
    $actual = (Get-FileHash -LiteralPath $Path).Hash
    if ($Expected -and $actual -ne $Expected) { throw "布局补丁摘要不一致：$Path。" }
    return $actual
}
$receiptPath = Join-Path $runtimeRoot 'receipt.json'
if (-not $ForceRebuild -and -not $IsolatedRestore -and (Test-Path $receiptPath)) {
    $receipt = Get-Content $receiptPath -Raw | ConvertFrom-Json
    if ($receipt.fingerprint -eq $fingerprint -and $receipt.passed -ge $baseline.minimumTests -and
        $receipt.failed -eq 0 -and $receipt.skipped -eq 0) {
        $null = Assert-Hash (Join-Path $runtimeRoot 'Avalonia.Base.dll') $receipt.dllSha256
        $null = Assert-Hash (Join-Path $runtimeRoot 'Avalonia.Base.pdb') $receipt.pdbSha256
        $null = Assert-Hash (Join-Path $runtimeRoot 'Avalonia.Base.dll') $baseline.dllSha256
        $null = Assert-Hash (Join-Path $runtimeRoot 'Avalonia.Base.pdb') $baseline.pdbSha256
        $null = Assert-Hash $receipt.testTrx $receipt.testSha256
        Write-Output "Avalonia 布局补丁已验证：$($baseline.identity)。"
        exit 0
    }
}
$sdkLines = & dotnet --list-sdks
if (-not ($sdkLines | Where-Object { $_.StartsWith($baseline.sdkVersion + ' [') })) { throw '缺少固定 .NET SDK。' }
$buildRoot = Join-Path $taskRoot ('build/' + $fingerprint.Substring(0, 16) + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$sourceRoot = Join-Path $buildRoot 'source'
$seed = Join-Path $taskRoot 'pinned'
New-Item -ItemType Directory -Force $buildRoot | Out-Null
if (-not (Test-Path (Join-Path $seed '.git'))) {
    Invoke-Checked git @('init', $seed)
    Invoke-Checked git @('-C', $seed, 'fetch', '--depth', '1', $baseline.repository, $baseline.commit)
}
# 本地对象缓存仅减少重复下载，固定提交的对象哈希仍由 Git 验证；不使用其工作区文件。
Invoke-Checked git @('clone', '--shared', '--no-checkout', $seed, $sourceRoot)
Invoke-Checked git @('-C', $sourceRoot, 'config', 'core.longpaths', 'true')
Invoke-Checked git @('-C', $sourceRoot, 'config', 'core.autocrlf', 'false')
Invoke-Checked git @('-C', $sourceRoot, 'config', 'core.eol', 'lf')
Invoke-Checked git @('-C', $sourceRoot, 'checkout', '--detach', $baseline.commit)
# Git 自动换行配置不能改变源码/PDB 身份；应用输入同样规范为 LF。
$normalizedPatch = Join-Path $buildRoot 'layout-ownership.patch'
[IO.File]::WriteAllText($normalizedPatch, [IO.File]::ReadAllText((Join-Path $patchRoot 'layout-ownership.patch')).Replace("`r`n", "`n"))
Invoke-Checked git @('-C', $sourceRoot, 'apply', '--check', $normalizedPatch)
Invoke-Checked git @('-C', $sourceRoot, 'apply', $normalizedPatch)
@{ sdk = @{ version = $baseline.sdkVersion; rollForward = 'disable'; allowPrerelease = $false } } |
    ConvertTo-Json | Set-Content (Join-Path $sourceRoot 'global.json') -Encoding utf8NoBOM
Copy-Item (Join-Path $patchRoot 'runtime-build.targets') $sourceRoot
Copy-Item (Join-Path $patchRoot 'restore/*') $sourceRoot -Recurse -Force
$nugetConfig = Join-Path $buildRoot 'nuget.config'
'<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>' |
    Set-Content $nugetConfig -Encoding utf8NoBOM
$properties = @('-p:AvsSkipBuildingLegacyTargetFrameworks=True', '-p:PublicSign=true',
    "-p:DirectoryBuildTargetsPath=$sourceRoot/runtime-build.targets", '-p:ContinuousIntegrationBuild=true',
    "-p:AssemblyVersion=$($baseline.assemblyVersion)", '-p:FileVersion=12.1.2.1',
    "-p:InformationalVersion=$($baseline.informationalVersion)", '-p:IncludeSourceRevisionInInformationalVersion=false',
    '-p:DisableSourceLink=true', '-p:RestorePackagesWithLockFile=true', '-p:RestoreLockedMode=true',
    '-p:Copyright=Copyright 2013-2026 © The AvaloniaUI Project',
    "-p:PathMap=$sourceRoot=/_/avalonia", "-p:RestoreConfigFile=$nugetConfig")
if ($IsolatedRestore) { $properties += "-p:RestorePackagesPath=$buildRoot/packages" }
Push-Location $sourceRoot
try { Invoke-Checked dotnet (@('build', 'src/Avalonia.Base/Avalonia.Base.csproj', '-c', 'Release', '-f', 'net10.0', '-m:1') + $properties) }
finally { Pop-Location }
$output = Join-Path $sourceRoot 'src/Avalonia.Base/bin/Release/net10.0'
$dllPath = Join-Path $output 'Avalonia.Base.dll'
$dllHash = Assert-Hash $dllPath $baseline.dllSha256
$pdbHash = Assert-Hash (Join-Path $output 'Avalonia.Base.pdb') $baseline.pdbSha256
$assembly = [Reflection.AssemblyName]::GetAssemblyName($dllPath)
if ($assembly.Version.ToString() -ne $baseline.assemblyVersion -or
    [Convert]::ToHexString($assembly.GetPublicKeyToken()).ToLowerInvariant() -ne $baseline.publicKeyToken) {
    throw '布局补丁改变了程序集绑定身份。'
}
$validationRoot = Join-Path $buildRoot 'validation'
New-Item -ItemType Directory -Force $validationRoot | Out-Null
Copy-Item (Join-Path $patchRoot 'validation/*') $validationRoot
'<Project />' | Set-Content (Join-Path $validationRoot 'Directory.Build.props') -Encoding utf8NoBOM
'<Project />' | Set-Content (Join-Path $validationRoot 'Directory.Build.targets') -Encoding utf8NoBOM
$validationProperties = @("-p:CandidateRuntime=$dllPath", "-p:RepositoryRoot=$repositoryRoot", "-p:RestoreConfigFile=$nugetConfig", '-p:RestoreLockedMode=true')
if ($IsolatedRestore) { $validationProperties += "-p:RestorePackagesPath=$buildRoot/packages" }
Invoke-Checked dotnet (@('test', (Join-Path $validationRoot 'LayoutPatch.Validation.csproj'), '-c', 'Release', '-m:1',
    '--logger', 'trx;LogFileName=layout-patch.trx', '--results-directory', (Join-Path $buildRoot 'results')) + $validationProperties)
[xml]$trx = Get-Content (Join-Path $buildRoot 'results/layout-patch.trx') -Raw
$counters = $trx.TestRun.ResultSummary.Counters
if ([int]$counters.passed -lt $baseline.minimumTests -or [int]$counters.failed -ne 0 -or
    [int]$counters.total -ne [int]$counters.passed) { throw '布局补丁测试为空、失败或存在跳过。' }
# 验证测试进程实际加载的输出文件，避免运行了原始 NuGet DLL 却写下绿色收据。
$null = Assert-Hash (Join-Path $validationRoot 'bin/Release/net10.0/Avalonia.Base.dll') $dllHash
New-Item -ItemType Directory -Force $runtimeRoot | Out-Null
Copy-Item (Join-Path $output 'Avalonia.Base.dll'), (Join-Path $output 'Avalonia.Base.pdb') $runtimeRoot
"<Project><PropertyGroup><MyAvaloniaLayoutPatchSha256>$dllHash</MyAvaloniaLayoutPatchSha256></PropertyGroup></Project>" |
    Set-Content (Join-Path $runtimeRoot 'identity.props') -Encoding utf8NoBOM
[ordered]@{ identity=$baseline.identity; fingerprint=$fingerprint; commit=$baseline.commit;
    sdk=$baseline.sdkVersion; publicSigned=$true; assembly=$assembly.FullName; dllSha256=$dllHash; pdbSha256=$pdbHash;
    buildRoot=$buildRoot; isolatedRestore=[bool]$IsolatedRestore; passed=[int]$counters.passed; failed=0; skipped=0;
    testTrx=(Join-Path $buildRoot 'results/layout-patch.trx'); testSha256=(Get-FileHash (Join-Path $buildRoot 'results/layout-patch.trx')).Hash } |
    ConvertTo-Json | Set-Content $receiptPath -Encoding utf8NoBOM
Copy-Item $receiptPath (Join-Path $buildRoot 'receipt.json')
Write-Output "已构建并验证 $($baseline.identity)：$dllHash。"
