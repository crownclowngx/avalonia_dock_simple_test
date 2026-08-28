Set-StrictMode -Version Latest

function Assert-PluginV3True {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function New-PluginV3ResultRoot {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $RelativePath
    )

    $repository = [IO.Path]::GetFullPath($RepositoryRoot)
    $result = [IO.Path]::GetFullPath((Join-Path $repository $RelativePath))
    $repositoryPrefix = $repository.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    Assert-PluginV3True `
        ($result.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) `
        "验收结果目录不在仓库内：$result。"

    # 只清理由调用方给出的、已经完成绝对路径归属校验的阶段目录。共享核心不接受通配符，
    # 也不会触碰整个 artifacts 根，从而保证三个插件的证据可以彼此独立保留。
    if (Test-Path -LiteralPath $result) {
        Remove-Item -LiteralPath $result -Recurse -Force
    }
    New-Item -ItemType Directory -Path $result | Out-Null
    return $result
}

function Invoke-PluginV3DotNet {
    param([Parameter(Mandatory)] [string[]] $Arguments)

    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
    }
}

function Invoke-PluginV3TestSuite {
    param(
        [Parameter(Mandatory)] $Suite,
        [Parameter(Mandatory)] [string] $ResultRoot,
        [Parameter(Mandatory)] [string] $Configuration,
        [bool] $NoRestore = $false
    )

    $suiteDirectory = Join-Path $ResultRoot $Suite.Name
    New-Item -ItemType Directory -Path $suiteDirectory | Out-Null
    $arguments = @(
        'test', [string]$Suite.Project,
        '-c', $Configuration,
        '-p:SkipPluginDeploy=true',
        '-p:TreatWarningsAsErrors=true',
        '--results-directory', $suiteDirectory,
        '--logger', "trx;LogFileName=$($Suite.Name).trx",
        '--logger', 'console;verbosity=minimal'
    )
    if ($Suite.PSObject.Properties.Name -contains 'Filter' -and
        -not [string]::IsNullOrWhiteSpace([string]$Suite.Filter)) {
        $arguments += @('--filter', [string]$Suite.Filter)
    }
    if ($Suite.PSObject.Properties.Name -contains 'Settings' -and
        -not [string]::IsNullOrWhiteSpace([string]$Suite.Settings)) {
        $arguments += @('--settings', [string]$Suite.Settings)
    }
    if ($Suite.PSObject.Properties.Name -contains 'CollectCoverage' -and
        [bool]$Suite.CollectCoverage) {
        $arguments += '--collect:XPlat Code Coverage'
    }
    if ($NoRestore) {
        $arguments += '--no-restore'
    }
    Invoke-PluginV3DotNet $arguments

    $trxFiles = @(Get-ChildItem -LiteralPath $suiteDirectory -Recurse `
        -Filter "$($Suite.Name).trx" -File)
    Assert-PluginV3True ($trxFiles.Count -eq 1) `
        "$($Suite.Name) 没有生成唯一 TRX。"
    [xml]$trx = Get-Content -LiteralPath $trxFiles[0].FullName
    $counters = $trx.TestRun.ResultSummary.Counters
    Assert-PluginV3True (
        [int]$counters.failed -eq 0 -and
        [int]$counters.notExecuted -eq 0 -and
        [int]$counters.executed -eq [int]$counters.passed) `
        "$($Suite.Name) 未做到全部执行、零失败、零跳过。"
    $passed = [int]$counters.passed
    Assert-PluginV3True ($passed -gt 0) "$($Suite.Name) 没有实际执行测试。"

    $coveragePath = $null
    if ($Suite.PSObject.Properties.Name -contains 'CollectCoverage' -and
        [bool]$Suite.CollectCoverage) {
        # Collector 会把附件复制到 TRX 的 In 目录；只保留原始报告，避免合并同一份数据两次。
        $coverageFiles = @(Get-ChildItem -LiteralPath $suiteDirectory -Recurse `
            -Filter 'coverage.cobertura.xml' -File | Where-Object {
                $_.FullName -notmatch '[\\/]In[\\/]'
            })
        Assert-PluginV3True ($coverageFiles.Count -eq 1) `
            "$($Suite.Name) 没有生成唯一 coverage.cobertura.xml。"
        $coveragePath = $coverageFiles[0].FullName
    }

    return [pscustomobject]@{
        Passed = $passed
        TrxPath = $trxFiles[0].FullName
        CoveragePath = $coveragePath
    }
}

function Assert-PluginV3RgAbsent {
    param(
        [Parameter(Mandatory)] [string] $Pattern,
        [Parameter(Mandatory)] [string[]] $Paths,
        [string[]] $Globs = @('*.cs', '*.csproj'),
        [Parameter(Mandatory)] [string] $Message
    )

    $arguments = @('--quiet', $Pattern) + $Paths
    foreach ($glob in $Globs) {
        $arguments += @('-g', $glob)
    }
    & rg @arguments
    if ($LASTEXITCODE -eq 0) {
        throw $Message
    }
    if ($LASTEXITCODE -gt 1) {
        throw "无法执行 V3 结构扫描：$Pattern。"
    }
}

function Merge-PluginV3Coverage {
    param(
        [Parameter(Mandatory)] [string[]] $Reports,
        [Parameter(Mandatory)] [string] $TargetDirectory,
        [Parameter(Mandatory)] [string] $AssemblyFilter,
        [string] $FileFilter = '-*/obj/*;-*.g.cs;-*.g.i.cs'
    )

    Assert-PluginV3True ($Reports.Count -gt 0) '没有可合并的覆盖率报告。'
    Invoke-PluginV3DotNet @(
        'reportgenerator', "-reports:$($Reports -join ';')",
        "-targetdir:$TargetDirectory", '-reporttypes:Cobertura;JsonSummary',
        "-assemblyfilters:$AssemblyFilter", "-filefilters:$FileFilter")
    $path = Join-Path $TargetDirectory 'Cobertura.xml'
    Assert-PluginV3True (Test-Path -LiteralPath $path -PathType Leaf) `
        "覆盖率合并结果不存在：$path。"
    [xml]$coverage = Get-Content -LiteralPath $path
    return [pscustomobject]@{
        Path = $path
        Xml = $coverage
        Line = [Math]::Round(
            100 * [double]$coverage.DocumentElement.GetAttribute('line-rate'), 2)
        Branch = [Math]::Round(
            100 * [double]$coverage.DocumentElement.GetAttribute('branch-rate'), 2)
    }
}

