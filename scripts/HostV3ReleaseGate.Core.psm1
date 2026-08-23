Set-StrictMode -Version Latest

# V3 门禁只允许调用方在明确拥有的父目录内创建、复制或删除文件。比较时先规范化
# 绝对路径并补齐目录分隔符，避免把 C:\Temp2 误判为 C:\Temp 的子目录。
function Assert-HostV3GateChildPath {
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

# Git 克隆和 NuGet 缓存可能含只读文件。路径所有权检查和删除放在同一函数内，
# 避免调用方先验证一个路径、随后却删除另一个路径。这里只接受 LiteralPath，不支持通配符。
function Remove-HostV3GateOwnedTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$AllowedParent,
        [string]$Purpose = '临时目录清理'
    )

    Assert-HostV3GateChildPath -Candidate $Path -Parent $AllowedParent -Purpose $Purpose
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

# 所有机器摘要统一使用无 BOM UTF-8，避免 PowerShell 版本或区域设置制造无意义差异。
function Write-HostV3GateJson {
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
        ($Value | ConvertTo-Json -Depth 40),
        [Text.UTF8Encoding]::new($false))
}

# 编排器只拥有阶段顺序、状态落盘和失败即停止。还原、测试、打包、Harness 与 Smoke
# 的领域断言继续由叶子脚本拥有，防止总入口复制规则形成第二套实现。
function Invoke-HostV3GateStageSequence {
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
        Write-Host "`n[G14 V3] 开始阶段：$name"
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
            Write-Host "[G14 V3] 阶段通过：$name"
            Write-HostV3GateJson -Path $StatePath -Value @($results)
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
            Write-HostV3GateJson -Path $StatePath -Value @($results)
            throw "G14 V3 阶段 '$name' 失败：$($_.Exception.Message)"
        }
    }
    return @($results)
}

# 两轮比较只剔除时间、耗时、绝对路径和 transcript 等环境噪声。其余发布事实必须
# 逐字段相等，包括测试数、覆盖率、API、文档、四插件专项 Harness、包与 Smoke。
function ConvertTo-HostV3GateCanonicalEvidence {
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
        published = $Summary.published
        uploaded = $Summary.uploaded
        tagCreated = $Summary.tagCreated
        stages = @($Summary.stages | ForEach-Object {
            [ordered]@{ name = $_.name; status = $_.status }
        })
        productionSurface = $Summary.productionSurface
        sdkApi = $Summary.sdkApi
        documentation = $Summary.documentation
        pluginAcceptances = $Summary.pluginAcceptances
        managedPlugins = $Summary.managedPlugins
        windowsSmoke = $Summary.windowsSmoke
    }
}

function Get-HostV3GatePropertyMap {
    param([Parameter(Mandatory)] $Value)

    $map = [ordered]@{}
    if ($Value -is [Collections.IDictionary]) {
        foreach ($key in $Value.Keys) { $map[[string]$key] = $Value[$key] }
        return $map
    }
    foreach ($property in $Value.PSObject.Properties) { $map[$property.Name] = $property.Value }
    return $map
}

# 返回首个差异的 JSON 路径，避免只输出两段压缩 JSON，让覆盖率或某个插件包的
# 漂移能够直接定位。属性集合和顺序也参加比较，防止摘要静默丢字段。
function Find-HostV3GateDifference {
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
            $difference = Find-HostV3GateDifference $leftItems[$index] $rightItems[$index] "$Path[$index]"
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
        $leftMap = Get-HostV3GatePropertyMap $Left
        $rightMap = Get-HostV3GatePropertyMap $Right
        $leftKeys = @($leftMap.Keys)
        $rightKeys = @($rightMap.Keys)
        if (($leftKeys -join "`n") -cne ($rightKeys -join "`n")) {
            return "$Path：字段集合或顺序不同；左='$($leftKeys -join ',')'，右='$($rightKeys -join ',')'"
        }
        foreach ($key in $leftKeys) {
            $difference = Find-HostV3GateDifference $leftMap[$key] $rightMap[$key] "$Path.$key"
            if ($difference) { return $difference }
        }
        return $null
    }

    if ([string]$Left -cne [string]$Right) {
        return "$Path：左值='$Left'，右值='$Right'"
    }
    return $null
}

