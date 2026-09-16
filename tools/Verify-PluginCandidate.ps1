param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+-[a-z0-9.-]+$')][string]$CandidateVersion,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw '候选证据目录必须是新目录，避免覆盖已有实验。' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$feed = Join-Path $output 'feed'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('MyAvaloniaCandidate-' + [Guid]::NewGuid().ToString('N'))
$workspace = Join-Path $temporary 'workspace'
$packages = Join-Path $temporary 'packages'
$hive = Join-Path $temporary 'template-hive'
New-Item -ItemType Directory -Path $feed, $workspace, $packages -Force | Out-Null
$originalPackages = $env:NUGET_PACKAGES
$passed = $false

# 所有六包共享同一实验标识；只覆盖当前命令属性，不改集中正式版本。
# 插件源码、模板安装 hive 和 NuGet 缓存位于独立临时根，防止同号包污染用户常用缓存。
# 保留临时目录与所有日志用于复核，不在此流程发布、安装到 Host 或做发布重复性测试。
function Invoke-CandidateDotnet([string[]]$arguments, [string]$log) {
    & dotnet @arguments *> (Join-Path $output ($log + '.log'))
    if ($LASTEXITCODE -ne 0) { throw "候选验证失败：$log；见日志。" }
}

Push-Location $repo
try {
    $names = @('MyAvaloniaManagement.PluginSdk', 'MyAvaloniaManagement.PluginSdk.UI', 'MyAvaloniaManagement.PluginSdk.Workflow',
        'MyAvaloniaManagement.Icons', 'MyAvaloniaManagement.Plugin.Build', 'MyAvaloniaManagement.Plugin.Templates')
    foreach ($name in $names) {
        $published = Invoke-RestMethod ('https://api.nuget.org/v3-flatcontainer/' + $name.ToLowerInvariant() + '/index.json')
        if ($published.versions -contains $CandidateVersion) { throw "候选标识已公开占用：$name $CandidateVersion" }
        $area = if ($name -in @('MyAvaloniaManagement.Plugin.Build', 'MyAvaloniaManagement.Plugin.Templates')) { 'Packaging' } else { 'Host' }
        Invoke-CandidateDotnet @('pack', "$area/$name/$name.csproj", '-c', 'Release', '-o', $feed, '-m:1', '-warnaserror',
            "-p:MyAvaloniaPackageVersion=$CandidateVersion", '-p:MyAvaloniaPackageFileVersion=3.4.1.0') ('pack-' + $name)
    }
    $env:NUGET_PACKAGES = $packages
    Invoke-CandidateDotnet @('new', 'install', (Join-Path $feed "MyAvaloniaManagement.Plugin.Templates.$CandidateVersion.nupkg"),
        '--debug:custom-hive', $hive) 'template-install'
    Invoke-CandidateDotnet @('new', 'myavalonia-plugin', '-n', 'V10Candidate', '--plugin-id', 'myavalonia.plugin.v10-candidate',
        '-o', $workspace, '--debug:custom-hive', $hive) 'template-create'
    # 正式模板仍声明当前公共稳定版；实验只在生成副本中选中统一候选，重新生成其锁文件。
    $versionsPath = Join-Path $workspace 'Directory.Packages.props'
    [xml]$versions = Get-Content $versionsPath -Raw
    foreach ($item in $versions.Project.ItemGroup.PackageVersion) {
        if ($item.Include.StartsWith('MyAvaloniaManagement.')) { $item.Version = "[$CandidateVersion]" }
    }
    $versions.Save($versionsPath)
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    "<configuration><packageSources><clear/><add key='candidate' value='$escapedFeed'/><add key='nuget.org' value='https://api.nuget.org/v3/index.json'/></packageSources></configuration>" |
        Set-Content (Join-Path $workspace 'NuGet.Config') -Encoding utf8
    Set-Location $workspace
    Invoke-CandidateDotnet @('restore', 'V10Candidate.slnx', '--force-evaluate') 'consumer-restore'
    Invoke-CandidateDotnet @('restore', 'V10Candidate.slnx', '--locked-mode') 'consumer-locked-restore'
    Invoke-CandidateDotnet @('build', 'V10Candidate.slnx', '-c', 'Release', '--no-restore', '-warnaserror', '-m:1') 'consumer-build'
    Invoke-CandidateDotnet @('test', 'tests/V10Candidate.Tests', '-c', 'Release', '--no-build', '--logger', 'trx;LogFileName=candidate.trx', '--results-directory', $output) 'consumer-tests'
    [xml]$trx = Get-Content (Join-Path $output 'candidate.trx') -Raw
    $counts = $trx.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counts -or [int]$counts.total -le 0 -or [int]$counts.passed -ne [int]$counts.total -or
        [int]$counts.failed -ne 0 -or [int]$counts.notExecuted -ne 0) { throw '候选模板测试没有完整执行通过。' }
    $project = 'src/V10Candidate.Plugin/V10Candidate.Plugin.csproj'
    $metadata = Join-Path $workspace 'src/V10Candidate.Plugin/bin/Release/net10.0/plugin.build.json'
    $before = (Get-FileHash $metadata).Hash
    Invoke-CandidateDotnet @('build', $project, '-c', 'Release', '--no-restore', '-warnaserror', '-m:1') 'consumer-metadata-stability'
    if ($before -ne (Get-FileHash $metadata).Hash) { throw '相同输入生成了不同构建信息。' }
    $info = Get-Content $metadata -Raw | ConvertFrom-Json
    if ($info.buildVersion -ne $CandidateVersion) { throw '生成项目没有使用候选 Build。' }
    if ((Get-Content $metadata -Raw).Contains($temporary)) { throw '构建信息包含个人绝对路径。' }
    Invoke-CandidateDotnet @('msbuild', $project, '-t:BuildManagedPluginPackage', '-p:Configuration=Release',
        ('-p:ManagedPluginPackageOutput=' + (Join-Path $output 'plugin'))) 'consumer-package'
    $zip = @(Get-ChildItem (Join-Path $output 'plugin') -Filter '*.zip')
    if ($zip.Count -ne 1) { throw '没有唯一真实 ZIP。' }
    [IO.Compression.ZipFile]::ExtractToDirectory($zip[0].FullName, (Join-Path $output 'extracted'))
    $metadataFiles = @(Get-ChildItem (Join-Path $output 'extracted') -Recurse -Filter 'plugin.build.json')
    if ($metadataFiles.Count -ne 1) { throw 'ZIP 没有唯一构建信息。' }
    Copy-Item -LiteralPath $metadataFiles[0].FullName -Destination (Join-Path $output 'plugin.build.json')
    $passed = $true
}
finally {
    $env:NUGET_PACKAGES = $originalPackages
    Pop-Location
    [ordered]@{ passed = $passed; candidateVersion = $CandidateVersion; isolatedWorkspace = $workspace;
        feed = $feed; packages = @(Get-ChildItem $feed -Filter '*.nupkg' | ForEach-Object {
            [ordered]@{ name = $_.Name; sha256 = (Get-FileHash $_.FullName).Hash }
        }) } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'summary.json') -Encoding utf8
}
Write-Host "本地候选消费通过：$output"
