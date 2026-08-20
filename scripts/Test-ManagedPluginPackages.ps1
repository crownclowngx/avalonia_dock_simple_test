param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$builder = Join-Path $PSScriptRoot 'Build-ManagedPluginPackage.ps1'
$resultsRoot = if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    Join-Path $repositoryRoot 'artifacts\test-results\ManagedPluginPackages'
}
elseif ([IO.Path]::IsPathRooted($ResultsDirectory)) {
    [IO.Path]::GetFullPath($ResultsDirectory)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $ResultsDirectory))
}

function Read-ProjectProperties {
    param([Parameter(Mandatory)] [string]$ProjectPath)

    [xml]$document = Get-Content -Raw -LiteralPath $ProjectPath
    $result = @{}
    foreach ($element in @($document.Project.PropertyGroup.ChildNodes |
            Where-Object NodeType -eq ([Xml.XmlNodeType]::Element))) {
        $result[$element.LocalName] = [string]$element.InnerText.Trim()
    }
    return $result
}

function Write-JsonUtf8 {
    param([Parameter(Mandatory)] [string]$Path, [Parameter(Mandatory)] $Value)

    [IO.File]::WriteAllText(
        $Path,
        ($Value | ConvertTo-Json -Depth 16),
        [Text.UTF8Encoding]::new($false))
}

function Invoke-DotNetChecked {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    # 显式送到 Host，防止调用方接收函数返回值时把 dotnet 日志误当成结构化结果。
    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$Name 失败，退出码 $LASTEXITCODE。"
    }
}

