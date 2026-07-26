param(
    [string]$EvidenceRoot = '',
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $workspace 'TestResults\Upgrade'
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null

$expected = [ordered]@{
    Avalonia = '12.1.0'
    'Semi.Avalonia' = '12.1.0'
    'Irihi.Ursa' = '2.1.0'
    'Irihi.Ursa.Themes.Semi' = '2.1.0'
    'Dock.Avalonia' = '12.0.0.2'
    'Xaml.Behaviors' = '12.0.5'
    'StaticViewLocator' = '0.4.0'
    'LibVLCSharp' = '3.10.0'
    'LibVLCSharp.Avalonia' = '3.10.0'
    'VideoLAN.LibVLC.Windows' = '3.0.23.1'
}
$forbiddenIds = @(
    'Avalonia.Controls.TreeDataGrid'
    'Avalonia.Diagnostics'
    'Avalonia.Xaml.Interactions'
    'Dock.Avalonia.Diagnostics'
    'EmberDock.Settings'
)
$forbiddenDirectIds = @($forbiddenIds) + 'Dock.Settings'

[xml]$central = Get-Content -Raw -LiteralPath (
    Join-Path $workspace 'Directory.Packages.props')
$centralVersions = @{}
foreach ($node in $central.Project.ItemGroup.PackageVersion) {
    $centralVersions[[string]$node.Include] = [string]$node.Version
}
foreach ($pair in $expected.GetEnumerator()) {
    if ($centralVersions[$pair.Key] -ne $pair.Value) {
        throw "中央版本不匹配：$($pair.Key)，期望 $($pair.Value)，实际 $($centralVersions[$pair.Key])。"
    }
}
foreach ($id in $forbiddenDirectIds) {
    if ($centralVersions.ContainsKey($id)) {
        throw "中央版本仍声明已禁止的包：$id"
    }
}

$projectFiles = Get-ChildItem -LiteralPath $workspace -Recurse -Filter '*.csproj' -File |
    Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }
foreach ($projectFile in $projectFiles) {
    [xml]$project = Get-Content -Raw -LiteralPath $projectFile.FullName
    foreach ($reference in @($project.Project.ItemGroup.PackageReference)) {
        if ([string]$reference.Include -in $forbiddenDirectIds) {
            throw "项目仍直接引用已禁止的包：$($projectFile.FullName) -> $($reference.Include)"
        }
    }
}

if (-not $SkipRestore) {
    & dotnet restore (Join-Path $workspace 'MyAvaloniaManagement.sln') --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw "locked restore 失败，退出码 $LASTEXITCODE。"
    }
}

$resolved = @{}
$lockFiles = Get-ChildItem -LiteralPath $workspace -Recurse -Filter 'packages.lock.json' -File |
    Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }
foreach ($lockFile in $lockFiles) {
    $lock = Get-Content -Raw -LiteralPath $lockFile.FullName | ConvertFrom-Json
    foreach ($framework in $lock.dependencies.PSObject.Properties) {
        foreach ($package in $framework.Value.PSObject.Properties) {
            $id = [string]$package.Name
            $version = [string]$package.Value.resolved
            if (-not $resolved.ContainsKey($id)) {
                $resolved[$id] = [Collections.Generic.HashSet[string]]::new(
                    [StringComparer]::OrdinalIgnoreCase)
            }
            [void]$resolved[$id].Add($version)
        }
    }
}

foreach ($id in $forbiddenIds) {
    if ($resolved.ContainsKey($id)) {
        throw "lock file 仍解析到已禁止的包：$id"
    }
}
foreach ($package in $resolved.GetEnumerator()) {
    # Avalonia 12.1.0 官方包自身仍声明构建期 Avalonia.BuildServices 11.3.2；
    # 它不进入运行时公共类型图，因此只对这一精确构建工具例外。
    if ($package.Key -ne 'Avalonia.BuildServices' -and
        $package.Key.StartsWith('Avalonia', [StringComparison]::OrdinalIgnoreCase) -and
        @($package.Value | Where-Object { $_.StartsWith('11.') }).Count -gt 0) {
        throw "依赖图混入 Avalonia 11：$($package.Key) -> $($package.Value -join ', ')"
    }
    if ($package.Key.StartsWith('Dock.', [StringComparison]::OrdinalIgnoreCase) -and
        @($package.Value | Where-Object { $_.StartsWith('11.') }).Count -gt 0) {
        throw "依赖图混入 Dock 11：$($package.Key) -> $($package.Value -join ', ')"
    }
}

$graphPath = Join-Path $EvidenceRoot 'package-graph.txt'
$graphOutput = @(
    & dotnet list (Join-Path $workspace 'MyAvaloniaManagement.sln') package --include-transitive
)
if ($LASTEXITCODE -ne 0) {
    throw "依赖图导出失败，退出码 $LASTEXITCODE。"
}
[IO.File]::WriteAllLines($graphPath, $graphOutput, [Text.UTF8Encoding]::new($false))

$summary = [ordered]@{
    schemaVersion = 1
    kind = 'net10-avalonia12-dock12-package-graph'
    sourceRevision = (& git -C $workspace rev-parse HEAD).Trim()
    generatedUtc = [DateTime]::UtcNow.ToString('O')
    lockFileCount = $lockFiles.Count
    projectCount = $projectFiles.Count
    resolvedPackageCount = $resolved.Count
    forbiddenResolvedPackages = $forbiddenIds
    forbiddenDirectPackages = $forbiddenDirectIds
    documentedTransitiveExceptions = @(
        'Avalonia.BuildServices 11.3.2 (declared by Avalonia 12.1.0)'
        'Dock.Settings 12.0.0.2 (declared by Dock.Avalonia 12.0.0.2)'
    )
    expectedVersions = $expected
    passed = $true
}
$summaryPath = Join-Path $EvidenceRoot 'package-graph-summary.json'
[IO.File]::WriteAllText(
    $summaryPath,
    (($summary | ConvertTo-Json -Depth 8) -replace "`r`n", "`n") + "`n",
    [Text.UTF8Encoding]::new($false))
Write-Host "[依赖图] 通过：$summaryPath"
