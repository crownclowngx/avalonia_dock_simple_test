Set-StrictMode -Version Latest

# 本模块只保存可复用、可在临时夹具中验证的文档规则。仓库文件枚举、策略选择和
# 摘要落盘由入口脚本负责，避免“如何找到文件”与“如何判断事实”混成一个职责。

function Assert-DocumentationCondition {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-DocumentationChildPath {
    param(
        [Parameter(Mandatory)] [string]$Candidate,
        [Parameter(Mandatory)] [string]$Parent,
        [Parameter(Mandatory)] [string]$Purpose
    )

    $candidatePath = [IO.Path]::GetFullPath($Candidate)
    $parentPath = [IO.Path]::GetFullPath($Parent).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    Assert-DocumentationCondition (
        $candidatePath.StartsWith($parentPath, [StringComparison]::OrdinalIgnoreCase)) (
        "$Purpose 位于允许根之外：$candidatePath；允许根：$parentPath")
}

function Remove-DocumentationOwnedTree {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$AllowedParent
    )

    Assert-DocumentationChildPath -Candidate $Path -Parent $AllowedParent -Purpose '文档门禁临时目录'
    if (Test-Path -LiteralPath $Path) {
        # Windows 上只读夹具不能直接递归删除。这里只处理已经通过父目录哨兵验证的
        # 本轮临时树，不触碰仓库文件或调用方提供的任意目录。
        Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue |
            ForEach-Object { $_.Attributes = [IO.FileAttributes]::Normal }
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Get-DocumentationMarkdownLinks {
    param(
        [Parameter(Mandatory)] [string]$Text,
        [Parameter(Mandatory)] [string]$SourcePath
    )

    $results = [Collections.Generic.List[object]]::new()
    $lines = $Text -split "`r?`n"
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        # 同时覆盖普通链接和图片。目标允许使用 <含空格路径>；标题部分不是路径，
        # 因而有意不交给文件系统校验。
        foreach ($match in [regex]::Matches(
                $line,
                '!?' + '\[[^\]]*\]\(\s*(?<target><[^>]+>|[^\s\)]+)(?:\s+[^\)]*)?\)')) {
            $results.Add([pscustomobject]@{
                    SourcePath = $SourcePath
                    Line = $index + 1
                    Target = $match.Groups['target'].Value.Trim('<', '>')
                })
        }

        # 引用式链接的定义单独处理；使用方本身没有路径，不能重复计数。
        $reference = [regex]::Match(
            $line,
            '^\s*\[[^\]]+\]:\s*(?<target><[^>]+>|[^\s]+)')
        if ($reference.Success) {
            $results.Add([pscustomobject]@{
                    SourcePath = $SourcePath
                    Line = $index + 1
                    Target = $reference.Groups['target'].Value.Trim('<', '>')
                })
        }
    }
    return $results.ToArray()
}

function Test-DocumentationExternalTarget {
    param([Parameter(Mandatory)] [string]$Target)

    return $Target.StartsWith('#', [StringComparison]::Ordinal) -or
        $Target.StartsWith('//', [StringComparison]::Ordinal) -or
        [regex]::IsMatch($Target, '^[A-Za-z][A-Za-z0-9+.-]*:')
}

function Resolve-DocumentationLinkTarget {
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)] [string]$SourcePath,
        [Parameter(Mandatory)] [string]$Target
    )

    if (Test-DocumentationExternalTarget $Target) {
        return $null
    }

    # Markdown 片段和查询参数不属于磁盘路径。先移除它们，再做 URI 解码，避免
    # `%23` 这类合法文件名被错误当作锚点分隔符。
    $pathPart = ($Target -split '[#?]', 2)[0]
    $pathPart = [Uri]::UnescapeDataString($pathPart)
    if ([string]::IsNullOrWhiteSpace($pathPart)) {
        return $null
    }

    $sourceFullPath = if ([IO.Path]::IsPathRooted($SourcePath)) {
        [IO.Path]::GetFullPath($SourcePath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $SourcePath))
    }
    $candidate = if ($pathPart.StartsWith('/', [StringComparison]::Ordinal)) {
        Join-Path $RepositoryRoot $pathPart.TrimStart('/')
    }
    else {
        Join-Path (Split-Path -Parent $sourceFullPath) $pathPart
    }
    $candidate = [IO.Path]::GetFullPath($candidate)
    Assert-DocumentationChildPath -Candidate $candidate -Parent $RepositoryRoot -Purpose (
        "文档链接 $SourcePath -> $Target")
    return $candidate
}