function Test-ManagedPluginBuildContract {
    param([Parameter(Mandatory)] [string]$FixtureRoot)

    # 负例使用不带业务依赖的最小 SDK 项目。这样失败只能来自 Managed Plugin 协议，
    # 不会被四个真实插件的 NuGet 或业务代码偶然遮蔽；夹具始终位于本轮系统 Temp 下。
    New-Item -ItemType Directory -Path $FixtureRoot | Out-Null
    $propsPath = (Join-Path $repositoryRoot 'build\MyAvaloniaManagement.ManagedPlugin.props')
    $targetsPath = (Join-Path $repositoryRoot 'build\MyAvaloniaManagement.ManagedPlugin.targets')
    $fixtureProject = Join-Path $FixtureRoot 'ContractPlugin.csproj'
    $fixtureSource = Join-Path $FixtureRoot 'ContractPlugin.cs'
    $assetA = Join-Path $FixtureRoot 'asset-a.bin'
    $assetB = Join-Path $FixtureRoot 'asset-b.bin'
    [IO.File]::WriteAllText($fixtureSource, 'public sealed class ContractPlugin { }')
    [IO.File]::WriteAllText($assetA, 'a')
    [IO.File]::WriteAllText($assetB, 'b')

    $projectText = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ManagedPlugin>true</ManagedPlugin>
    <ManagedPluginId Condition="'`$(ContractMutation)' != 'missing-id'">myavalonia.plugin.contract-fixture</ManagedPluginId>
    <ManagedPluginId Condition="'`$(ContractMutation)' == 'invalid-id'">Contract Fixture</ManagedPluginId>
    <ManagedPluginDirectoryName Condition="'`$(ContractMutation)' != 'invalid-directory'">ContractFixture</ManagedPluginDirectoryName>
    <ManagedPluginDirectoryName Condition="'`$(ContractMutation)' == 'invalid-directory'">..\escape</ManagedPluginDirectoryName>
    <PluginVersion Condition="'`$(ContractMutation)' != 'missing-version'">1.0.0</PluginVersion>
    <ManagedPluginRuntimeIdentifier Condition="'`$(ContractMutation)' == 'invalid-rid'">linux-x64</ManagedPluginRuntimeIdentifier>
    <ManagedPluginHostApiMinInclusive Condition="'`$(ContractMutation)' != 'missing-range'">1.0.0</ManagedPluginHostApiMinInclusive>
    <ManagedPluginHostApiMaxExclusive Condition="'`$(ContractMutation)' != 'reversed-range'">2.0.0</ManagedPluginHostApiMaxExclusive>
    <ManagedPluginHostApiMaxExclusive Condition="'`$(ContractMutation)' == 'reversed-range'">1.0.0</ManagedPluginHostApiMaxExclusive>
    <ManagedPluginCommonContractMinInclusive>1.0.0</ManagedPluginCommonContractMinInclusive>
    <ManagedPluginCommonContractMaxExclusive Condition="'`$(ContractMutation)' != 'missing-range'">2.0.0</ManagedPluginCommonContractMaxExclusive>
    <ManagedPluginAssetDirectoryRelativePath Condition="'`$(ContractMutation)' == 'missing-directory-asset'">missing-tree</ManagedPluginAssetDirectoryRelativePath>
  </PropertyGroup>
  <Import Project="$propsPath" />
  <ItemGroup>
    <ManagedPluginAsset Include="$FixtureRoot\missing.bin" TargetPath="private\missing.bin" Condition="'`$(ContractMutation)' == 'missing-asset'" />
    <ManagedPluginAsset Include="$assetA" TargetPath="..\escape.bin" Condition="'`$(ContractMutation)' == 'escape-path'" />
    <ManagedPluginAsset Include="$assetA" TargetPath="private/same.bin" Condition="'`$(ContractMutation)' == 'duplicate-path'" />
    <ManagedPluginAsset Include="$assetB" TargetPath="private\same.bin" Condition="'`$(ContractMutation)' == 'duplicate-path'" />
    <ManagedPluginAsset Include="$assetA" TargetPath="Avalonia.Base.dll" Condition="'`$(ContractMutation)' == 'shared-assembly'" />
    <ManagedPluginAsset Include="$assetA" TargetPath="runtimes\linux-x64\native\fixture.dll" Condition="'`$(ContractMutation)' == 'foreign-rid'" />
  </ItemGroup>
  <Target Name="MutateRequiredOutput" BeforeTargets="DeployManagedPlugin">
    <Delete Files="`$(TargetPath)" Condition="'`$(ContractMutation)' == 'missing-dll'" />
    <Delete Files="`$(TargetDir)`$(AssemblyName).deps.json" Condition="'`$(ContractMutation)' == 'missing-deps'" />
    <Delete Files="`$(TargetDir)`$(AssemblyName).pdb" Condition="'`$(ContractMutation)' == 'missing-pdb'" />
  </Target>
  <Import Project="$targetsPath" />
</Project>
"@
    [IO.File]::WriteAllText($fixtureProject, $projectText, [Text.UTF8Encoding]::new($false))

    $deployRoot = Join-Path $FixtureRoot 'deploy'
    Invoke-DotNetChecked '构建契约夹具还原' @('restore', $fixtureProject, '--nologo')

    # 正例同时证明只清理当前插件目录：预置兄弟插件必须保留，当前目录陈旧文件必须移除。
    $siblingSentinel = Join-Path $deployRoot 'SiblingPlugin\keep.txt'
    $staleCurrent = Join-Path $deployRoot 'ContractFixture\stale.txt'
    New-Item -ItemType Directory -Path (Split-Path $siblingSentinel), (Split-Path $staleCurrent) -Force | Out-Null
    [IO.File]::WriteAllText($siblingSentinel, 'keep')
    [IO.File]::WriteAllText($staleCurrent, 'stale')
    Invoke-DotNetChecked '构建契约正例' @(
        'build', $fixtureProject, '-c', $Configuration, '--no-restore', '--nologo',
        "-p:ManagedPluginDeployRoot=$deployRoot", '-p:SkipPluginDeploy=false')
    if (-not (Test-Path -LiteralPath $siblingSentinel) -or (Test-Path -LiteralPath $staleCurrent)) {
        throw 'Managed Plugin 部署没有遵守“只清理当前插件目录”的边界。'
    }

    # SkipPluginDeploy 必须是硬开关：即使构建成功，也不能创建目标插件目录。
    $skipRoot = Join-Path $FixtureRoot 'skip-deploy'
    Invoke-DotNetChecked 'SkipPluginDeploy 正例' @(
        'build', $fixtureProject, '-c', $Configuration, '--no-restore', '--nologo',
        "-p:ManagedPluginDeployRoot=$skipRoot", '-p:SkipPluginDeploy=true')
    if (Test-Path -LiteralPath (Join-Path $skipRoot 'ContractFixture')) {
        throw 'SkipPluginDeploy=true 仍写入了插件部署目录。'
    }

    $negativeCases = @(
        @{ mutation = 'missing-id'; message = '缺少 ManagedPluginId' },
        @{ mutation = 'invalid-id'; message = '规范小写稳定 ID' },
        @{ mutation = 'missing-version'; message = 'PluginVersion 必须' },
        @{ mutation = 'invalid-directory'; message = 'ManagedPluginDirectoryName 只能' },
        @{ mutation = 'invalid-rid'; message = '只允许 win-x64' },
        @{ mutation = 'missing-range'; message = '必须显式声明 Host API' },
        @{ mutation = 'reversed-range'; message = 'minInclusive 小于 maxExclusive' },
        @{ mutation = 'missing-dll'; message = '部署资产不存在' },
        @{ mutation = 'missing-deps'; message = '部署资产不存在' },
        @{ mutation = 'missing-pdb'; message = '部署资产不存在' },
        @{ mutation = 'missing-asset'; message = '部署资产不存在' },
        @{ mutation = 'missing-directory-asset'; message = '目录资产源不存在' },
        @{ mutation = 'escape-path'; message = '目标路径必须位于插件目录内' },
        @{ mutation = 'duplicate-path'; message = '重复的部署目标路径' },
        @{ mutation = 'shared-assembly'; message = '不得携带宿主共享程序集' },
        @{ mutation = 'foreign-rid'; message = '不得携带非 win-x64 原生资产' }
    )
    foreach ($case in $negativeCases) {
        $output = (& dotnet build $fixtureProject -c $Configuration --no-restore --nologo `
            "-p:ContractMutation=$($case.mutation)" `
            "-p:ManagedPluginDeployRoot=$deployRoot" `
            -p:SkipPluginDeploy=false 2>&1 | Out-String)
        if ($LASTEXITCODE -eq 0) {
            throw "构建契约负例本应失败：$($case.mutation)"
        }
        if (-not $output.Contains($case.message, [StringComparison]::Ordinal)) {
            throw "构建契约负例没有给出预期中文诊断：$($case.mutation)；期望 '$($case.message)'。`n$output"
        }
    }

    return $negativeCases.Count
}

