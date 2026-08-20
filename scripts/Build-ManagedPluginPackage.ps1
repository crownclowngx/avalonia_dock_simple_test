param(
    [Parameter(Mandatory)]
    [string]$Project,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

# 本脚本只负责一个插件的可重复构建、通用包校验与 ZIP/清单生成。
# 插件业务测试、联网验收和原生运行时探针仍由各插件自己的发布入口负责。
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = if ([IO.Path]::IsPathRooted($Project)) {
    [IO.Path]::GetFullPath($Project)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Project))
}

function Assert-PathUnder {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$AllowedRoot,
        [Parameter(Mandatory)] [string]$Purpose
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\', '/')
    if (-not $fullPath.StartsWith(
            $fullRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose 必须位于允许目录内：$fullPath；允许根：$fullRoot"
    }
}

function Invoke-DotNet {
    param([Parameter(Mandatory)] [string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet 命令失败，退出码 $LASTEXITCODE：dotnet $($Arguments -join ' ')"
    }
}

function Get-StableRelativePath {
    param(
        [Parameter(Mandatory)] [string]$BasePath,
        [Parameter(Mandatory)] [string]$Path
    )

    return [IO.Path]::GetRelativePath(
        [IO.Path]::GetFullPath($BasePath),
        [IO.Path]::GetFullPath($Path)).Replace('\', '/')
}

function Write-JsonUtf8 {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] $Value
    )

    $json = $Value | ConvertTo-Json -Depth 16
    [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Managed Plugin 项目不存在：$projectPath"
}
Assert-PathUnder $projectPath $repositoryRoot 'Managed Plugin 项目'

[xml]$projectXml = Get-Content -Raw -LiteralPath $projectPath
$propertyElements = @($projectXml.Project.PropertyGroup.ChildNodes |
    Where-Object NodeType -eq ([Xml.XmlNodeType]::Element))
$properties = @{}
foreach ($element in $propertyElements) {
    $properties[$element.LocalName] = [string]$element.InnerText.Trim()
}

if ($properties['ManagedPlugin'] -ne 'true') {
    throw "项目没有声明 ManagedPlugin=true：$projectPath"
}

$pluginId = $properties['ManagedPluginId']
$pluginVersion = $properties['PluginVersion']
$pluginDirectoryName = $properties['ManagedPluginDirectoryName']
$runtimeIdentifier = if ($properties['ManagedPluginRuntimeIdentifier']) {
    $properties['ManagedPluginRuntimeIdentifier']
}
else {
    'win-x64'
}
$assemblyName = if ($properties['AssemblyName']) {
    $properties['AssemblyName']
}
else {
    [IO.Path]::GetFileNameWithoutExtension($projectPath)
}
$targetFramework = if ($properties['TargetFramework']) {
    $properties['TargetFramework']
}
else {
    'net10.0'
}

foreach ($required in @{
        ManagedPluginId = $pluginId
        PluginVersion = $pluginVersion
        ManagedPluginDirectoryName = $pluginDirectoryName
    }.GetEnumerator()) {
    if ([string]::IsNullOrWhiteSpace([string]$required.Value)) {
        throw "项目缺少 $($required.Key)：$projectPath"
    }
}
if ($runtimeIdentifier -ne 'win-x64') {
    throw "Managed Plugin v1 只允许 win-x64，实际为 $runtimeIdentifier。"
}

$resolvedOutput = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot 'artifacts\managed-plugin-packages'
}
elseif ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

# C# PE 的 CodeView 会记录 PDB 输出路径，Avalonia XAML 后处理也会把 ProjectDirectory 写入
# portable PDB。若固定槽依赖克隆绝对路径，只能保证同一克隆内重复构建一致。这里改用稳定的
# “仓库相对项目路径 + 配置”作为槽身份，并让构建经由槽内固定 Junction 访问当前克隆。
# 命名互斥量保证两个克隆不会同时切换同一 Junction；每轮仍先清空槽，不复用任何构建产物。
$projectRelativePath = Get-StableRelativePath $repositoryRoot $projectPath
$slotIdentity = ($projectRelativePath.ToUpperInvariant() + '|' + $Configuration.ToUpperInvariant())
$slotHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($slotIdentity)))
$slotName = $slotHash.Substring(0, 20)
$workingRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'MyAvaloniaManagedPluginBuild\' + $slotName)
$stableBuildParent = if ([string]::IsNullOrWhiteSpace($env:MYAVALONIA_MANAGED_PLUGIN_STABLE_ROOT)) {
    Join-Path ([IO.Path]::GetTempPath()) 'MyAvaloniaManagedPluginStable'
}
else {
    [IO.Path]::GetFullPath($env:MYAVALONIA_MANAGED_PLUGIN_STABLE_ROOT)
}
$stableBuildRoot = Join-Path $stableBuildParent $slotName
$physicalSourceJunction = Join-Path $workingRoot 'source'
$buildMutex = [Threading.Mutex]::new($false, "Local\MyAvaloniaManagedPluginBuild-$slotName")
if (-not $buildMutex.WaitOne([TimeSpan]::FromMinutes(5))) {
    $buildMutex.Dispose()
    throw "等待同一插件构建槽超时：$projectPath"
}

