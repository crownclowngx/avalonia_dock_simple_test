Set-StrictMode -Version Latest

# G14 只允许清理或覆盖调用方明确拥有的子目录。这里先规范化绝对路径，再比较带末尾
# 分隔符的父目录前缀，避免 C:\Temp2 被误判成 C:\Temp 的子目录。
function Assert-HostV1GateChildPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$Candidate,
        [Parameter(Mandatory)] [string]$Parent,
        [string]$Purpose = '文件操作'
    )

    $resolvedCandidate = [IO.Path]::GetFullPath($Candidate)
    $resolvedParent = [IO.Path]::GetFullPath($Parent).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedCandidate.StartsWith(
            $resolvedParent,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose 拒绝操作允许根之外的路径：$resolvedCandidate；允许根：$resolvedParent"
    }
}

# Git 与 NuGet 包中可能带有只读文件。直接 Remove-Item -Force 在部分 Windows 文件系统上仍会
# 因只读属性失败，所以清理前必须在“已经证明属于允许根”的前提下移除该属性。这个函数只处理
# 门禁自己创建的目录，不接受模糊匹配或通配符；路径所有权判断与实际删除因此保持在同一处。
function Remove-HostV1GateOwnedTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$AllowedParent,
        [string]$Purpose = '临时目录清理'
    )

    Assert-HostV1GateChildPath -Candidate $Path -Parent $AllowedParent -Purpose $Purpose
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    # 先处理子项、最后处理根目录，避免只读目录阻止递归删除。这里使用 LiteralPath，防止
    # 文件名中的 [] 等字符被 PowerShell 当作通配符解释。
    $lastError = $null
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        foreach ($item in @(Get-ChildItem -LiteralPath $Path -Recurse -Force) + @(Get-Item -LiteralPath $Path -Force)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReadOnly) -ne 0) {
                $item.Attributes = $item.Attributes -band (-bnot [IO.FileAttributes]::ReadOnly)
            }
        }
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            $lastError = $_
            if ($attempt -lt 20) {
                # Windows 在子进程退出后可能仍需极短时间释放已加载的 MSBuild/Avalonia DLL。
                # 重试总等待不超过 10 秒，既吸收杀毒/索引器的正常释放延迟，也不会无限隐藏占用。
                Start-Sleep -Milliseconds 500
            }
        }
    }
    throw $lastError
}

# 所有机器可读证据统一写成无 BOM UTF-8。发布证据会被不同工具读取，固定编码可以避免
# PowerShell 版本或系统区域设置把无意义的编码差异带入两轮比较。
function Write-HostV1GateJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] $Value
    )

    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    [IO.File]::WriteAllText(
        $Path,
        ($Value | ConvertTo-Json -Depth 32),
        [Text.UTF8Encoding]::new($false))
}

# 阶段执行器只处理顺序、状态和失败即停止，不知道 restore、测试或打包的内部实现。
# 每个叶子脚本仍拥有自己的业务断言；这里持续落盘阶段状态，使进程中途失败也不会丢失证据。
function Invoke-HostV1GateStageSequence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object[]]$Stages,
        [Parameter(Mandatory)] [string]$StatePath
    )

    $results = [Collections.Generic.List[object]]::new()
    foreach ($stage in $Stages) {
        $name = [string]$stage.Name
        if ([string]::IsNullOrWhiteSpace($name) -or $stage.Action -isnot [scriptblock]) {
            throw '门禁阶段必须同时提供非空 Name 和 ScriptBlock Action。'
        }

        $started = [DateTime]::UtcNow
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        Write-Host "`n[G14] 开始阶段：$name"
        try {
            # 阶段日志直接送到 Host/Transcript，不能混入函数返回的结构化阶段数组。
            & $stage.Action | Out-Host
            $stopwatch.Stop()
            $results.Add([ordered]@{
                name = $name
                status = 'passed'
                startedAtUtc = $started.ToString('O')
                durationMilliseconds = $stopwatch.ElapsedMilliseconds
                error = $null
            })
            Write-Host "[G14] 阶段通过：$name"
            Write-HostV1GateJson -Path $StatePath -Value @($results)
        }
        catch {
            $stopwatch.Stop()
            $results.Add([ordered]@{
                name = $name
                status = 'failed'
                startedAtUtc = $started.ToString('O')
                durationMilliseconds = $stopwatch.ElapsedMilliseconds
                error = $_.Exception.Message
            })
            Write-HostV1GateJson -Path $StatePath -Value @($results)
            throw "G14 阶段 '$name' 失败：$($_.Exception.Message)"
        }
    }

    return @($results)
}

