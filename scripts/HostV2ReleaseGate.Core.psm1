Set-StrictMode -Version Latest

# V2 发布门禁只允许操作调用方明确拥有的子目录。先规范化绝对路径，再比较带目录
# 分隔符的父路径前缀，避免把 C:\Temp2 错当成 C:\Temp 的子目录。
function Assert-HostV2GateChildPath {
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

# Git 克隆与 NuGet 缓存可能含只读文件。清理与路径所有权检查必须放在同一函数中，
# 这样调用方不能先验证一个路径、随后却删除另一个路径。这里只接受 LiteralPath，不支持通配符。
function Remove-HostV2GateOwnedTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$AllowedParent,
        [string]$Purpose = '临时目录清理'
    )

    Assert-HostV2GateChildPath -Candidate $Path -Parent $AllowedParent -Purpose $Purpose
    if (-not (Test-Path -LiteralPath $Path)) { return }

    $lastError = $null
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        foreach ($item in @(Get-ChildItem -LiteralPath $Path -Recurse -Force) +
            @(Get-Item -LiteralPath $Path -Force)) {
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
                # MSBuild/Avalonia 子进程退出后，Windows 偶尔仍需极短时间释放 DLL。
                # 有限重试总等待不超过十秒，不会把永久占用伪装成成功。
                Start-Sleep -Milliseconds 500
            }
        }
    }
    throw $lastError
}

# 所有机器证据统一使用无 BOM UTF-8，避免 PowerShell 版本或区域设置制造无意义差异。
function Write-HostV2GateJson {
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

# 编排器只负责顺序、状态和失败即停止。还原、测试、打包与 Smoke 的领域断言继续由
# 现有叶子脚本拥有，避免总入口复制业务规则或形成第二套测试实现。
function Invoke-HostV2GateStageSequence {
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
        Write-Host "`n[G14 V2] 开始阶段：$name"
        try {
            & $stage.Action | Out-Host
            $stopwatch.Stop()
            $results.Add([ordered]@{
                name = $name
                status = 'passed'
                startedAtUtc = $started.ToString('O')
                durationMilliseconds = $stopwatch.ElapsedMilliseconds
                error = $null
            })
            Write-Host "[G14 V2] 阶段通过：$name"
            Write-HostV2GateJson -Path $StatePath -Value @($results)
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
            Write-HostV2GateJson -Path $StatePath -Value @($results)
            throw "G14 V2 阶段 '$name' 失败：$($_.Exception.Message)"
        }
    }
    return @($results)
}

# 规范化只剔除环境噪声：时间、耗时、绝对路径和 transcript 内容。其余发布事实必须逐字段
# 相等，尤其不能忽略文档事实、独立插件测试数、API 条目或 V2 布局 Smoke。
function ConvertTo-HostV2GateCanonicalEvidence {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $Summary)

    return [ordered]@{
        schemaVersion = $Summary.schemaVersion
        baseline = $Summary.baseline
        sourceRevision = $Summary.sourceRevision
        sourceTree = $Summary.sourceTree
        platform = [ordered]@{
            operatingSystem = $Summary.platform.operatingSystem
            architecture = $Summary.platform.architecture
            configuration = $Summary.platform.configuration
        }
        sdkVersion = $Summary.sdkVersion
        sdkPackageVersion = $Summary.sdkPackageVersion
        aiflow = $Summary.aiflow
        publishable = $Summary.publishable
        stages = @($Summary.stages | ForEach-Object {
            [ordered]@{ name = $_.name; status = $_.status }
        })
        productionSurface = $Summary.productionSurface
        sdkApi = $Summary.sdkApi
        documentation = $Summary.documentation
        managedPlugins = $Summary.managedPlugins
        windowsSmoke = $Summary.windowsSmoke
    }
}

function Get-HostV2GatePropertyMap {
    param([Parameter(Mandatory)] $Value)

    $map = [ordered]@{}
    if ($Value -is [Collections.IDictionary]) {
        foreach ($key in $Value.Keys) { $map[[string]$key] = $Value[$key] }
        return $map
    }
    foreach ($property in $Value.PSObject.Properties) { $map[$property.Name] = $property.Value }
    return $map
}

