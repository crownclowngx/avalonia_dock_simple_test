[CmdletBinding()]
param(
    [ValidatePattern('^v[1-9][0-9]*$')]
    [string]$Baseline = 'v1',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sdkRelativePath = 'Host\MyAvaloniaManagementCommon\MyAvaloniaManagementCommon.csproj'
$sdkProject = Join-Path $repositoryRoot $sdkRelativePath
$versionFile = Join-Path $repositoryRoot 'Directory.Version.props'
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$workingRoot = Join-Path $temporaryParent (
    'MyAvaloniaPluginSdkCompatibility-' + [Guid]::NewGuid().ToString('N'))

# 统一表达脚本政策断言。这里只负责把布尔结果转换为可读异常，
# 不参与 API 签名解析，避免 PowerShell 再实现一套 Analyzer 逻辑。
function Assert-True {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

# 破坏性清理前的必经哨兵。候选路径和父路径都先转为绝对路径，
# 再比较带末尾分隔符的父前缀；这样 `C:\Temp2` 不会被误当作 `C:\Temp` 子目录。
function Assert-ChildPath {
    param(
        [Parameter(Mandatory)] [string]$Candidate,
        [Parameter(Mandatory)] [string]$Parent
    )

    $resolvedCandidate = [IO.Path]::GetFullPath($Candidate)
    $resolvedParent = [IO.Path]::GetFullPath($Parent).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    Assert-True (
        $resolvedCandidate.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase)) (
        "拒绝操作临时根之外的路径：$resolvedCandidate；允许根：$resolvedParent")
}

# 执行必须成功的 dotnet 命令，同时保证 Push-Location 总能成对恢复。
# 成功路径保留原始输出，便于 CI 和人工审阅看到真实构建证据。
function Invoke-DotNetChecked {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [string]$WorkingDirectory = $repositoryRoot
    )

    Push-Location $WorkingDirectory
    try {
        & dotnet @Arguments | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "$Name 失败，退出码 $LASTEXITCODE。"
        }
    }
    finally {
        Pop-Location
    }
}

# 执行“必须失败”的负例构建。仅检查退出码会掩盖无关编译错误，
# 因此还要求输出同时包含 RS 诊断和被破坏的具体类型/成员签名。
function Invoke-DotNetFailure {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string[]]$ExpectedFragments,
        [Parameter(Mandatory)] [string]$WorkingDirectory
    )

    Push-Location $WorkingDirectory
    try {
        $output = @(& dotnet @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
        $text = $output -join [Environment]::NewLine
        Assert-True ($exitCode -ne 0) "$Name 意外通过，兼容门禁没有阻断测试变异。"
        foreach ($fragment in $ExpectedFragments) {
            Assert-True (
                $text.Contains($fragment, [StringComparison]::Ordinal)) (
                "$Name 已失败，但诊断没有包含 '$fragment'。`n$text")
        }
        Write-Host "[G13] $Name：已按预期失败，并打印 $($ExpectedFragments -join ', ')。"
        return $text
    }
    finally {
        Pop-Location
    }
}

# PowerShell 的默认排序可受当前文化影响；API 基线是源码事实，
# 必须用 StringComparer.Ordinal 生成与机器和区域无关的审阅顺序。
function Get-OrdinalSortedCopy {
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [string[]]$Values)

    $copy = [string[]]$Values.Clone()
    [Array]::Sort($copy, [StringComparer]::Ordinal)
    return $copy
}