Assert-PathUnder $stableBuildRoot $stableBuildParent 'Managed Plugin 稳定路径'
if (Test-Path -LiteralPath $stableBuildRoot) {
    # stableBuildRoot 自身只能是指向本轮物理工作目录的 Junction；删除重解析点时不使用 Recurse。
    [IO.Directory]::Delete($stableBuildRoot)
}
if (Test-Path -LiteralPath $workingRoot) {
    Assert-PathUnder $workingRoot ([IO.Path]::GetTempPath()) 'Managed Plugin 临时目录'
    if (Test-Path -LiteralPath $physicalSourceJunction) {
        # Junction 必须作为单个重解析点删除，绝不能递归进入它指向的真实仓库。
        [IO.Directory]::Delete($physicalSourceJunction)
    }
    [IO.Directory]::Delete($workingRoot, $true)
}
$dotnetArtifacts = Join-Path $workingRoot 'dotnet'
$stageRoot = Join-Path $workingRoot 'stage'
$deployRoot = Join-Path $stageRoot 'Controls'
$pluginRoot = Join-Path $deployRoot $pluginDirectoryName
$validationRoot = Join-Path $workingRoot 'validation'
New-Item -ItemType Directory -Path $workingRoot, $dotnetArtifacts, $deployRoot, $stableBuildParent -Force | Out-Null
New-Item -ItemType Junction -Path $physicalSourceJunction -Target $repositoryRoot | Out-Null
New-Item -ItemType Junction -Path $stableBuildRoot -Target $workingRoot | Out-Null
$stableProjectPath = Join-Path (Join-Path $stableBuildRoot 'source') $projectRelativePath.Replace('/', '\')
$stableDotnetArtifacts = Join-Path $stableBuildRoot 'dotnet'
$stableDeployRoot = Join-Path $stableBuildRoot 'stage\Controls'

try {
    Invoke-DotNet @(
        'restore', $stableProjectPath,
        '--locked-mode', '--nologo',
        '--artifacts-path', $stableDotnetArtifacts,
        '-p:SkipPluginDeploy=true'
    )
    Invoke-DotNet @(
        'build', $stableProjectPath,
        '-c', $Configuration,
        '--no-restore', '--nologo', '-warnaserror',
        '--artifacts-path', $stableDotnetArtifacts,
        '-p:ContinuousIntegrationBuild=true',
        # Roslyn 仍显式映射真实仓库、稳定 Junction 和物理构建槽；Avalonia 后处理即使
        # 忽略 PathMap，看到的也是两轮相同的 stableBuildRoot，而不是各自的物理隔离目录。
        # `%2C` 是 MSBuild 命令行属性值中的转义逗号；若直接写逗号，dotnet 会把第二个
        # Windows 路径误解析成新的属性名。MSBuild 解码后传给编译器的仍是标准 PathMap 列表。
        "-p:PathMap=$stableBuildRoot=/_/managed-plugin-build%2C$repositoryRoot=/_/repo%2C$workingRoot=/_/managed-plugin-build",
        "-p:ManagedPluginDeployRoot=$stableDeployRoot",
        '-p:SkipPluginDeploy=false'
    )

    if (-not (Test-Path -LiteralPath $pluginRoot -PathType Container)) {
        throw "构建未生成插件部署目录：$pluginRoot"
    }

    $requiredFiles = @(
        'plugin.manifest.json',
        "$assemblyName.dll",
        "$assemblyName.deps.json",
        "$assemblyName.pdb"
    )
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $pluginRoot $requiredFile) -PathType Leaf)) {
            throw "插件包缺少必需文件：$requiredFile"
        }
    }

    $manifestPath = Join-Path $pluginRoot 'plugin.manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $actualRootFields = @($manifest.PSObject.Properties.Name | Sort-Object)
    $expectedRootFields = @('compatibility', 'entryAssembly', 'pluginId', 'pluginVersion', 'schemaVersion')
    if (Compare-Object $expectedRootFields $actualRootFields) {
        throw "plugin.manifest.json 的根字段不是严格 schema v1：$($actualRootFields -join ', ')"
    }
    if ($manifest.schemaVersion -ne 1 -or
        $manifest.pluginId -ne $pluginId -or
        $manifest.pluginVersion -ne $pluginVersion -or
        $manifest.entryAssembly -ne "$assemblyName.dll") {
        throw '生成清单与项目声明不一致。'
    }

    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName(
        (Join-Path $pluginRoot "$assemblyName.dll")).Version.ToString(3)
    if ($assemblyVersion -ne $pluginVersion) {
        throw "入口程序集版本 $assemblyVersion 与插件版本 $pluginVersion 不一致。"
    }

    $payloadFiles = @(Get-ChildItem -LiteralPath $pluginRoot -File -Recurse)
    $forbiddenShared = @($payloadFiles | Where-Object {
        $_.Extension -eq '.dll' -and $_.Name -match '^(?:MyAvaloniaManagement(?:Common)?|CommunityToolkit\.Mvvm|Avalonia(?:\.|$)|Dock\.|Semi\.Avalonia|Ursa(?:\.|$)|Microsoft\.Extensions\.|Newtonsoft\.Json)'
    })
    if ($forbiddenShared.Count -ne 0) {
        throw "插件包混入宿主共享程序集：$($forbiddenShared.Name -join ', ')"
    }

    $foreignRid = @($payloadFiles | Where-Object {
        $relative = Get-StableRelativePath $pluginRoot $_.FullName
        ($relative -match '(^|/)runtimes/([^/]+)/' -and $Matches[2] -ne 'win-x64') -or
        ($relative -match '(^|/)native/([^/]+)/' -and $Matches[2] -ne 'win-x64')
    })
    if ($foreignRid.Count -ne 0) {
        throw "Windows x64 插件包混入其他 RID 资产：$($foreignRid.FullName -join ', ')"
    }

    $payloadEntries = foreach ($file in ($payloadFiles | Sort-Object {
                Get-StableRelativePath $stageRoot $_.FullName
            })) {
        [ordered]@{
            path = Get-StableRelativePath $stageRoot $file.FullName
            length = $file.Length
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash
        }
    }

    $baseName = "$assemblyName-$pluginVersion-$runtimeIdentifier"
    $zipPath = Join-Path $resolvedOutput "$baseName.zip"
    $sidecarPath = Join-Path $resolvedOutput "$baseName.manifest.json"
    foreach ($exactOutput in $zipPath, $sidecarPath) {
        if (Test-Path -LiteralPath $exactOutput) {
            [IO.File]::Delete([IO.Path]::GetFullPath($exactOutput))
        }
    }

    # CreateFromDirectory 会继承文件时间且不承诺条目顺序，因此显式逐项写入确定性 ZIP。
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipStream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $zipStream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            foreach ($file in (Get-ChildItem -LiteralPath $stageRoot -File -Recurse |
                    Sort-Object { Get-StableRelativePath $stageRoot $_.FullName })) {
                $relativePath = Get-StableRelativePath $stageRoot $file.FullName
                $entry = $archive.CreateEntry(
                    $relativePath,
                    [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(
                    2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $entryStream = $entry.Open()
                try {
                    $input = [IO.File]::OpenRead($file.FullName)
                    try {
                        $input.CopyTo($entryStream)
                    }
                    finally {
                        $input.Dispose()
                    }
                }
                finally {
                    $entryStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $zipStream.Dispose()
    }

    # 不信任打包前目录：从最终 ZIP 解压并复算每个文件，覆盖漏包、损坏和路径逃逸。
    New-Item -ItemType Directory -Path $validationRoot | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $validationRoot)
    $validatedFiles = @(Get-ChildItem -LiteralPath $validationRoot -File -Recurse)
    if ($validatedFiles.Count -ne $payloadEntries.Count) {
        throw "ZIP 文件数量不一致：清单 $($payloadEntries.Count)，实际 $($validatedFiles.Count)。"
    }
    foreach ($entry in $payloadEntries) {
        $validatedPath = Join-Path $validationRoot $entry.path.Replace('/', '\')
        if (-not (Test-Path -LiteralPath $validatedPath -PathType Leaf)) {
            throw "ZIP 缺少清单文件：$($entry.path)"
        }
        $info = Get-Item -LiteralPath $validatedPath
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $validatedPath).Hash
        if ($info.Length -ne $entry.length -or $hash -ne $entry.sha256) {
            throw "ZIP 文件摘要不一致：$($entry.path)"
        }
    }

    $revision = (& git -C $repositoryRoot rev-parse --short=12 HEAD).Trim()
    $packageManifest = [ordered]@{
        schemaVersion = 1
        pluginId = $pluginId
        pluginVersion = $pluginVersion
        entryAssembly = "$assemblyName.dll"
        directoryName = $pluginDirectoryName
        targetFramework = $targetFramework
        runtimeIdentifier = $runtimeIdentifier
        sourceRevision = $revision
        archive = [ordered]@{
            file = [IO.Path]::GetFileName($zipPath)
            length = (Get-Item -LiteralPath $zipPath).Length
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash
        }
        files = @($payloadEntries)
    }
    Write-JsonUtf8 $sidecarPath $packageManifest

    Write-Host "Managed Plugin 独立包已生成：$zipPath"
    Write-Host "机器可读清单：$sidecarPath"
}
finally {
    # 工作目录由本脚本在系统 Temp 下以固定前缀和哈希槽创建，删除前再次验证边界。
    if (Test-Path -LiteralPath $stableBuildRoot) {
        Assert-PathUnder $stableBuildRoot $stableBuildParent 'Managed Plugin 稳定路径'
        # 稳定路径只是指向隔离物理目录的 Junction，必须先移除它本身。
        [IO.Directory]::Delete($stableBuildRoot)
    }
    if (Test-Path -LiteralPath $workingRoot) {
        Assert-PathUnder $workingRoot ([IO.Path]::GetTempPath()) 'Managed Plugin 临时目录'
        if (Test-Path -LiteralPath $physicalSourceJunction) {
            # 先移除 Junction 本身，再递归删除纯临时输出，防止清理边界跨入真实仓库。
            [IO.Directory]::Delete($physicalSourceJunction)
        }
        [IO.Directory]::Delete($workingRoot, $true)
    }
    $buildMutex.ReleaseMutex()
    $buildMutex.Dispose()
}
