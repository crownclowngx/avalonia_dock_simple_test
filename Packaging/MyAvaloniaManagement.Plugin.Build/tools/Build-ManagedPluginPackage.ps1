param(
    [Parameter(Mandatory)]
    [string]$Project,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

function Invoke-DotNet {
    param([Parameter(Mandatory)] [string[]]$Arguments, [string]$WorkingDirectory)

    Push-Location $WorkingDirectory
    try {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') 失败，退出码 $LASTEXITCODE。"
        }
    }
    finally {
        Pop-Location
    }
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

function Get-RelativePath {
    param([string]$BasePath, [string]$Path)
    [IO.Path]::GetRelativePath(
        [IO.Path]::GetFullPath($BasePath),
        [IO.Path]::GetFullPath($Path)).Replace('\', '/')
}

function Find-SourceRoot {
    param([string]$ProjectDirectory)

    $gitRoot = @(& git -C $ProjectDirectory rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -eq 0 -and $gitRoot.Count -gt 0) {
        return [IO.Path]::GetFullPath($gitRoot[0])
    }

    for ($directory = [IO.DirectoryInfo]::new($ProjectDirectory);
         $null -ne $directory;
         $directory = $directory.Parent) {
        if ((Test-Path -LiteralPath (Join-Path $directory.FullName 'Directory.Build.props')) -or
            @(Get-ChildItem -LiteralPath $directory.FullName -File -Filter '*.sln*' -ErrorAction SilentlyContinue).Count -gt 0) {
            return $directory.FullName
        }
    }

    return [IO.Path]::GetFullPath($ProjectDirectory)
}

function Get-EvaluatedProperties {
    param([string]$ProjectPath, [string]$WorkingDirectory)

    $names = @(
        'ManagedPlugin',
        'ManagedPluginId',
        'PluginVersion',
        'ManagedPluginDirectoryName',
        'ManagedPluginEntryType',
        'ManagedPluginSdkMinInclusive',
        'ManagedPluginSdkMaxExclusive',
        'ManagedPluginRuntimeIdentifier',
        'AssemblyName',
        'TargetFramework'
    )
    $output = @(& dotnet msbuild $ProjectPath -nologo "-getProperty:$($names -join ',')" 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "无法读取 Managed Plugin 的最终 MSBuild 属性：$($output -join [Environment]::NewLine)"
    }
    return (($output -join [Environment]::NewLine) | ConvertFrom-Json).Properties
}

function Assert-StrictManifest {
    param([string]$ManifestPath)

    $text = Get-Content -Raw -LiteralPath $ManifestPath
    $options = [Text.Json.JsonDocumentOptions]::new()
    $options.AllowTrailingCommas = $false
    $options.CommentHandling = [Text.Json.JsonCommentHandling]::Disallow
    $options.MaxDepth = 8
    $document = [Text.Json.JsonDocument]::Parse($text, $options)
    try {
        $rootNames = @($document.RootElement.EnumerateObject() | ForEach-Object Name | Sort-Object)
        if (Compare-Object @('entryPoint', 'pluginId', 'pluginVersion', 'schemaVersion', 'sdk') $rootNames) {
            throw 'plugin.manifest.json 根字段集合不符合严格 schema 2。'
        }
        $entryNames = @($document.RootElement.GetProperty('entryPoint').EnumerateObject() | ForEach-Object Name | Sort-Object)
        if (Compare-Object @('assembly', 'type') $entryNames) {
            throw 'plugin.manifest.json entryPoint 字段集合不符合严格 schema 2。'
        }
        $sdkNames = @($document.RootElement.GetProperty('sdk').EnumerateObject() | ForEach-Object Name | Sort-Object)
        if (Compare-Object @('maxExclusive', 'minInclusive') $sdkNames) {
            throw 'plugin.manifest.json sdk 字段集合不符合严格 schema 2。'
        }
    }
    finally {
        $document.Dispose()
    }
    return $text | ConvertFrom-Json
}

function Write-JsonUtf8 {
    param([string]$Path, $Value)
    [IO.File]::WriteAllText(
        $Path,
        ($Value | ConvertTo-Json -Depth 16),
        [Text.UTF8Encoding]::new($false))
}

$projectPath = [IO.Path]::GetFullPath($Project)
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Managed Plugin 项目不存在：$projectPath"
}

$projectDirectory = Split-Path -Parent $projectPath
$sourceRoot = Find-SourceRoot $projectDirectory
$properties = Get-EvaluatedProperties $projectPath $projectDirectory
if ($properties.ManagedPlugin -ne 'true') {
    throw "项目没有声明 ManagedPlugin=true：$projectPath"
}

$requiredProperties = @(
    'ManagedPluginId',
    'PluginVersion',
    'ManagedPluginDirectoryName',
    'ManagedPluginEntryType',
    'ManagedPluginSdkMinInclusive',
    'ManagedPluginSdkMaxExclusive',
    'AssemblyName',
    'TargetFramework'
)
foreach ($name in $requiredProperties) {
    if ([string]::IsNullOrWhiteSpace([string]$properties.$name)) {
        throw "项目最终求值结果缺少 $name。"
    }
}
if ($properties.ManagedPluginRuntimeIdentifier -ne 'win-x64') {
    throw "当前 Managed Plugin 构建协议只允许 win-x64，实际为 $($properties.ManagedPluginRuntimeIdentifier)。"
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

$relativeProject = Get-RelativePath $sourceRoot $projectPath
$identity = "$($relativeProject.ToUpperInvariant())|$($Configuration.ToUpperInvariant())"
$slotHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($identity))).Substring(0, 20)
$temporaryParent = Join-Path ([IO.Path]::GetTempPath()) 'MyAvaloniaExternalPluginBuild'
$workingRoot = Join-Path $temporaryParent $slotHash
$sourceJunction = Join-Path $workingRoot 'source'
$artifactsRoot = Join-Path $workingRoot 'artifacts'
$stageRoot = Join-Path $workingRoot 'stage'
$deployRoot = Join-Path $stageRoot 'Controls'
$pluginRoot = Join-Path $deployRoot $properties.ManagedPluginDirectoryName
$validationRoot = Join-Path $workingRoot 'validation'
$mutex = [Threading.Mutex]::new($false, "Local\MyAvaloniaExternalPluginBuild-$slotHash")