function Get-PluginV3FileLineCoverage {
    param(
        [Parameter(Mandatory)] [object[]] $Classes,
        [Parameter(Mandatory)] [string] $RelativePath
    )

    $matching = @($Classes | Where-Object {
        $_.filename.Replace('\', '/').EndsWith(
            $RelativePath, [StringComparison]::OrdinalIgnoreCase)
    })
    Assert-PluginV3True ($matching.Count -gt 0) "覆盖率缺少关键文件：$RelativePath。"
    $lines = @($matching | ForEach-Object { $_.lines.line } |
        Group-Object number | ForEach-Object {
            [pscustomobject]@{
                Covered = @($_.Group | Where-Object { [int]$_.hits -gt 0 }).Count -gt 0
            }
        })
    Assert-PluginV3True ($lines.Count -gt 0) "关键文件没有可执行行：$RelativePath。"
    return [Math]::Round(100 * @($lines | Where-Object Covered).Count / $lines.Count, 2)
}

function New-PluginV3PackageEvidence {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $ScriptsRoot,
        [Parameter(Mandatory)] [string] $ResultRoot,
        [Parameter(Mandatory)] [string] $ProjectPath,
        [Parameter(Mandatory)] [string] $PackagePrefix,
        [Parameter(Mandatory)] [string] $Configuration
    )

    $firstRoot = Join-Path $ResultRoot 'package-first'
    $secondRoot = Join-Path $ResultRoot 'package-second'
    $builder = Join-Path $ScriptsRoot 'Build-ManagedPluginPackage.ps1'
    & $builder -Project $ProjectPath -Configuration $Configuration -OutputDirectory $firstRoot
    if ($LASTEXITCODE -ne 0) {
        throw '第一次隔离测试 ZIP 构建失败。'
    }
    & $builder -Project $ProjectPath -Configuration $Configuration -OutputDirectory $secondRoot
    if ($LASTEXITCODE -ne 0) {
        throw '第二次隔离测试 ZIP 构建失败。'
    }

    $sidecars = @(Get-ChildItem -LiteralPath $firstRoot `
        -Filter "$PackagePrefix-*-win-x64.manifest.json" -File)
    Assert-PluginV3True ($sidecars.Count -eq 1) `
        "预期唯一机器清单，实际为 $($sidecars.Count) 份。"
    $baseName = $sidecars[0].Name -replace '\.manifest\.json$', ''
    $secondPath = Join-Path $secondRoot "$baseName.manifest.json"
    Assert-PluginV3True (Test-Path -LiteralPath $secondPath -PathType Leaf) `
        '第二次隔离构建缺少对应机器清单。'
    $first = Get-Content -Raw -LiteralPath $sidecars[0].FullName | ConvertFrom-Json
    $second = Get-Content -Raw -LiteralPath $secondPath | ConvertFrom-Json
    Assert-PluginV3True ($first.archive.sha256 -eq $second.archive.sha256) `
        '两次隔离测试 ZIP 的归档摘要不一致。'
    $firstFiles = @($first.files | ForEach-Object { "$($_.path)|$($_.length)|$($_.sha256)" })
    $secondFiles = @($second.files | ForEach-Object { "$($_.path)|$($_.length)|$($_.sha256)" })
    Assert-PluginV3True (-not (Compare-Object $firstFiles $secondFiles)) `
        '两次隔离测试 ZIP 的逐文件事实不一致。'

    $forbidden = @($first.files.path | Where-Object {
        $_ -match '(^|/)(?:MyAvaloniaManagement(?:Common|\.PluginSdk(?:\.UI)?)?|Avalonia(?:\.|$)|Dock\.|Newtonsoft\.Json|Microsoft\.Extensions\.).*\.dll$'
    })
    Assert-PluginV3True ($forbidden.Count -eq 0) `
        "测试 ZIP 混入宿主共享程序集：$($forbidden -join ', ')"

    $loadRoot = Join-Path $ResultRoot 'package-load'
    Expand-Archive -LiteralPath (Join-Path $firstRoot "$baseName.zip") -DestinationPath $loadRoot
    return [pscustomobject]@{
        BaseName = $baseName
        FirstSidecar = $first
        LoadRoot = $loadRoot
        ArchiveSha256 = [string]$first.archive.sha256
        FileCount = @($first.files).Count
    }
}