# 检查文本基线的仓库政策，而不重复解析 C# 符号。Analyzer 负责符号对比；
# 此函数只保护文件存在、nullable 头、可读性、唯一性和禁止删除标记等人工审阅属性。
function Assert-ApiBaselineFile {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$DisplayName
    )

    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "$DisplayName 不存在：$Path"
    $lines = @(Get-Content -LiteralPath $Path)
    Assert-True ($lines.Count -ge 1) "$DisplayName 不能为空。"
    Assert-True ($lines[0] -ceq '#nullable enable') "$DisplayName 第一行必须是 #nullable enable。"

    $entries = @($lines | Select-Object -Skip 1)
    Assert-True (-not ($entries | Where-Object { [string]::IsNullOrWhiteSpace($_) })) (
        "$DisplayName 不允许空条目，避免文本差异掩盖真实 API。")
    Assert-True (-not ($entries | Where-Object { $_ -cne $_.Trim() })) (
        "$DisplayName 的 API 条目不允许首尾空白。")
    Assert-True (-not ($entries | Where-Object { $_.StartsWith('*REMOVED*', [StringComparison]::Ordinal) })) (
        "$DisplayName 不允许使用 *REMOVED* 绕过同一主版本的破坏性变更。")

    $duplicates = @($entries | Group-Object -CaseSensitive | Where-Object Count -gt 1)
    Assert-True ($duplicates.Count -eq 0) (
        "$DisplayName 存在重复条目：$($duplicates.Name -join ', ')")
    $sorted = Get-OrdinalSortedCopy $entries
    $orderDifference = if ($entries.Count -eq 0) {
        $null
    }
    else {
        Compare-Object $entries $sorted -SyncWindow 0
    }
    Assert-True (-not $orderDifference) (
        "$DisplayName 必须按 Ordinal 稳定排序。")
    return $entries
}

# 所有测试副本回写都固定为无 BOM UTF-8，避免编码差异成为
# Analyzer 诊断或文本 diff 的噪声。该函数不会被用于仓库源文件。
function Set-Utf8Text {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Content
    )

    # 测试副本使用无 BOM UTF-8，与仓库源文件和 PublicAPI 文本保持相同编码。
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

# 每个变异都使用完整的旧文本作哨兵，并强制恰好命中一次。
# 如果源码已重构、样例成员消失或命中多处，脚本应立即失败并要求重新审阅用例。
function Replace-ExactlyOnce {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$OldText,
        [Parameter(Mandatory)] [AllowEmptyString()] [string]$NewText
    )

    $content = [IO.File]::ReadAllText($Path)
    $matches = [regex]::Matches($content, [regex]::Escape($OldText)).Count
    Assert-True ($matches -eq 1) (
        "测试变异要求唯一命中，但在 $Path 中找到 $matches 处：$OldText")
    Set-Utf8Text $Path ($content.Replace($OldText, $NewText, [StringComparison]::Ordinal))
}

# 在一个已还原的测试副本中施加单一差异，验证预期失败后无条件恢复原文本。
# 每次负例都从同一份正常 SDK 出发，避免前一个变异污染后一个结论。
function Invoke-MutatedBuildFailure {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$OldText,
        [Parameter(Mandatory)] [AllowEmptyString()] [string]$NewText,
        [Parameter(Mandatory)] [string[]]$ExpectedFragments,
        [Parameter(Mandatory)] [string]$FixtureRoot,
        [Parameter(Mandatory)] [string]$FixtureProject
    )

    $original = [IO.File]::ReadAllText($Path)
    try {
        Replace-ExactlyOnce $Path $OldText $NewText
        Invoke-DotNetFailure $Name @(
            'build', $FixtureProject,
            '-c', $Configuration,
            '--no-restore', '--nologo', '-t:Rebuild'
        ) $ExpectedFragments $FixtureRoot | Out-Null
    }
    finally {
        Set-Utf8Text $Path $original
    }
}