if (-not $mutex.WaitOne([TimeSpan]::FromMinutes(5))) {
    $mutex.Dispose()
    throw "等待同一插件构建槽超时：$projectPath"
}

try {
    Assert-PathUnder $workingRoot $temporaryParent 'Managed Plugin 临时目录'
    if (Test-Path -LiteralPath $workingRoot) {
        if (Test-Path -LiteralPath $sourceJunction) {
            [IO.Directory]::Delete($sourceJunction)
        }
        [IO.Directory]::Delete($workingRoot, $true)
    }
    New-Item -ItemType Directory -Path $workingRoot, $artifactsRoot, $deployRoot -Force | Out-Null
    New-Item -ItemType Junction -Path $sourceJunction -Target $sourceRoot | Out-Null

    $junctionProject = Join-Path $sourceJunction $relativeProject.Replace('/', '\')
    Invoke-DotNet @(
        'restore', $junctionProject,
        '--locked-mode', '--nologo',
        '--artifacts-path', $artifactsRoot,
        '-p:SkipPluginDeploy=true'
    ) $sourceRoot
    Invoke-DotNet @(
        'build', $junctionProject,
        '-c', $Configuration,
        '--no-restore', '--nologo', '-warnaserror',
        '--artifacts-path', $artifactsRoot,
        '-p:ContinuousIntegrationBuild=true',
        # portable PDB 会记录编译输入与生成器输出路径。仅用 Junction 固定同一轮槽名时，
        # 槽的 TEMP 父目录和外部仓库绝对根仍会随隔离轮次变化，造成 DLL/PDB/ZIP 哈希漂移。
        # PathMap 只归一化调试文档路径，不改变 IL、清单、运行时装载或 public API。
        # `%2C` 是 MSBuild 属性中的逗号转义，避免 Windows 把第二段映射误识别为新属性。
        "-p:PathMap=$workingRoot=/_/external-managed-plugin-build%2C$sourceRoot=/_/external-repository",
        "-p:ManagedPluginDeployRoot=$deployRoot",
        '-p:SkipPluginDeploy=false'
    ) $sourceRoot

    if (-not (Test-Path -LiteralPath $pluginRoot -PathType Container)) {
        throw "构建未生成插件部署目录：$pluginRoot"
    }

    $requiredFiles = @(
        'plugin.manifest.json',
        "$($properties.AssemblyName).dll",
        "$($properties.AssemblyName).deps.json",
        "$($properties.AssemblyName).pdb"
    )
    foreach ($fileName in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $pluginRoot $fileName) -PathType Leaf)) {
            throw "插件包缺少必需文件：$fileName"
        }
    }

    $manifest = Assert-StrictManifest (Join-Path $pluginRoot 'plugin.manifest.json')
    if ($manifest.schemaVersion -ne 2 -or
        $manifest.pluginId -ne $properties.ManagedPluginId -or
        $manifest.pluginVersion -ne $properties.PluginVersion -or
        $manifest.entryPoint.assembly -ne "$($properties.AssemblyName).dll" -or
        $manifest.entryPoint.type -ne $properties.ManagedPluginEntryType -or
        $manifest.sdk.minInclusive -ne $properties.ManagedPluginSdkMinInclusive -or
        $manifest.sdk.maxExclusive -ne $properties.ManagedPluginSdkMaxExclusive) {
        throw '生成清单与最终 MSBuild 声明不一致。'
    }

    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName(
        (Join-Path $pluginRoot "$($properties.AssemblyName).dll")).Version.ToString(3)
    if ($assemblyVersion -ne $properties.PluginVersion) {
        throw "入口程序集版本 $assemblyVersion 与插件版本 $($properties.PluginVersion) 不一致。"
    }

    $payloadFiles = @(Get-ChildItem -LiteralPath $pluginRoot -File -Recurse)
    $forbidden = @($payloadFiles | Where-Object {
        $_.Extension -eq '.dll' -and
        $_.Name -match '^(?:MyAvaloniaManagement(?:Common)?|CommunityToolkit\.Mvvm|Avalonia(?:\.|$)|Dock\.|Semi\.Avalonia|Ursa(?:\.|$)|Microsoft\.Extensions\.|Newtonsoft\.Json)'
    })
    if ($forbidden.Count -ne 0) {
        throw "插件包混入宿主共享程序集：$($forbidden.Name -join ', ')"
    }

    $entries = foreach ($file in ($payloadFiles | Sort-Object { Get-RelativePath $stageRoot $_.FullName })) {
        [ordered]@{
            path = Get-RelativePath $stageRoot $file.FullName
            length = $file.Length
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash
        }
    }

    $baseName = "$($properties.AssemblyName)-$($properties.PluginVersion)-win-x64"
    $zipPath = Join-Path $resolvedOutput "$baseName.zip"
    $sidecarPath = Join-Path $resolvedOutput "$baseName.manifest.json"
    foreach ($path in $zipPath, $sidecarPath) {
        if (Test-Path -LiteralPath $path) {
            [IO.File]::Delete($path)
        }
    }

    Add-Type -AssemblyName System.IO.Compression
    $zipStream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $zipStream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            foreach ($file in (Get-ChildItem -LiteralPath $stageRoot -File -Recurse |
                    Sort-Object { Get-RelativePath $stageRoot $_.FullName })) {
                $relative = Get-RelativePath $stageRoot $file.FullName
                $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $entryStream = $entry.Open()
                try {
                    $input = [IO.File]::OpenRead($file.FullName)
                    try { $input.CopyTo($entryStream) }
                    finally { $input.Dispose() }
                }
                finally { $entryStream.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $zipStream.Dispose() }

    New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $validationRoot)
    foreach ($entry in $entries) {
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

    $revision = @(& git -C $sourceRoot rev-parse --short=12 HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or $revision.Count -eq 0) {
        $revision = @('unversioned')
    }
    $packageManifest = [ordered]@{
        schemaVersion = 2
        pluginId = $properties.ManagedPluginId
        pluginVersion = $properties.PluginVersion
        entryPoint = [ordered]@{
            assembly = "$($properties.AssemblyName).dll"
            type = $properties.ManagedPluginEntryType
        }
        sdk = [ordered]@{
            minInclusive = $properties.ManagedPluginSdkMinInclusive
            maxExclusive = $properties.ManagedPluginSdkMaxExclusive
        }
        directoryName = $properties.ManagedPluginDirectoryName
        targetFramework = $properties.TargetFramework
        runtimeIdentifier = 'win-x64'
        sourceRevision = [string]$revision[0]
        archive = [ordered]@{
            file = [IO.Path]::GetFileName($zipPath)
            length = (Get-Item -LiteralPath $zipPath).Length
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash
        }
        files = @($entries)
    }
    Write-JsonUtf8 $sidecarPath $packageManifest

    Write-Host "Managed Plugin 独立包已生成：$zipPath"
    Write-Host "机器可读清单：$sidecarPath"
}
finally {
    if (Test-Path -LiteralPath $workingRoot) {
        Assert-PathUnder $workingRoot $temporaryParent 'Managed Plugin 临时目录'
        if (Test-Path -LiteralPath $sourceJunction) {
            [IO.Directory]::Delete($sourceJunction)
        }
        [IO.Directory]::Delete($workingRoot, $true)
    }
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