# 返回首个不同字段的 JSON 路径，比比较两段压缩 JSON 更利于定位到底是覆盖率、测试数、
# 文档计数还是某个插件包发生了漂移。
function Find-HostV2GateDifference {
    param($Left, $Right, [string]$Path = '$')

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
            $difference = Find-HostV2GateDifference $leftItems[$index] $rightItems[$index] "$Path[$index]"
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
        $leftMap = Get-HostV2GatePropertyMap $Left
        $rightMap = Get-HostV2GatePropertyMap $Right
        $leftKeys = @($leftMap.Keys)
        $rightKeys = @($rightMap.Keys)
        if (($leftKeys -join "`n") -cne ($rightKeys -join "`n")) {
            return "$Path：字段集合或顺序不同；左='$($leftKeys -join ',')'，右='$($rightKeys -join ',')'"
        }
        foreach ($key in $leftKeys) {
            $difference = Find-HostV2GateDifference $leftMap[$key] $rightMap[$key] "$Path.$key"
            if ($difference) { return $difference }
        }
        return $null
    }

    if ([string]$Left -cne [string]$Right) {
        return "$Path：左值='$Left'，右值='$Right'"
    }
    return $null
}

function Assert-HostV2GateEvidenceEqual {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $First,
        [Parameter(Mandatory)] $Second
    )

    $difference = Find-HostV2GateDifference `
        (ConvertTo-HostV2GateCanonicalEvidence $First) `
        (ConvertTo-HostV2GateCanonicalEvidence $Second)
    if ($difference) { throw "G14 V2 两轮发布证据不一致：$difference" }
}

# summary.json 不是充分证据。本函数反查实际 TRX、覆盖率、文档摘要、最终 ZIP/manifest 和
# Smoke 文件，同时复算所有交付文件的长度与 SHA-256，避免只信任一份可被单独改写的汇总。
function Assert-HostV2GateArtifacts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$PassRoot,
        [Parameter(Mandatory)] $Summary
    )

    $requiredFiles = @(
        'pass.log',
        'stage-state.json',
        'HostV2ProductionSurface\summary.json',
        'MyAvaloniaManagement\summary.json',
        'MyAvaloniaManagement\Unit\Unit.trx',
        'MyAvaloniaManagement\UI\UI.trx',
        'MyAvaloniaManagement\Plugin\Plugin.trx',
        'MyAvaloniaManagement\coverage\Cobertura.xml',
        'AdditionalSuites\PluginSdk\PluginSdk.trx',
        'AdditionalSuites\DaTang\DaTang.trx',
        'AdditionalSuites\MySmallTools\MySmallTools.trx',
        'AdditionalSuites\BiliDownloader\BiliDownloader.trx',
        'Documentation\summary.json',
        'ManagedPluginPackages\summary.json',
        'WindowsSmoke\summary.json'
    )
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $PassRoot $relativePath) -PathType Leaf)) {
            throw "G14 V2 缺少必需发布证据：$relativePath"
        }
    }

    if (-not $Summary.windowsSmoke.passed -or -not $Summary.windowsSmoke.layoutSaved -or
        [string]$Summary.windowsSmoke.layoutFileName -cne 'layout-v2.json' -or
        [int]$Summary.windowsSmoke.layoutSchemaVersion -ne 2 -or
        -not $Summary.windowsSmoke.legacyLayoutAbsent) {
        throw 'G14 V2 Smoke 证据没有证明唯一 layout-v2.json/schema 2。'
    }

    foreach ($plugin in @($Summary.managedPlugins.plugins)) {
        $archiveName = [string]$plugin.archive.file
        $manifestName = [string]$plugin.manifest.file
        if ([string]::IsNullOrWhiteSpace($archiveName) -or
            [string]::IsNullOrWhiteSpace($manifestName)) {
            throw "G14 V2 插件 $($plugin.pluginId) 缺少 archive 或 manifest 汇总。"
        }
        foreach ($entry in @(
                @{ Name = $archiveName; Expected = $plugin.archive },
                @{ Name = $manifestName; Expected = $plugin.manifest })) {
            $path = Join-Path (Join-Path $PassRoot 'ManagedPluginPackages') ([string]$entry.Name)
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "G14 V2 插件 $($plugin.pluginId) 缺少最终包证据：$($entry.Name)"
            }
            $file = Get-Item -LiteralPath $path
            $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            if ($file.Length -ne [long]$entry.Expected.length) {
                throw "G14 V2 插件 $($plugin.pluginId) 的 $($entry.Name) 长度与汇总不一致。"
            }
            if ($actualHash -cne [string]$entry.Expected.sha256) {
                throw "G14 V2 插件 $($plugin.pluginId) 的 $($entry.Name) SHA-256 与汇总不一致。"
            }
        }
    }
}

Export-ModuleMember -Function @(
    'Assert-HostV2GateChildPath',
    'Remove-HostV2GateOwnedTree',
    'Write-HostV2GateJson',
    'Invoke-HostV2GateStageSequence',
    'ConvertTo-HostV2GateCanonicalEvidence',
    'Assert-HostV2GateEvidenceEqual',
    'Assert-HostV2GateArtifacts'
)
