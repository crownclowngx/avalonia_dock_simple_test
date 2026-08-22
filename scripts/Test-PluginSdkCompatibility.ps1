param(
    [ValidatePattern('^v[1-9][0-9]*$')]
    [string]$Baseline = 'v2',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
# 兼容性变异项目会复制完整 SDK 目录并生成 bin/obj，因此使用短随机根避免
# 在 G14 已隔离 TEMP 下再次叠加长路径。随机性与安全清理的父路径校验保持不变。
$workingRoot = Join-Path $temporaryParent (
    'MSA-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
$projectDefinitions = @(
    [pscustomobject]@{
        Name = 'Core'
        RelativePath = 'Host\MyAvaloniaManagement.PluginSdk\MyAvaloniaManagement.PluginSdk.csproj'
        ApiDirectory = 'Host\MyAvaloniaManagement.PluginSdk\ApiCompatibility'
    },
    [pscustomobject]@{
        Name = 'UI'
        RelativePath = 'Host\MyAvaloniaManagement.PluginSdk.UI\MyAvaloniaManagement.PluginSdk.UI.csproj'
        ApiDirectory = 'Host\MyAvaloniaManagement.PluginSdk.UI\ApiCompatibility'
    }
)

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-ChildPath {
    param([string]$ChildPath, [string]$ParentPath)
    $child = [IO.Path]::GetFullPath($ChildPath)
    $parent = [IO.Path]::GetFullPath($ParentPath).TrimEnd([IO.Path]::DirectorySeparatorChar)
    Assert-True ($child.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) `
        "路径 $child 不在允许目录 $parent 内。"
}

function Invoke-DotNetChecked {
    param([string]$Description, [string[]]$Arguments, [string]$WorkingDirectory = $repositoryRoot)
    Push-Location $WorkingDirectory
    try {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Description 失败，退出码 $LASTEXITCODE。" }
    }
    finally { Pop-Location }
}

function Invoke-DotNetFailure {
    param(
        [string]$Description,
        [string[]]$Arguments,
        [string[]]$ExpectedFragments,
        [string]$WorkingDirectory
    )
    Push-Location $WorkingDirectory
    try {
        $output = @(& dotnet @Arguments 2>&1)
        Assert-True ($LASTEXITCODE -ne 0) "$Description 意外成功。"
        $text = $output -join [Environment]::NewLine
        foreach ($fragment in $ExpectedFragments) {
            Assert-True ($text.Contains($fragment, [StringComparison]::Ordinal)) `
                "$Description 缺少预期诊断 $fragment。"
        }
    }
    finally { Pop-Location }
}

function Read-ApiFile {
    param([string]$Path, [string]$Description)
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "$Description 不存在：$Path"
    $lines = @(Get-Content -LiteralPath $Path)
    Assert-True ($lines.Count -ge 1 -and $lines[0] -ceq '#nullable enable') "$Description 缺少 nullable 头。"
    $entries = @($lines | Select-Object -Skip 1)
    Assert-True (-not ($entries | Where-Object { [string]::IsNullOrWhiteSpace($_) })) "$Description 包含空行。"
    Assert-True (-not ($entries | Where-Object { $_.StartsWith('*REMOVED*', [StringComparison]::Ordinal) })) `
        "$Description 不得用 REMOVED 标记绕过破坏检查。"
    Assert-True ($entries.Count -eq @($entries | Select-Object -Unique).Count) "$Description 包含重复条目。"
    [string[]]$sorted = @($entries)
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    Assert-True (($entries -join "`n") -ceq ($sorted -join "`n")) "$Description 必须按 Ordinal 稳定排序。"
    return $entries
}

function Set-Utf8Text {
    param([string]$Path, [string]$Content)
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function Replace-ExactlyOnce {
    param([string]$Path, [string]$OldText, [string]$NewText)
    $text = [IO.File]::ReadAllText($Path)
    $first = $text.IndexOf($OldText, [StringComparison]::Ordinal)
    Assert-True ($first -ge 0) "替换哨兵不存在：$OldText"
    Assert-True ($text.IndexOf($OldText, $first + $OldText.Length, [StringComparison]::Ordinal) -lt 0) `
        "替换哨兵不唯一：$OldText"
    Set-Utf8Text $Path ($text.Remove($first, $OldText.Length).Insert($first, $NewText))
}