# 规范化结果时只保留具有发布语义的事实。时间、耗时、临时路径和日志位置属于执行环境噪声，
# 不能导致两次等价门禁互相否定；测试数量、覆盖率、API 基线、包摘要和 Smoke 则必须完全一致。
function ConvertTo-HostV1GateCanonicalEvidence {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $Summary)

    return [ordered]@{
        schemaVersion = $Summary.schemaVersion
        sourceRevision = $Summary.sourceRevision
        sourceTree = $Summary.sourceTree
        platform = [ordered]@{
            operatingSystem = $Summary.platform.operatingSystem
            architecture = $Summary.platform.architecture
            configuration = $Summary.platform.configuration
        }
        sdkVersion = $Summary.sdkVersion
        stages = @($Summary.stages | ForEach-Object {
            [ordered]@{ name = $_.name; status = $_.status }
        })
        host = $Summary.host
        sdkPackage = $Summary.sdkPackage
        sdkApi = $Summary.sdkApi
        managedPlugins = $Summary.managedPlugins
        windowsSmoke = $Summary.windowsSmoke
    }
}

function Get-HostV1GatePropertyMap {
    param([Parameter(Mandatory)] $Value)

    $map = [ordered]@{}
    if ($Value -is [Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            $map[[string]$key] = $Value[$key]
        }
        return $map
    }

    foreach ($property in $Value.PSObject.Properties) {
        $map[$property.Name] = $property.Value
    }
    return $map
}

# 递归比较用于返回第一处语义差异的 JSON 路径。仅比较压缩 JSON 虽然更短，却会让失败诊断
# 只显示一大段文本；逐字段诊断能直接告诉维护者是覆盖率、测试数还是某个 ZIP 摘要发生漂移。
function Find-HostV1GateDifference {
    param(
        $Left,
        $Right,
        [string]$Path = '$'
    )

    if ($null -eq $Left -or $null -eq $Right) {
        if ($null -eq $Left -and $null -eq $Right) { return $null }
        return "$Path：左值='$Left'，右值='$Right'"
    }

    $leftIsSequence = $Left -is [Collections.IEnumerable] -and $Left -isnot [string] -and
        $Left -isnot [Collections.IDictionary] -and $Left -isnot [pscustomobject]
    $rightIsSequence = $Right -is [Collections.IEnumerable] -and $Right -isnot [string] -and
        $Right -isnot [Collections.IDictionary] -and $Right -isnot [pscustomobject]
    if ($leftIsSequence -or $rightIsSequence) {
        if (-not ($leftIsSequence -and $rightIsSequence)) {
            return "$Path：一侧是集合，另一侧不是集合。"
        }
        $leftItems = @($Left)
        $rightItems = @($Right)
        if ($leftItems.Count -ne $rightItems.Count) {
            return "$Path.length：左值=$($leftItems.Count)，右值=$($rightItems.Count)"
        }
        for ($index = 0; $index -lt $leftItems.Count; $index++) {
            $difference = Find-HostV1GateDifference $leftItems[$index] $rightItems[$index] "$Path[$index]"
            if ($difference) { return $difference }
        }
        return $null
    }

    $leftIsObject = $Left -is [Collections.IDictionary] -or $Left -is [pscustomobject]
    $rightIsObject = $Right -is [Collections.IDictionary] -or $Right -is [pscustomobject]
    if ($leftIsObject -or $rightIsObject) {
        if (-not ($leftIsObject -and $rightIsObject)) {
            return "$Path：一侧是对象，另一侧不是对象。"
        }
        $leftMap = Get-HostV1GatePropertyMap $Left
        $rightMap = Get-HostV1GatePropertyMap $Right
        $leftKeys = @($leftMap.Keys)
        $rightKeys = @($rightMap.Keys)
        if (($leftKeys -join "`n") -cne ($rightKeys -join "`n")) {
            return "$Path：字段集合或顺序不同；左='$($leftKeys -join ',')'，右='$($rightKeys -join ',')'"
        }
        foreach ($key in $leftKeys) {
            $difference = Find-HostV1GateDifference $leftMap[$key] $rightMap[$key] "$Path.$key"
            if ($difference) { return $difference }
        }
        return $null
    }

    if ([string]$Left -cne [string]$Right) {
        return "$Path：左值='$Left'，右值='$Right'"
    }
    return $null
}