function Assert-HostV3GateEvidenceEqual {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $First,
        [Parameter(Mandatory)] $Second
    )

    $difference = Find-HostV3GateDifference `
        (ConvertTo-HostV3GateCanonicalEvidence $First) `
        (ConvertTo-HostV3GateCanonicalEvidence $Second)
    if ($difference) { throw "G14 V3 两轮发布证据不一致：$difference" }
}

# summary.json 只是索引，不是充分证据。本函数反查 TRX、覆盖率、专项摘要、最终 ZIP、
# 外置 manifest 和 Smoke 摘要，并复算交付文件长度与 SHA-256，防止单独改写汇总冒充通过。
function Assert-HostV3GateArtifacts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$PassRoot,
        [Parameter(Mandatory)] $Summary
    )

    $requiredFiles = @(
        'pass.log',
        'stage-state.json',
        'HostV3ProductionSurface\summary.json',
        'MyAvaloniaManagement\summary.json',
        'MyAvaloniaManagement\Unit\Unit.trx',
        'MyAvaloniaManagement\UI\UI.trx',
        'MyAvaloniaManagement\Plugin\Plugin.trx',
        'MyAvaloniaManagement\coverage\Cobertura.xml',
        'AdditionalSuites\PluginSdk\PluginSdk.trx',
        'AdditionalSuites\MyPlugTest\MyPlugTest.trx',
        'AdditionalSuites\DaTang\DaTang.trx',
        'AdditionalSuites\MySmallTools\MySmallTools.trx',
        'AdditionalSuites\BiliDownloader\BiliDownloader.trx',
        'Documentation\summary.json',
        'ManagedPluginPackages\summary.json',
        'PluginAcceptances\MyPlugTestV3\summary.json',
        'PluginAcceptances\DaTangAccountingHelpPlugV3\summary.json',
        'PluginAcceptances\MySmallToolsV3\summary.json',
        'PluginAcceptances\MySmallToolsV3\real-media-harness.json',
        'PluginAcceptances\BiliDownloaderV3\summary.json',
        'WindowsSmoke\summary.json'
    )
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $PassRoot $relativePath) -PathType Leaf)) {
            throw "G14 V3 缺少必需发布证据：$relativePath"
        }
    }

    $apiProjects = @($Summary.sdkApi.projects)
    if ($apiProjects.Count -ne 2 -or
        [int]$apiProjects[0].shipped -ne 127 -or [int]$apiProjects[0].unshipped -ne 0 -or
        [int]$apiProjects[1].shipped -ne 45 -or [int]$apiProjects[1].unshipped -ne 0) {
        throw 'G14 V3 API 证据不是 Core 127/0、UI 45/0。'
    }

    if (-not $Summary.windowsSmoke.passed -or -not $Summary.windowsSmoke.layoutSaved -or
        [string]$Summary.windowsSmoke.layoutFileName -cne 'layout-v2.json' -or
        [int]$Summary.windowsSmoke.layoutSchemaVersion -ne 2 -or
        -not $Summary.windowsSmoke.legacyLayoutAbsent -or
        -not $Summary.windowsSmoke.isolatedDataDirectory) {
        throw 'G14 V3 Smoke 证据没有证明隔离数据根中的唯一 layout-v2.json/schema 2。'
    }

    # 总入口在写盘前使用 OrderedDictionary 保持字段顺序，核心单测则从 JSON
    # 读回 PSCustomObject。这里同时接受两种表示，避免把“容器实现细节”误当成
    # 发布事实；后续仍逐个验证四份摘要的业务字段，断言强度没有降低。
    $acceptances = if ($Summary.pluginAcceptances -is [System.Collections.IDictionary]) {
        @($Summary.pluginAcceptances.Values)
    }
    else {
        @($Summary.pluginAcceptances.PSObject.Properties | ForEach-Object { $_.Value })
    }
    if ($acceptances.Count -ne 4) { throw 'G14 V3 必须包含四个业务插件专项验收摘要。' }
    foreach ($acceptance in $acceptances) {
        if ([int]$acceptance.manifest.schemaVersion -ne 2 -or
            [string]$acceptance.manifest.pluginVersion -cne '3.0.0' -or
            [string]$acceptance.manifest.sdkMinInclusive -cne '3.0.0' -or
            [string]$acceptance.manifest.sdkMaxExclusive -cne '4.0.0' -or
            [int]$acceptance.deterministicBuilds -ne 2) {
            throw "G14 V3 插件 $($acceptance.manifest.pluginId) 的专项 manifest 或确定性证据不正确。"
        }
    }
    $smallTools = $Summary.pluginAcceptances.MySmallTools
    if ([int]$smallTools.harness.cycles -ne 20 -or -not $smallTools.harness.success -or
        -not $smallTools.harness.allFinalResourcesZero -or
        [int]$smallTools.harness.aliveClosedDocuments -ne 0 -or
        [int]$smallTools.harness.aliveClosedViews -ne 0 -or
        [int]$smallTools.harness.aliveDisposedEncryptedStreams -ne 0) {
        throw 'G14 V3 MySmallTools Harness 没有证明 20 轮资源归零。'
    }

    if (-not $Summary.managedPlugins.gates.finalZipHostLoad -or
        [int]$Summary.managedPlugins.gates.deterministicBuildsPerPlugin -ne 2 -or
        @($Summary.managedPlugins.plugins).Count -ne 4) {
        throw 'G14 V3 四插件包矩阵没有证明两次确定性构建和最终 ZIP 真实 Host 加载。'
    }
    foreach ($plugin in @($Summary.managedPlugins.plugins)) {
        $archiveName = [string]$plugin.archive.file
        $manifestName = [string]$plugin.manifest.file
        if ([string]$plugin.pluginVersion -cne '3.0.0' -or
            -not $archiveName.EndsWith('-3.0.0-win-x64.zip', [StringComparison]::Ordinal) -or
            [int]$plugin.manifest.schemaVersion -ne 2 -or
            [string]$plugin.manifest.sdkMinInclusive -cne '3.0.0' -or
            [string]$plugin.manifest.sdkMaxExclusive -cne '4.0.0') {
            throw "G14 V3 插件 $($plugin.pluginId) 的最终包版本、平台或 manifest 契约不正确。"
        }
        foreach ($entry in @(
                @{ Name = $archiveName; Expected = $plugin.archive },
                @{ Name = $manifestName; Expected = $plugin.manifest })) {
            $path = Join-Path (Join-Path $PassRoot 'ManagedPluginPackages') ([string]$entry.Name)
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "G14 V3 插件 $($plugin.pluginId) 缺少最终包证据：$($entry.Name)"
            }
            $file = Get-Item -LiteralPath $path
            $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            if ($file.Length -ne [long]$entry.Expected.length -or
                $actualHash -cne [string]$entry.Expected.sha256) {
                throw "G14 V3 插件 $($plugin.pluginId) 的 $($entry.Name) 实体摘要与汇总不一致。"
            }
        }
    }
}

Export-ModuleMember -Function @(
    'Assert-HostV3GateChildPath',
    'Remove-HostV3GateOwnedTree',
    'Write-HostV3GateJson',
    'Invoke-HostV3GateStageSequence',
    'ConvertTo-HostV3GateCanonicalEvidence',
    'Assert-HostV3GateEvidenceEqual',
    'Assert-HostV3GateArtifacts'
)