function Assert-PluginV3Manifest {
    param(
        [Parameter(Mandatory)] $Manifest,
        [Parameter(Mandatory)] [string] $PluginId,
        [string] $PluginVersion = '3.0.0',
        [string] $SdkMinInclusive = '3.3.0'
    )

    Assert-PluginV3True (
        [int]$Manifest.schemaVersion -eq 2 -and
        $Manifest.pluginId -ceq $PluginId -and
        $Manifest.pluginVersion -ceq $PluginVersion -and
        $Manifest.sdk.minInclusive -ceq $SdkMinInclusive -and
        $Manifest.sdk.maxExclusive -ceq '4.0.0') `
        '测试 ZIP 的 manifest schema、身份、版本或 V3 SDK 区间不正确。'
}

function Write-PluginV3Json {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] $Value,
        [int] $Depth = 8
    )

    [IO.File]::WriteAllText(
        $Path,
        ($Value | ConvertTo-Json -Depth $Depth),
        [Text.UTF8Encoding]::new($false))
}

Export-ModuleMember -Function @(
    'Assert-PluginV3True',
    'New-PluginV3ResultRoot',
    'Invoke-PluginV3DotNet',
    'Invoke-PluginV3TestSuite',
    'Assert-PluginV3RgAbsent',
    'Merge-PluginV3Coverage',
    'Get-PluginV3FileLineCoverage',
    'New-PluginV3PackageEvidence',
    'Assert-PluginV3Manifest',
    'Write-PluginV3Json')