$projects = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'Plugins') -Filter '*.csproj' -File -Recurse |
    Where-Object {
        (Read-ProjectProperties $_.FullName)['ManagedPlugin'] -eq 'true'
    } |
    Sort-Object FullName)
if ($projects.Count -eq 0) {
    throw '没有发现声明 ManagedPlugin=true 的插件项目。'
}

$workingRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'MyAvaloniaManagedPluginMatrix-' + [Guid]::NewGuid().ToString('N'))
$firstRoot = Join-Path $workingRoot 'first'
$secondRoot = Join-Path $workingRoot 'second'
New-Item -ItemType Directory -Path $firstRoot, $secondRoot | Out-Null

try {
    $contractNegativeCases = Test-ManagedPluginBuildContract (
        Join-Path $workingRoot 'contract-fixture')

    # 使用真实插件补充默认开发体验门禁：Debug/Release 直接 build 都应落到各自 Host 输出，
    # 不能因为统一协议或独立打包入口而要求开发者改用发布脚本。
    $deploymentProbeProject = Join-Path $repositoryRoot 'Plugins\MyPlugTest\MyPlugTest\MyPlugTest.csproj'
    foreach ($buildConfiguration in 'Debug', 'Release') {
        Invoke-DotNetChecked "默认 $buildConfiguration 插件部署" @(
            'build', $deploymentProbeProject, '-c', $buildConfiguration, '--nologo',
            '-p:SkipPluginDeploy=false')
        $expectedEntry = Join-Path $repositoryRoot (
            "Host\MyAvaloniaManagement\bin\$buildConfiguration\net10.0\Controls\MyPlugTest\MyPlugTest.dll")
        if (-not (Test-Path -LiteralPath $expectedEntry -PathType Leaf)) {
            throw "直接 build 没有部署到默认 Host 目录：$expectedEntry"
        }
    }
    $summaries = @()
    foreach ($project in $projects) {
        $properties = Read-ProjectProperties $project.FullName
        $assemblyName = if ($properties['AssemblyName']) {
            $properties['AssemblyName']
        }
        else {
            [IO.Path]::GetFileNameWithoutExtension($project.Name)
        }
        $version = $properties['PluginVersion']
        $rid = if ($properties['ManagedPluginRuntimeIdentifier']) {
            $properties['ManagedPluginRuntimeIdentifier']
        }
        else {
            'win-x64'
        }
        $baseName = "$assemblyName-$version-$rid"

        Write-Host "`n[G12] 第一次隔离构建：$($project.FullName)"
        & $builder -Project $project.FullName -Configuration $Configuration -OutputDirectory $firstRoot
        if ($LASTEXITCODE -ne 0) { throw "第一次插件打包失败：$($project.FullName)" }

        Write-Host "`n[G12] 第二次隔离构建：$($project.FullName)"
        & $builder -Project $project.FullName -Configuration $Configuration -OutputDirectory $secondRoot
        if ($LASTEXITCODE -ne 0) { throw "第二次插件打包失败：$($project.FullName)" }

        $firstManifestPath = Join-Path $firstRoot "$baseName.manifest.json"
        $secondManifestPath = Join-Path $secondRoot "$baseName.manifest.json"
        $firstManifest = Get-Content -Raw -LiteralPath $firstManifestPath | ConvertFrom-Json
        $secondManifest = Get-Content -Raw -LiteralPath $secondManifestPath | ConvertFrom-Json
        if ($firstManifest.archive.sha256 -ne $secondManifest.archive.sha256) {
            throw "两次干净构建的 ZIP 摘要不一致：$($properties['ManagedPluginId'])"
        }

        $firstFileFacts = @($firstManifest.files | ForEach-Object {
            "$($_.path)|$($_.length)|$($_.sha256)"
        })
        $secondFileFacts = @($secondManifest.files | ForEach-Object {
            "$($_.path)|$($_.length)|$($_.sha256)"
        })
        if (Compare-Object $firstFileFacts $secondFileFacts) {
            throw "两次干净构建的文件清单不一致：$($properties['ManagedPluginId'])"
        }

        # 当前四插件的高价值私有资产哨兵防止声明误删；新增插件不需要修改公共 Target。
        $requiredSentinels = switch ($properties['ManagedPluginId']) {
            'myavalonia.plugin.bili-downloader' {
                @('Flurl.Http.dll', 'Microsoft.Data.Sqlite.dll', 'runtimes/win-x64/native/e_sqlite3.dll')
            }
            'myavalonia.plugin.datang-accounting-help' {
                @('EPPlus.dll', 'Microsoft.IO.RecyclableMemoryStream.dll')
            }
            'myavalonia.plugin.my-plug-test' {
                @('EPPlus.dll', 'Flurl.Http.dll')
            }
            'myavalonia.plugin.my-small-tools' {
                @('LibVLCSharp.dll', 'native/win-x64/libvlc/libvlc.dll', 'native/win-x64/libvlc/plugins/video_output/libdirect3d11_plugin.dll')
            }
            default { @() }
        }
        $packagePrefix = "Controls/$($properties['ManagedPluginDirectoryName'])/"
        $paths = @($firstManifest.files.path)
        foreach ($sentinel in $requiredSentinels) {
            if ($paths -notcontains ($packagePrefix + $sentinel)) {
                throw "$($properties['ManagedPluginId']) 缺少私有资产哨兵：$sentinel"
            }
        }

        $summaries += [ordered]@{
            pluginId = $firstManifest.pluginId
            pluginVersion = $firstManifest.pluginVersion
            project = [IO.Path]::GetRelativePath($repositoryRoot, $project.FullName).Replace('\', '/')
            archive = $firstManifest.archive
            files = $firstManifest.files.Count
            deterministic = $true
        }
    }

    # 把四个第一轮 ZIP 解压到同一候选 Host 根，再用宿主真实 PluginLoadContext 发现模块。
    # 这一步验证的是最终 ZIP，而不是项目 bin 或打包前 staging。
    $packageLoadRoot = Join-Path $workingRoot 'package-load'
    New-Item -ItemType Directory -Path $packageLoadRoot | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($archive in Get-ChildItem -LiteralPath $firstRoot -Filter '*.zip' -File) {
        [IO.Compression.ZipFile]::ExtractToDirectory($archive.FullName, $packageLoadRoot)
    }
    $previousPackageRoot = [Environment]::GetEnvironmentVariable('MYAVALONIA_G12_PACKAGE_ROOT')
    try {
        [Environment]::SetEnvironmentVariable('MYAVALONIA_G12_PACKAGE_ROOT', $packageLoadRoot)
        Invoke-DotNetChecked '最终 ZIP 宿主真实加载门禁' @(
            'test', (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'),
            '-c', $Configuration, '--no-restore', '--nologo',
            '--filter', 'FullyQualifiedName~CurrentManagedPluginLoadingTests',
            '-p:SkipPluginDeploy=true')
    }
    finally {
        [Environment]::SetEnvironmentVariable('MYAVALONIA_G12_PACKAGE_ROOT', $previousPackageRoot)
    }

    # 结果目录只保存已经复验的第一轮独立包和聚合报告。
    $allowedResultsRoot = [IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot 'artifacts')).TrimEnd('\') + '\'
    if (-not $resultsRoot.StartsWith($allowedResultsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "测试结果必须写入仓库 artifacts 下：$resultsRoot"
    }
    if (Test-Path -LiteralPath $resultsRoot) {
        [IO.Directory]::Delete($resultsRoot, $true)
    }
    New-Item -ItemType Directory -Path $resultsRoot | Out-Null
    Copy-Item -LiteralPath (Get-ChildItem -LiteralPath $firstRoot -File).FullName -Destination $resultsRoot

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        platform = 'win-x64'
        gates = [ordered]@{
            contractNegativeCases = $contractNegativeCases
            skipDeploy = $true
            currentDirectoryOnlyCleanup = $true
            defaultDebugAndReleaseDeploy = $true
            finalZipHostLoad = $true
            deterministicBuildsPerPlugin = 2
        }
        plugins = @($summaries)
    }
    $summaryPath = Join-Path $resultsRoot 'summary.json'
    Write-JsonUtf8 $summaryPath $summary
    Write-Host "`nG12 Managed Plugin 包矩阵通过：$($projects.Count) 个独立插件。"
    Write-Host "机器可读汇总：$summaryPath"
}
finally {
    if (Test-Path -LiteralPath $workingRoot) {
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (-not $workingRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "拒绝清理系统 Temp 之外的矩阵目录：$workingRoot"
        }
        [IO.Directory]::Delete($workingRoot, $true)
    }
}