function Assert-DocumentationLinks {
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)] [object[]]$Links,
        [Parameter(Mandatory)] [string[]]$TrackedPaths
    )

    $tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $TrackedPaths) {
        [void]$tracked.Add($path.Replace('\', '/').TrimStart('./'))
    }

    $checked = 0
    foreach ($link in $Links) {
        $targetPath = Resolve-DocumentationLinkTarget `
            -RepositoryRoot $RepositoryRoot `
            -SourcePath $link.SourcePath `
            -Target $link.Target
        if ($null -eq $targetPath) { continue }

        Assert-DocumentationCondition (Test-Path -LiteralPath $targetPath) (
            "$($link.SourcePath):$($link.Line) 的本地链接不存在：$($link.Target)")
        if (Test-Path -LiteralPath $targetPath -PathType Leaf) {
            $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $targetPath).Replace('\', '/')
            Assert-DocumentationCondition ($tracked.Contains($relative)) (
                "$($link.SourcePath):$($link.Line) 的链接大小写或 Git 跟踪状态不正确：$relative")
        }
        $checked++
    }
    return $checked
}

function Get-DocumentationCommandPaths {
    param(
        [Parameter(Mandatory)] [string]$Text,
        [Parameter(Mandatory)] [string]$SourcePath
    )

    $results = [Collections.Generic.List[object]]::new()
    $lines = $Text -split "`r?`n"
    for ($index = 0; $index -lt $lines.Count; $index++) {
        foreach ($match in [regex]::Matches(
                $lines[$index],
                '(?<path>(?:\.\\|\.\/)?scripts[\\/][A-Za-z0-9_.-]+\.ps1)')) {
            $results.Add([pscustomobject]@{
                    SourcePath = $SourcePath
                    Line = $index + 1
                    Path = $match.Groups['path'].Value
                })
        }
    }
    return $results.ToArray()
}

function Assert-DocumentationCommandPaths {
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)] [object[]]$Commands,
        [Parameter(Mandatory)] [string[]]$TrackedPaths
    )

    $tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $TrackedPaths) { [void]$tracked.Add($path.Replace('\', '/')) }
    foreach ($command in $Commands) {
        $relative = $command.Path -replace '^\.[\\/]', '' -replace '\\', '/'
        $fullPath = Join-Path $RepositoryRoot $relative
        Assert-DocumentationCondition (Test-Path -LiteralPath $fullPath -PathType Leaf) (
            "$($command.SourcePath):$($command.Line) 引用的脚本不存在：$($command.Path)")
        Assert-DocumentationCondition ($tracked.Contains($relative)) (
            "$($command.SourcePath):$($command.Line) 引用的脚本大小写或 Git 跟踪状态不正确：$relative")
    }
    return $Commands.Count
}

function Get-DocumentationProjectPaths {
    param(
        [Parameter(Mandatory)] [string]$Text,
        [Parameter(Mandatory)] [string]$SourcePath
    )

    $results = [Collections.Generic.List[object]]::new()
    $lines = $Text -split "`r?`n"
    for ($index = 0; $index -lt $lines.Count; $index++) {
        foreach ($match in [regex]::Matches(
                $lines[$index],
                '(?<path>(?:Host|Plugins)[\\/][A-Za-z0-9_.\\/-]+\.csproj)')) {
            $results.Add([pscustomobject]@{
                    SourcePath = $SourcePath
                    Line = $index + 1
                    Path = $match.Groups['path'].Value.Replace('\', '/')
                })
        }
    }
    return $results.ToArray()
}

function Assert-DocumentationProjectPaths {
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)] [object[]]$Projects,
        [Parameter(Mandatory)] [string[]]$TrackedPaths
    )

    $tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $TrackedPaths) { [void]$tracked.Add($path.Replace('\', '/')) }
    foreach ($project in $Projects) {
        $fullPath = Join-Path $RepositoryRoot $project.Path
        Assert-DocumentationCondition (Test-Path -LiteralPath $fullPath -PathType Leaf) (
            "$($project.SourcePath):$($project.Line) 引用的项目不存在：$($project.Path)")
        Assert-DocumentationCondition ($tracked.Contains($project.Path)) (
            "$($project.SourcePath):$($project.Line) 引用的项目大小写或 Git 候选状态不正确：$($project.Path)")
    }
    return $Projects.Count
}

function Assert-DocumentationForbiddenStatements {
    param(
        [Parameter(Mandatory)] [object[]]$Documents,
        [Parameter(Mandatory)] [object[]]$Rules
    )

    foreach ($document in $Documents) {
        $lines = $document.Text -split "`r?`n"
        foreach ($rule in $Rules) {
            for ($index = 0; $index -lt $lines.Count; $index++) {
                if ([regex]::IsMatch($lines[$index], $rule.Pattern)) {
                    throw "$($document.Path):$($index + 1) 命中过期表述规则 '$($rule.Name)'：$($lines[$index].Trim())"
                }
            }
        }
    }
}

function Assert-DocumentationSourceSymbols {
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)] [object[]]$RequiredSymbols,
        [Parameter(Mandatory)] [string[]]$ForbiddenSymbols,
        [Parameter(Mandatory)] [string[]]$ProductionFiles
    )

    foreach ($policy in $RequiredSymbols) {
        $path = Join-Path $RepositoryRoot $policy.Path
        Assert-DocumentationCondition (Test-Path -LiteralPath $path -PathType Leaf) (
            "当前类型 $($policy.Symbol) 的事实文件不存在：$($policy.Path)")
        $text = [IO.File]::ReadAllText($path)
        Assert-DocumentationCondition (
            [regex]::IsMatch($text, "\b$([regex]::Escape($policy.Symbol))\b")) (
            "当前类型 $($policy.Symbol) 未在事实文件中声明：$($policy.Path)")
    }

    foreach ($relativePath in $ProductionFiles) {
        $text = [IO.File]::ReadAllText((Join-Path $RepositoryRoot $relativePath))
        foreach ($symbol in $ForbiddenSymbols) {
            Assert-DocumentationCondition (-not [regex]::IsMatch(
                    $text,
                    "\b$([regex]::Escape($symbol))\b")) (
                "已删除的候选 SDK 类型 $symbol 重新出现在生产源码：$relativePath")
        }
    }
}

function Get-ManagementBaselineFacts {
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)] [string[]]$PluginProjects
    )

    $versionPath = Join-Path $RepositoryRoot 'Directory.Version.props'
    Assert-DocumentationCondition (Test-Path -LiteralPath $versionPath -PathType Leaf) (
        "集中版本文件不存在：$versionPath")
    [xml]$versionDocument = Get-Content -Raw -LiteralPath $versionPath
    $properties = $versionDocument.Project.PropertyGroup
    $productVersion = [Version]([string]$properties.MyAvaloniaProductVersion)
    $sdkVersion = [Version]([string]$properties.MyAvaloniaPluginSdkVersion)
    $hostAssemblyVersion = [Version]([string]$properties.MyAvaloniaProductAssemblyVersion)
    $sdkAssemblyVersion = [Version]([string]$properties.MyAvaloniaPluginSdkAssemblyVersion)
    $sdkNextMajorVersion = [Version]([string]$properties.MyAvaloniaPluginSdkNextMajorVersion)
    $apiBaseline = [string]$properties.MyAvaloniaPluginSdkApiBaseline

    Assert-DocumentationCondition ($productVersion -eq $sdkVersion) (
        "当前阶段要求产品版本与 SDK 版本一致，实际为 $productVersion / $sdkVersion。")
    $legacyHostApiVersionNode = $versionDocument.SelectSingleNode(
        '/Project/PropertyGroup/MyAvaloniaHostApiAssemblyVersion')
    Assert-DocumentationCondition ($null -eq $legacyHostApiVersionNode) (
        'V3 不得重新声明独立 MyAvaloniaHostApiAssemblyVersion。')
    $expectedHostAssemblyVersion = [Version]::new(
        $productVersion.Major, $productVersion.Minor, $productVersion.Build, 0)
    Assert-DocumentationCondition ($hostAssemblyVersion -eq $expectedHostAssemblyVersion) (
        "Host AssemblyVersion 与产品版本不一致。")
    Assert-DocumentationCondition ($sdkAssemblyVersion.Major -eq $sdkVersion.Major) (
        "SDK AssemblyVersion 主版本与包版本不一致。")
    Assert-DocumentationCondition (
        $sdkNextMajorVersion -eq [Version]::new($sdkVersion.Major + 1, 0, 0)) (
        "SDK 下一主版本 $sdkNextMajorVersion 与当前版本 $sdkVersion 不连续。")
    Assert-DocumentationCondition ($apiBaseline -ceq "v$($sdkVersion.Major)") (
        "活动 API 基线 $apiBaseline 与 SDK 主版本 $($sdkVersion.Major) 不一致。")

    # V3 尚未发布，因此活动 Shipped 必须为空，全部当前签名进入 Unshipped。V2 Shipped 继续作为
    # 历史正式承诺保留；Core 允许 G2–G5 已评审的破坏式变化，UI 只允许 G8 把两个 owner 方法
    # 原子替换为一个返回 IDisposable 租约的方法。除此之外仍逐条相等，避免借阶段更新改写历史表面。
    $sdkApiRoots = @(
        Join-Path $RepositoryRoot "Host\MyAvaloniaManagement.PluginSdk\ApiCompatibility\$apiBaseline"
        Join-Path $RepositoryRoot "Host\MyAvaloniaManagement.PluginSdk.UI\ApiCompatibility\$apiBaseline"
    )
    $allShippedEntries = [Collections.Generic.List[string]]::new()
    $allUnshippedEntries = [Collections.Generic.List[string]]::new()
    $apiCounts = [Collections.Generic.List[object]]::new()
    foreach ($baselineRoot in $sdkApiRoots) {
        $shippedPath = Join-Path $baselineRoot 'PublicAPI.Shipped.txt'
        $unshippedPath = Join-Path $baselineRoot 'PublicAPI.Unshipped.txt'
        foreach ($path in @($shippedPath, $unshippedPath)) {
            Assert-DocumentationCondition (Test-Path -LiteralPath $path -PathType Leaf) (
                "活动 API 基线文件不存在：$path")
            Assert-DocumentationCondition (@(Get-Content -LiteralPath $path)[0] -ceq '#nullable enable') (
                "API 基线文件缺少 nullable 头：$path")
        }
        $shippedEntries = @(Get-Content -LiteralPath $shippedPath | Select-Object -Skip 1 |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $unshippedEntries = @(Get-Content -LiteralPath $unshippedPath | Select-Object -Skip 1 |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        Assert-DocumentationCondition ($shippedEntries.Count -eq 0) (
            "G1 未发布 V3 SDK 的 Shipped 必须为空：$baselineRoot")
        Assert-DocumentationCondition ($unshippedEntries.Count -gt 0) (
            "G1 未发布 V3 SDK 的 Unshipped 不能为空：$baselineRoot")

        $v2Root = Join-Path (Split-Path $baselineRoot -Parent) 'v2'
        $v2Shipped = @(Get-Content -LiteralPath (Join-Path $v2Root 'PublicAPI.Shipped.txt') |
                Select-Object -Skip 1 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $v2Unshipped = @(Get-Content -LiteralPath (Join-Path $v2Root 'PublicAPI.Unshipped.txt') |
                Select-Object -Skip 1 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        Assert-DocumentationCondition ($v2Shipped.Count -gt 0 -and $v2Unshipped.Count -eq 0) (
            "V2 历史 API 必须保持 Shipped 非空且 Unshipped 为空：$v2Root")
        $isG3Core = $unshippedEntries -contains (
            'MyAvaloniaManagement.PluginSdk.NewDocumentActivation')
        if ($isG3Core) {
            Assert-DocumentationCondition ($v2Shipped.Count -eq 85) (
                "G3 不得改写 V2 Core Shipped 数量：$baselineRoot")
            Assert-DocumentationCondition ($unshippedEntries.Count -eq 127) (
                "G5 Core v3 Unshipped 必须为删除通用事件总线后的 127 条：$baselineRoot")
            Assert-DocumentationCondition (
                $unshippedEntries -contains (
                    'MyAvaloniaManagement.PluginSdk.IPersistablePluginDocument.AcceptChanges(MyAvaloniaManagement.PluginSdk.DocumentRevision savedRevision) -> void')) (
                "G5 Core v3 缺少既有指定修订确认：$baselineRoot")
            Assert-DocumentationCondition (
                -not ($unshippedEntries -match 'CaptureContentAsync|AcceptChanges\(\)')) (
                "G5 Core v3 不得保留旧保存协议：$baselineRoot")
            Assert-DocumentationCondition (
                $unshippedEntries -contains (
                    'MyAvaloniaManagement.PluginSdk.RestoreDocumentActivation')) (
                "G5 Core v3 缺少 RestoreDocumentActivation：$baselineRoot")
            Assert-DocumentationCondition (
                -not ($unshippedEntries -match 'DocumentActivationContext')) (
                "G5 Core v3 不得保留旧可空组合激活类型：$baselineRoot")
        }
        elseif ($unshippedEntries -contains (
                'MyAvaloniaManagement.PluginSdk.UI.IWindowContentFullscreenHost.TryPresent(Avalonia.Controls.Control! content) -> System.IDisposable?')) {
            Assert-DocumentationCondition ($v2Shipped.Count -eq 46) (
                "G8 不得改写 V2 UI Shipped 数量：$baselineRoot")
            Assert-DocumentationCondition ($unshippedEntries.Count -eq 45) (
                "G8 UI v3 Unshipped 必须为全屏租约收口后的 45 条：$baselineRoot")
            Assert-DocumentationCondition (
                -not ($unshippedEntries -match 'TryRestore|TryPresent\(Avalonia\.Controls\.Control! content, System\.Object! owner\)')) (
                "G8 UI v3 不得保留 owner 全屏 API：$baselineRoot")

            $v2WithoutFullscreenOwner = @($v2Shipped | Where-Object {
                    $_ -notmatch 'IWindowContentFullscreenHost\.(TryPresent|TryRestore)'
                })
            $v3WithoutFullscreenLease = @($unshippedEntries | Where-Object {
                    $_ -notmatch 'IWindowContentFullscreenHost\.TryPresent'
                })
            Assert-DocumentationCondition (
                ($v2WithoutFullscreenOwner -join "`n") -ceq ($v3WithoutFullscreenLease -join "`n")) (
                "G8 UI 除全屏租约替换外必须与 V2 Shipped 逐条一致：$baselineRoot")
        }
        else {
            # 最小测试夹具仍走此分支，以继续验证没有阶段变化时的逐条投影规则。
            Assert-DocumentationCondition (
                ($v2Shipped -join "`n") -ceq ($unshippedEntries -join "`n")) (
                "未发生已评审协议变更的 V3 Unshipped 必须与 V2 Shipped 完全一致：$baselineRoot")
        }
        $allShippedEntries.AddRange([string[]]$shippedEntries)
        $allUnshippedEntries.AddRange([string[]]$unshippedEntries)
        $apiCounts.Add([pscustomobject]@{
                Project = Split-Path (Split-Path (Split-Path $baselineRoot -Parent) -Parent) -Leaf
                Shipped = $shippedEntries.Count
                Unshipped = $unshippedEntries.Count
            })
    }

    # V1 的 243 条历史正式承诺只保存在 Core SDK 历史目录中，不复制到 UI 或 Legacy 桥。
    $v1Root = Join-Path $RepositoryRoot 'Host\MyAvaloniaManagement.PluginSdk\ApiCompatibility\v1'
    $v1Shipped = @(Get-Content -LiteralPath (Join-Path $v1Root 'PublicAPI.Shipped.txt') |
            Select-Object -Skip 1 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $v1Unshipped = @(Get-Content -LiteralPath (Join-Path $v1Root 'PublicAPI.Unshipped.txt') |
            Select-Object -Skip 1 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-DocumentationCondition ($v1Shipped.Count -gt 0 -and $v1Unshipped.Count -eq 0) (
        'V1 历史 API 基线必须保持 Shipped 非空且 Unshipped 为空。')

    $expectedMaximum = $sdkNextMajorVersion
    $plugins = [Collections.Generic.List[object]]::new()
    foreach ($relativePath in $PluginProjects) {
        $projectPath = Join-Path $RepositoryRoot $relativePath
        Assert-DocumentationCondition (Test-Path -LiteralPath $projectPath -PathType Leaf) (
            "Managed Plugin 项目不存在：$relativePath")
        [xml]$project = Get-Content -Raw -LiteralPath $projectPath
        # 真实项目通常有多个 PropertyGroup，且其中部分组不含插件属性。不能依赖
        # PowerShell 对 XML 节点集合的隐式成员展开，否则 StrictMode 会把第一个缺失成员误判为失败。
        $readProjectProperty = {
            param([Parameter(Mandatory)] [string]$Name)
            $nodes = @($project.SelectNodes("/Project/PropertyGroup/$Name"))
            Assert-DocumentationCondition ($nodes.Count -eq 1) (
                "$relativePath 必须且只能声明一个 $Name，实际为 $($nodes.Count) 个。")
            return [string]$nodes[0].InnerText.Trim()
        }
        Assert-DocumentationCondition (
            (& $readProjectProperty 'ManagedPlugin') -ceq 'true') (
            "$relativePath 没有声明 ManagedPlugin=true。")
        $pluginVersion = [Version](& $readProjectProperty 'PluginVersion')
        $entryType = & $readProjectProperty 'ManagedPluginEntryType'
        $sdkMinExpression = & $readProjectProperty 'ManagedPluginSdkMinInclusive'
        $sdkMaxExpression = & $readProjectProperty 'ManagedPluginSdkMaxExclusive'
        Assert-DocumentationCondition ($pluginVersion -eq $sdkVersion) (
            "$relativePath 的插件版本 $pluginVersion 与当前 SDK $sdkVersion 不一致。")
        Assert-DocumentationCondition (
            $entryType -cmatch '^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$') (
            "$relativePath 的 ManagedPluginEntryType 不是规范的命名空间限定类型全名。")
        Assert-DocumentationCondition (
            $sdkMinExpression -ceq '$(MyAvaloniaPluginSdkVersion)' -and
            $sdkMaxExpression -ceq '$(MyAvaloniaPluginSdkNextMajorVersion)') (
            "$relativePath 的 Managed Plugin SDK 兼容字段没有投影集中 SDK 区间。")
        $plugins.Add([pscustomobject]@{
                Project = $relativePath.Replace('\', '/')
                Version = $pluginVersion.ToString(3)
                EntryPoint = $entryType
                SdkRange = "[$($sdkVersion.ToString(3)), $($expectedMaximum.ToString(3)))"
            })
    }

    return [pscustomobject]@{
        ProductVersion = $productVersion.ToString(3)
        SdkVersion = $sdkVersion.ToString(3)
        HostAssemblyVersion = $hostAssemblyVersion.ToString(4)
        SdkAssemblyVersion = $sdkAssemblyVersion.ToString(4)
        ApiBaseline = $apiBaseline
        ShippedEntries = $allShippedEntries.Count
        UnshippedEntries = $allUnshippedEntries.Count
        ApiProjects = $apiCounts.ToArray()
        Plugins = $plugins.ToArray()
    }
}

Export-ModuleMember -Function @(
    'Assert-DocumentationCondition',
    'Assert-DocumentationChildPath',
    'Remove-DocumentationOwnedTree',
    'Get-DocumentationMarkdownLinks',
    'Test-DocumentationExternalTarget',
    'Resolve-DocumentationLinkTarget',
    'Assert-DocumentationLinks',
    'Get-DocumentationCommandPaths',
    'Assert-DocumentationCommandPaths',
    'Get-DocumentationProjectPaths',
    'Assert-DocumentationProjectPaths',
    'Assert-DocumentationForbiddenStatements',
    'Assert-DocumentationSourceSymbols',
    'Get-ManagementBaselineFacts')