function Assert-HostV1GateEvidenceEqual {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $First,
        [Parameter(Mandatory)] $Second
    )

    $left = ConvertTo-HostV1GateCanonicalEvidence $First
    $right = ConvertTo-HostV1GateCanonicalEvidence $Second
    $difference = Find-HostV1GateDifference $left $right
    if ($difference) {
        throw "G14 两轮发布证据不一致：$difference"
    }
}

# 仅有 summary.json 还不足以审计一次发布门禁。本函数把汇总中的插件列表反向映射到最终
# ZIP/外置清单，并同时要求三套 TRX、覆盖率、Smoke 和完整 transcript 均真实存在。
function Assert-HostV1GateArtifacts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$PassRoot,
        [Parameter(Mandatory)] $Summary
    )

    $requiredFiles = @(
        'pass.log',
        'MyAvaloniaManagement\summary.json',
        'MyAvaloniaManagement\Unit\Unit.trx',
        'MyAvaloniaManagement\UI\UI.trx',
        'MyAvaloniaManagement\Plugin\Plugin.trx',
        'MyAvaloniaManagement\coverage\Cobertura.xml',
        'ManagedPluginPackages\summary.json',
        'WindowsSmoke\summary.json'
    )
    foreach ($relativePath in $requiredFiles) {
        $path = Join-Path $PassRoot $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "G14 缺少必需发布证据：$relativePath"
        }
    }

    foreach ($plugin in @($Summary.managedPlugins.plugins)) {
        $archiveName = [string]$plugin.archive.file
        if ([string]::IsNullOrWhiteSpace($archiveName)) {
            throw "G14 插件 $($plugin.pluginId) 的汇总缺少 archive.file。"
        }
        $manifestName = [string]$plugin.manifest.file
        if ([string]::IsNullOrWhiteSpace($manifestName)) {
            throw "G14 插件 $($plugin.pluginId) 的汇总缺少 manifest.file。"
        }
        foreach ($entry in @(
                @{ Name = $archiveName; Expected = $plugin.archive },
                @{ Name = $manifestName; Expected = $plugin.manifest })) {
            $name = [string]$entry.Name
            $path = Join-Path (Join-Path $PassRoot 'ManagedPluginPackages') $name
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "G14 插件 $($plugin.pluginId) 缺少最终包证据：$name"
            }
            $file = Get-Item -LiteralPath $path
            $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            if ($file.Length -ne [long]$entry.Expected.length) {
                throw "G14 插件 $($plugin.pluginId) 的 $name 长度与汇总不一致。"
            }
            if ($actualHash -cne [string]$entry.Expected.sha256) {
                throw "G14 插件 $($plugin.pluginId) 的 $name SHA-256 与汇总不一致。"
            }
        }
    }
}

Export-ModuleMember -Function @(
    'Assert-HostV1GateChildPath',
    'Remove-HostV1GateOwnedTree',
    'Write-HostV1GateJson',
    'Invoke-HostV1GateStageSequence',
    'ConvertTo-HostV1GateCanonicalEvidence',
    'Assert-HostV1GateEvidenceEqual',
    'Assert-HostV1GateArtifacts'
)