function Invoke-MutatedBuildFailure {
    param(
        [string]$Description,
        [string]$Path,
        [string]$OldText,
        [string]$NewText,
        [string[]]$ExpectedFragments,
        [string]$ProjectPath
    )
    $original = [IO.File]::ReadAllText($Path)
    try {
        Replace-ExactlyOnce $Path $OldText $NewText
        Invoke-DotNetFailure $Description @(
            'build', $ProjectPath, '-c', $Configuration, '--no-restore', '--nologo', '-t:Rebuild'
        ) $ExpectedFragments $workingRoot
    }
    finally { Set-Utf8Text $Path $original }
}

[xml]$versionDocument = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props')
$properties = $versionDocument.Project.PropertyGroup
$activeBaseline = [string]$properties.MyAvaloniaPluginSdkApiBaseline
$sdkVersion = [Version]([string]$properties.MyAvaloniaPluginSdkVersion)
$sdkAssemblyVersion = [Version]([string]$properties.MyAvaloniaPluginSdkAssemblyVersion)
$baselineMajor = [int]$Baseline.Substring(1)
Assert-True ($Baseline -ceq $activeBaseline) "请求基线 $Baseline 与活动基线 $activeBaseline 不一致。"
Assert-True ($baselineMajor -eq $sdkVersion.Major -and $baselineMajor -eq $sdkAssemblyVersion.Major) `
    "基线主版本必须同时匹配 SDK 包和程序集版本。"

$counts = @{}
foreach ($definition in $projectDefinitions) {
    $directory = Join-Path $repositoryRoot "$($definition.ApiDirectory)\$Baseline"
    $shipped = Read-ApiFile (Join-Path $directory 'PublicAPI.Shipped.txt') "$($definition.Name) Shipped"
    $unshipped = Read-ApiFile (Join-Path $directory 'PublicAPI.Unshipped.txt') "$($definition.Name) Unshipped"
    Assert-True (-not ($shipped | Where-Object { $unshipped -ccontains $_ })) `
        "$($definition.Name) Shipped 与 Unshipped 存在重复 API。"
    $counts[$definition.Name] = @($shipped.Count, $unshipped.Count)
    Invoke-DotNetChecked "$($definition.Name) SDK 锁定还原" @(
        'restore', $definition.RelativePath, '--locked-mode', '--nologo')
    Invoke-DotNetChecked "$($definition.Name) SDK API 构建" @(
        'build', $definition.RelativePath, '-c', $Configuration, '--no-restore', '--nologo', '-warnaserror')
}