# 创建能独立还原和构建的 SDK 测试副本。除项目源码外，还复制集中版本、
# 包版本、通用构建属性和 global.json，使负例验证的是与真实 SDK 相同的构建协议。
# bin/obj 可能带入仓库旧产物，因此只在确认位于本轮 FixtureRoot 下后删除。
function Copy-CompatibilityFixture {
    param([Parameter(Mandatory)] [string]$FixtureRoot)

    New-Item -ItemType Directory -Path (Join-Path $FixtureRoot 'Host') -Force | Out-Null
    foreach ($file in @(
        'Directory.Build.props',
        'Directory.Packages.props',
        'Directory.Version.props',
        'global.json')) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $file) -Destination $FixtureRoot
    }

    $sourceDirectory = Join-Path $repositoryRoot 'Host\MyAvaloniaManagementCommon'
    $targetDirectory = Join-Path $FixtureRoot 'Host\MyAvaloniaManagementCommon'
    Copy-Item -LiteralPath $sourceDirectory -Destination $targetDirectory -Recurse
    foreach ($generatedDirectory in @('bin', 'obj')) {
        $path = Join-Path $targetDirectory $generatedDirectory
        if (Test-Path -LiteralPath $path) {
            Assert-ChildPath $path $FixtureRoot
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
}

[xml]$versionDocument = Get-Content -Raw -LiteralPath $versionFile
$properties = $versionDocument.Project.PropertyGroup
$activeBaseline = [string]$properties.MyAvaloniaPluginSdkApiBaseline
$sdkVersion = [Version]([string]$properties.MyAvaloniaPluginSdkVersion)
$sdkAssemblyVersion = [Version]([string]$properties.MyAvaloniaPluginSdkAssemblyVersion)
$baselineMajor = [int]$Baseline.Substring(1)

Assert-True ($Baseline -ceq $activeBaseline) (
    "请求基线 $Baseline 与活动基线 $activeBaseline 不一致。请使用集中版本文件声明的活动基线。")
Assert-True ($baselineMajor -eq $sdkVersion.Major) (
    "基线 $Baseline 与 SDK 包版本 $sdkVersion 的主版本不一致。")
Assert-True ($baselineMajor -eq $sdkAssemblyVersion.Major) (
    "基线 $Baseline 与 SDK AssemblyVersion $sdkAssemblyVersion 的主版本不一致。")

$baselineDirectory = Join-Path (
    Split-Path -Parent $sdkProject) "ApiCompatibility\$Baseline"
$shippedPath = Join-Path $baselineDirectory 'PublicAPI.Shipped.txt'
$unshippedPath = Join-Path $baselineDirectory 'PublicAPI.Unshipped.txt'
$shippedEntries = Assert-ApiBaselineFile $shippedPath 'Shipped API 基线'
$unshippedEntries = Assert-ApiBaselineFile $unshippedPath 'Unshipped API 基线'
$crossFileDuplicates = @($shippedEntries | Where-Object { $unshippedEntries -ccontains $_ })
Assert-True ($crossFileDuplicates.Count -eq 0) (
    "Shipped 与 Unshipped 存在重复 API：$($crossFileDuplicates -join ', ')")

Assert-ChildPath $workingRoot $temporaryParent
New-Item -ItemType Directory -Path $workingRoot | Out-Null

try {
    Write-Host "[G13] 验证真实 SDK 与 $Baseline 文本基线。"
    Invoke-DotNetChecked '真实 SDK 锁定还原' @(
        'restore', $sdkProject, '--locked-mode', '--nologo')
    Invoke-DotNetChecked '真实 SDK API 兼容构建' @(
        'build', $sdkProject,
        '-c', $Configuration,
        '--no-restore', '--nologo', '-warnaserror')

    $fixtureRoot = Join-Path $workingRoot 'fixture'
    Copy-CompatibilityFixture $fixtureRoot
    $fixtureProject = Join-Path $fixtureRoot $sdkRelativePath
    Invoke-DotNetChecked '测试副本锁定还原' @(
        'restore', $fixtureProject, '--locked-mode', '--nologo') $fixtureRoot

    $documentLoadException = Join-Path $fixtureRoot (
        'Host\MyAvaloniaManagementCommon\Save\DocumentLoadException.cs')
    $documentLoadExceptionText = [IO.File]::ReadAllText($documentLoadException)
    try {
        Assert-ChildPath $documentLoadException $fixtureRoot
        Remove-Item -LiteralPath $documentLoadException -Force
        Invoke-DotNetFailure '删除 public 类型' @(
            'build', $fixtureProject,
            '-c', $Configuration,
            '--no-restore', '--nologo', '-t:Rebuild'
        ) @('RS0017', 'DocumentLoadException') $fixtureRoot | Out-Null
    }
    finally {
        Set-Utf8Text $documentLoadException $documentLoadExceptionText
    }

    $creationIntentId = Join-Path $fixtureRoot (
        'Host\MyAvaloniaManagementCommon\DocumentCreation\CreationIntentId.cs')
    $parseMember = '    public static CreationIntentId Parse(string value) => new(value);'
    Invoke-MutatedBuildFailure `
        '删除 public 成员' $creationIntentId $parseMember '' `
        @('RS0017', 'CreationIntentId.Parse', 'string! value') `
        $fixtureRoot $fixtureProject

    $toolTypeId = Join-Path $fixtureRoot (
        'Host\MyAvaloniaManagementCommon\ToolCreation\ToolTypeId.cs')
    Invoke-MutatedBuildFailure `
        '收窄 public 可见性' $toolTypeId `
        'public sealed class ToolTypeIdSystemTextJsonConverter' `
        'internal sealed class ToolTypeIdSystemTextJsonConverter' `
        @('RS0017', 'ToolTypeIdSystemTextJsonConverter') `
        $fixtureRoot $fixtureProject

    Invoke-MutatedBuildFailure `
        '修改 public 参数名称' $creationIntentId $parseMember `
        '    public static CreationIntentId Parse(string text) => new(text);' `
        @('RS0017', 'CreationIntentId.Parse', 'string! value') `
        $fixtureRoot $fixtureProject

    Invoke-MutatedBuildFailure `
        '修改 public 参数类型' $creationIntentId $parseMember `
        '    public static CreationIntentId Parse(object value) => new((string)value);' `
        @('RS0017', 'CreationIntentId.Parse', 'string! value') `
        $fixtureRoot $fixtureProject

    Invoke-MutatedBuildFailure `
        '修改 public 参数数量' $creationIntentId $parseMember `
        '    public static CreationIntentId Parse(string value, bool strict) => new(value);' `
        @('RS0017', 'CreationIntentId.Parse', 'string! value') `
        $fixtureRoot $fixtureProject

    Invoke-MutatedBuildFailure `
        '修改 public 返回类型' $creationIntentId $parseMember `
        '    public static object Parse(string value) => new CreationIntentId(value);' `
        @('RS0017', 'CreationIntentId.Parse', 'CreationIntentId!') `
        $fixtureRoot $fixtureProject

    $originalCreationIntentId = [IO.File]::ReadAllText($creationIntentId)
    $fixtureUnshipped = Join-Path $fixtureRoot (
        "Host\MyAvaloniaManagementCommon\ApiCompatibility\$Baseline\PublicAPI.Unshipped.txt")
    $originalUnshipped = [IO.File]::ReadAllText($fixtureUnshipped)
    $probeSource = @'

    /// <summary>仅用于证明兼容新增必须经过显式 API 审阅。</summary>
    public static string G13CompatibilityProbe() => "ok";
'@
    try {
        $recordTail = "    public override string ToString() => Value;`r`n}"
        Replace-ExactlyOnce $creationIntentId $recordTail (
            "    public override string ToString() => Value;$probeSource`r`n}")
        Invoke-DotNetFailure '未登记的兼容新增' @(
            'build', $fixtureProject,
            '-c', $Configuration,
            '--no-restore', '--nologo', '-t:Rebuild'
        ) @('RS0016', 'G13CompatibilityProbe') $fixtureRoot | Out-Null

        $probeApi = 'static MyAvaloniaManagementCommon.DocumentCreation.CreationIntentId.G13CompatibilityProbe() -> string!'
        $registeredLines = @(
            Get-Content -LiteralPath $fixtureUnshipped
            $probeApi
        )
        Set-Utf8Text $fixtureUnshipped (($registeredLines -join "`r`n") + "`r`n")
        Invoke-DotNetChecked '显式登记后的兼容新增' @(
            'build', $fixtureProject,
            '-c', $Configuration,
            '--no-restore', '--nologo', '-t:Rebuild') $fixtureRoot
    }
    finally {
        Set-Utf8Text $creationIntentId $originalCreationIntentId
        Set-Utf8Text $fixtureUnshipped $originalUnshipped
    }

    Write-Host (
        "[G13] 通过：Shipped=$($shippedEntries.Count)，" +
        "Unshipped=$($unshippedEntries.Count)，7 个破坏性负例和 1 组兼容新增审阅流程均符合预期。")
}
finally {
    if (Test-Path -LiteralPath $workingRoot) {
        # 只删除本次 GUID 命名的系统 Temp 子目录；仓库、Temp 根和其他任务目录均不在删除范围。
        Assert-ChildPath $workingRoot $temporaryParent
        Remove-Item -LiteralPath $workingRoot -Recurse -Force
    }
}