Assert-ChildPath $workingRoot $temporaryParent
New-Item -ItemType Directory -Path $workingRoot | Out-Null
try {
    foreach ($file in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'Directory.Version.props', 'global.json')) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $file) -Destination (Join-Path $workingRoot $file)
    }
    New-Item -ItemType Directory -Path (Join-Path $workingRoot 'Host') | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginSdk') `
        -Destination (Join-Path $workingRoot 'Host') -Recurse
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginSdk.UI') `
        -Destination (Join-Path $workingRoot 'Host') -Recurse

    $coreProject = Join-Path $workingRoot $projectDefinitions[0].RelativePath
    $uiProject = Join-Path $workingRoot $projectDefinitions[1].RelativePath
    Invoke-DotNetChecked '测试副本锁定还原' @('restore', $uiProject, '--locked-mode', '--nologo') $workingRoot

    $documentContracts = Join-Path $workingRoot 'Host\MyAvaloniaManagement.PluginSdk\DocumentContracts.cs'
    $documentOriginal = [IO.File]::ReadAllText($documentContracts)
    try {
        Remove-Item -LiteralPath $documentContracts -Force
        Invoke-DotNetFailure 'Core 删除 public 类型' @(
            'build', $coreProject, '-c', $Configuration, '--no-restore', '--nologo', '-t:Rebuild'
        ) @('RS0017', 'DocumentContent') $workingRoot
    }
    finally { Set-Utf8Text $documentContracts $documentOriginal }

    $identifiers = Join-Path $workingRoot 'Host\MyAvaloniaManagement.PluginSdk\StableIdentifiers.cs'
    $parseMember = '    public static PluginId Parse(string value) => new(value);'
    Invoke-MutatedBuildFailure 'Core 删除 public 成员' $identifiers $parseMember '' `
        @('RS0017', 'PluginId.Parse') $coreProject
    Invoke-MutatedBuildFailure 'Core 修改参数名称' $identifiers $parseMember `
        '    public static PluginId Parse(string text) => new(text);' `
        @('RS0017', 'PluginId.Parse', 'string! value') $coreProject
    Invoke-MutatedBuildFailure 'Core 修改参数类型' $identifiers $parseMember `
        '    public static PluginId Parse(object value) => new((string)value);' `
        @('RS0017', 'PluginId.Parse', 'string! value') $coreProject
    Invoke-MutatedBuildFailure 'Core 修改参数数量' $identifiers $parseMember `
        '    public static PluginId Parse(string value, bool strict) => new(value);' `
        @('RS0017', 'PluginId.Parse', 'string! value') $coreProject
    Invoke-MutatedBuildFailure 'Core 修改返回类型' $identifiers $parseMember `
        '    public static object Parse(string value) => new PluginId(value);' `
        @('RS0017', 'PluginId.Parse', 'PluginId!') $coreProject

    $fullscreenPort = Join-Path $workingRoot 'Host\MyAvaloniaManagement.PluginSdk.UI\IWindowContentFullscreenHost.cs'
    Invoke-MutatedBuildFailure 'UI 收窄 public 类型' $fullscreenPort `
        'public interface IWindowContentFullscreenHost' 'internal interface IWindowContentFullscreenHost' `
        @('RS0017', 'IWindowContentFullscreenHost') $uiProject

    $originalIdentifiers = [IO.File]::ReadAllText($identifiers)
    $fixtureUnshipped = Join-Path $workingRoot "Host\MyAvaloniaManagement.PluginSdk\ApiCompatibility\$Baseline\PublicAPI.Unshipped.txt"
    $originalUnshipped = [IO.File]::ReadAllText($fixtureUnshipped)
    try {
        $probe = @'

    /// <summary>仅用于证明兼容新增必须显式登记。</summary>
    public static string G2CompatibilityProbe() => "ok";
'@
        Replace-ExactlyOnce $identifiers $parseMember ($parseMember + $probe)
        Invoke-DotNetFailure 'Core 未登记兼容新增' @(
            'build', $coreProject, '-c', $Configuration, '--no-restore', '--nologo', '-t:Rebuild'
        ) @('RS0016', 'G2CompatibilityProbe') $workingRoot
        $registered = @(
            Get-Content -LiteralPath $fixtureUnshipped
            'static MyAvaloniaManagement.PluginSdk.PluginId.G2CompatibilityProbe() -> string!'
        )
        Set-Utf8Text $fixtureUnshipped (($registered -join "`r`n") + "`r`n")
        Invoke-DotNetChecked 'Core 登记后的兼容新增' @(
            'build', $coreProject, '-c', $Configuration, '--no-restore', '--nologo', '-t:Rebuild') $workingRoot
    }
    finally {
        Set-Utf8Text $identifiers $originalIdentifiers
        Set-Utf8Text $fixtureUnshipped $originalUnshipped
    }

    Write-Host (
        "[SDK API] 通过：Core Shipped=$($counts.Core[0])、Unshipped=$($counts.Core[1])；" +
        "UI Shipped=$($counts.UI[0])、Unshipped=$($counts.UI[1])；" +
        '7 个破坏性负例和 1 组兼容新增审阅流程符合预期。')
}
finally {
    if (Test-Path -LiteralPath $workingRoot) {
        Assert-ChildPath $workingRoot $temporaryParent
        Remove-Item -LiteralPath $workingRoot -Recurse -Force
    }
}
