Set-StrictMode -Version Latest

# G10 只允许在调用方明确拥有的结果根内创建或清理隔离副本。这里同时拒绝空路径、
# 通配符、父目录本身和同级目录，避免“先算路径、后删目录”时扩大用户数据范围。
function Assert-WorkbenchCommandG10ChildPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$Candidate,
        [Parameter(Mandatory)] [string]$Parent,
        [string]$Purpose = 'G10 文件操作'
    )

    if ([string]::IsNullOrWhiteSpace($Candidate) -or
        $Candidate.IndexOfAny([char[]]'*?[]') -ge 0) {
        throw "$Purpose 拒绝空路径或通配符路径：$Candidate。"
    }

    $candidatePath = [IO.Path]::GetFullPath($Candidate)
    $parentPath = [IO.Path]::GetFullPath($Parent).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $candidatePath.StartsWith(
            $parentPath,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose 拒绝操作允许根之外的路径：$candidatePath；允许根：$parentPath。"
    }
}

# 隔离构建会生成只读 NuGet 文件，清理必须先验证所有权再有限重试。该函数不接受通配符，
# 也不会吞掉最终占用错误，因此杀毒软件或残留进程不能被伪装成清理成功。
function Remove-WorkbenchCommandG10OwnedTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$AllowedParent,
        [string]$Purpose = 'G10 临时目录清理'
    )

    Assert-WorkbenchCommandG10ChildPath -Candidate $Path -Parent $AllowedParent -Purpose $Purpose
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
            if ($attempt -lt 20) { Start-Sleep -Milliseconds 500 }
        }
    }
    throw $lastError
}

function Write-WorkbenchCommandG10Json {
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
        ($Value | ConvertTo-Json -Depth 50),
        [Text.UTF8Encoding]::new($false))
}

function Get-WorkbenchCommandG10GitFiles {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string]$RepositoryRoot)

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    if (-not (Test-Path -LiteralPath (Join-Path $root '.git'))) {
        throw "G10 工作树不是 Git 仓库：$root。"
    }

    $raw = & git -C $root ls-files --cached --others --exclude-standard -z
    if ($LASTEXITCODE -ne 0) { throw "无法读取 G10 工作树文件清单：$root。" }
    $files = @(([string]$raw).Split([char]0, [StringSplitOptions]::RemoveEmptyEntries) |
        Where-Object { Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf } |
        Sort-Object -CaseSensitive)
    if ($files.Count -eq 0) { throw "G10 工作树没有可复制文件：$root。" }
    return $files
}

# 指纹把相对路径、字节长度和文件内容哈希一起纳入，既能发现漏拷文件，也能区分同内容的
# 不同路径。使用当前工作树字节而不是 Git tree，才能如实签署尚未提交的用户保留行尾状态。
function Get-WorkbenchCommandG10WorkspaceFingerprint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [string[]]$RelativePaths
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $paths = if ($null -eq $RelativePaths -or $RelativePaths.Count -eq 0) {
        Get-WorkbenchCommandG10GitFiles -RepositoryRoot $root
    }
    else {
        @($RelativePaths | Sort-Object -CaseSensitive)
    }

    $hash = [Security.Cryptography.IncrementalHash]::CreateHash(
        [Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        foreach ($relativePath in $paths) {
            if ([IO.Path]::IsPathRooted($relativePath) -or
                [regex]::Split($relativePath, '[\\/]') -contains '..') {
                throw "G10 文件清单包含越界路径：$relativePath。"
            }
            $path = Join-Path $root $relativePath
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "G10 文件清单缺少实体文件：$relativePath。"
            }
            $item = Get-Item -LiteralPath $path -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                -not [string]::IsNullOrWhiteSpace([string]$item.LinkType)) {
                throw "G10 不接受链接文件：$relativePath。"
            }

            $fileHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            $record = "$($relativePath.Replace('\\', '/'))`0$($item.Length)`0$fileHash`n"
            $hash.AppendData([Text.Encoding]::UTF8.GetBytes($record))
        }
        return [ordered]@{
            files = $paths.Count
            sha256 = [Convert]::ToHexString($hash.GetHashAndReset())
        }
    }
    finally {
        $hash.Dispose()
    }
}

# G6/G9 的兼容负例需要读取历史提交，因此隔离副本用 git clone --no-hardlinks 重建独立
# 元数据，再用当前工作树文件覆盖克隆结果。这样不会复制源 .git 的硬链接，同时仍能签署
# 未提交的当前内容。复制后逐项拒绝链接并重新计算指纹，确保实际结果与源工作树一致。
function Copy-WorkbenchCommandG10Workspace {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$SourceRoot,
        [Parameter(Mandatory)] [string]$DestinationRoot,
        [Parameter(Mandatory)] [string]$AllowedDestinationParent
    )

    $source = [IO.Path]::GetFullPath($SourceRoot)
    $destination = [IO.Path]::GetFullPath($DestinationRoot)
    Assert-WorkbenchCommandG10ChildPath -Candidate $destination `
        -Parent $AllowedDestinationParent -Purpose 'G10 工作树复制'
    if (Test-Path -LiteralPath $destination) {
        Remove-WorkbenchCommandG10OwnedTree -Path $destination `
            -AllowedParent $AllowedDestinationParent -Purpose 'G10 旧隔离副本清理'
    }
    $files = @(Get-WorkbenchCommandG10GitFiles -RepositoryRoot $source)
    $sourceFingerprint = Get-WorkbenchCommandG10WorkspaceFingerprint `
        -RepositoryRoot $source -RelativePaths $files
    & git clone --no-hardlinks --quiet $source $destination
    if ($LASTEXITCODE -ne 0) { throw "G10 无硬链接克隆失败：$source。" }

    $rawDeleted = & git -C $source ls-files --deleted -z
    if ($LASTEXITCODE -ne 0) { throw "G10 无法读取工作树删除清单：$source。" }
    if ($null -ne $rawDeleted) {
        foreach ($relativePath in ([string]$rawDeleted).Split(
                [char]0,
                [StringSplitOptions]::RemoveEmptyEntries)) {
            $deletedTarget = Join-Path $destination $relativePath
            if (Test-Path -LiteralPath $deletedTarget -PathType Leaf) {
                Remove-Item -LiteralPath $deletedTarget -Force
            }
        }
    }
    foreach ($relativePath in $files) {
        $sourcePath = Join-Path $source $relativePath
        $destinationPath = Join-Path $destination $relativePath
        $parent = Split-Path -Parent $destinationPath
        if (-not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
    }

    $destinationFingerprint = Get-WorkbenchCommandG10WorkspaceFingerprint `
        -RepositoryRoot $destination -RelativePaths $files
    $destinationFiles = @(Get-WorkbenchCommandG10GitFiles -RepositoryRoot $destination)
    if ($sourceFingerprint.files -ne $destinationFingerprint.files -or
        $sourceFingerprint.sha256 -cne $destinationFingerprint.sha256 -or
        ($files -join "`n") -cne ($destinationFiles -join "`n")) {
        throw 'G10 隔离副本文件数或内容指纹与源工作树不一致。'
    }
    return $sourceFingerprint
}

function Assert-WorkbenchCommandG10NonReleaseSummary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Summary,
        [string]$Description = 'G10 摘要'
    )

    # 正式入口在本进程内组装 [ordered] 字典，Core 单元测试和落盘重读则得到
    # PSCustomObject。两者都是合法摘要载体，但 IDictionary 的键不会出现在
    # PSObject.Properties 中；统一投影为属性表后再校验，避免把已经通过的内存摘要
    # 误判为“缺少 passed”，同时仍严格区分字段缺失、非布尔值和 true 发布标记。
    $properties = Get-WorkbenchCommandG10PropertyMap $Summary
    if (-not $properties.Contains('passed') -or
        $properties['passed'] -isnot [bool] -or
        -not [bool]$properties['passed']) {
        throw "$Description 未声明通过。"
    }
    foreach ($flag in @(
            'aiflow', 'windowsCi', 'windowsSmoke', 'releaseAcceptance', 'releaseGate',
            'publishable', 'published', 'uploaded', 'signed', 'tagCreated')) {
        if (-not $properties.Contains($flag) -or
            $properties[$flag] -isnot [bool] -or
            [bool]$properties[$flag]) {
            throw "$Description 的非发布标记 $flag 必须存在且为 false。"
        }
    }
}

function Get-WorkbenchCommandG10PropertyMap {
    param([Parameter(Mandatory)] $Value)
    $map = [ordered]@{}
    if ($Value -is [Collections.IDictionary]) {
        foreach ($key in $Value.Keys) { $map[[string]$key] = $Value[$key] }
        return $map
    }
    foreach ($property in $Value.PSObject.Properties) { $map[$property.Name] = $property.Value }
    return $map
}

function Find-WorkbenchCommandG10Difference {
    param($Left, $Right, [string]$Path = '$')

    if ($null -eq $Left -or $null -eq $Right) {
        if ($null -eq $Left -and $null -eq $Right) { return $null }
        return "$Path：左值='$Left'，右值='$Right'"
    }
    $leftSequence = $Left -is [Collections.IEnumerable] -and $Left -isnot [string] -and
        $Left -isnot [Collections.IDictionary] -and $Left -isnot [pscustomobject]
    $rightSequence = $Right -is [Collections.IEnumerable] -and $Right -isnot [string] -and
        $Right -isnot [Collections.IDictionary] -and $Right -isnot [pscustomobject]
    if ($leftSequence -or $rightSequence) {
        if (-not ($leftSequence -and $rightSequence)) { return "$Path：集合类型不一致。" }
        $leftItems = @($Left)
        $rightItems = @($Right)
        if ($leftItems.Count -ne $rightItems.Count) {
            return "$Path.length：左值=$($leftItems.Count)，右值=$($rightItems.Count)"
        }
        for ($index = 0; $index -lt $leftItems.Count; $index++) {
            $difference = Find-WorkbenchCommandG10Difference `
                $leftItems[$index] $rightItems[$index] "$Path[$index]"
            if ($difference) { return $difference }
        }
        return $null
    }

    $leftObject = $Left -is [Collections.IDictionary] -or $Left -is [pscustomobject]
    $rightObject = $Right -is [Collections.IDictionary] -or $Right -is [pscustomobject]
    if ($leftObject -or $rightObject) {
        if (-not ($leftObject -and $rightObject)) { return "$Path：对象类型不一致。" }
        $leftMap = Get-WorkbenchCommandG10PropertyMap $Left
        $rightMap = Get-WorkbenchCommandG10PropertyMap $Right
        $leftKeys = @($leftMap.Keys)
        $rightKeys = @($rightMap.Keys)
        if (($leftKeys -join "`n") -cne ($rightKeys -join "`n")) {
            return "$Path：字段集合或顺序不同。"
        }
        foreach ($key in $leftKeys) {
            $difference = Find-WorkbenchCommandG10Difference `
                $leftMap[$key] $rightMap[$key] "$Path.$key"
            if ($difference) { return $difference }
        }
        return $null
    }

    if ([string]$Left -cne [string]$Right) {
        return "$Path：左值='$Left'，右值='$Right'"
    }
    return $null
}

# 每轮 summary 由正式入口显式投影稳定事实；Core 仅剔除轮次、时间和绝对证据路径，
# 不猜测领域字段。测试数、覆盖率、版本、API 和制品哈希因此都会参加逐路径比较。
function ConvertTo-WorkbenchCommandG10CanonicalEvidence {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $Summary)

    return [ordered]@{
        schemaVersion = $Summary.schemaVersion
        stage = $Summary.stage
        configuration = $Summary.configuration
        source = $Summary.source
        api = $Summary.api
        versions = $Summary.versions
        schemas = $Summary.schemas
        host = $Summary.host
        sdkAndTemplate = $Summary.sdkAndTemplate
        workflowStudio = $Summary.workflowStudio
        classicGame = $Summary.classicGame
        externalHost = $Summary.externalHost
        combinedTests = $Summary.combinedTests
        documentation = $Summary.documentation
        passed = $Summary.passed
        aiflow = $Summary.aiflow
        windowsCi = $Summary.windowsCi
        windowsSmoke = $Summary.windowsSmoke
        releaseAcceptance = $Summary.releaseAcceptance
        releaseGate = $Summary.releaseGate
        publishable = $Summary.publishable
        published = $Summary.published
        uploaded = $Summary.uploaded
        signed = $Summary.signed
        tagCreated = $Summary.tagCreated
    }
}

function Assert-WorkbenchCommandG10EvidenceEqual {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $First,
        [Parameter(Mandatory)] $Second
    )

    $difference = Find-WorkbenchCommandG10Difference `
        (ConvertTo-WorkbenchCommandG10CanonicalEvidence $First) `
        (ConvertTo-WorkbenchCommandG10CanonicalEvidence $Second)
    if ($difference) { throw "G10 两轮稳定证据不一致：$difference" }
}

# 只有两轮均满足非发布边界且稳定事实完全一致时才写最终 summary。这个提交点让失败
# 场景天然没有成功摘要，避免上一次绿色文件被误认为本轮已经封板。
function Complete-WorkbenchCommandG10Sealing {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $First,
        [Parameter(Mandatory)] $Second,
        [Parameter(Mandatory)] [string]$OutputPath
    )

    Assert-WorkbenchCommandG10NonReleaseSummary -Summary $First -Description 'G10 第一轮摘要'
    Assert-WorkbenchCommandG10NonReleaseSummary -Summary $Second -Description 'G10 第二轮摘要'
    Assert-WorkbenchCommandG10EvidenceEqual -First $First -Second $Second
    $summary = [ordered]@{
        schemaVersion = 1
        stage = 'WorkbenchCommandG10'
        configuration = [string]$First.configuration
        repeatabilityVerified = $true
        evidence = ConvertTo-WorkbenchCommandG10CanonicalEvidence $First
        rounds = @(
            [ordered]@{ round = 1; evidencePath = [string]$First.evidencePath },
            [ordered]@{ round = 2; evidencePath = [string]$Second.evidencePath })
        sdk33PreviouslyPublished = $true
        templates13PreviouslyPublished = $true
        passed = $true
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        published = $false
        uploaded = $false
        signed = $false
        tagCreated = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    Write-WorkbenchCommandG10Json -Path $OutputPath -Value $summary
    return $summary
}

# G10 的最终本地验收按用户确认采用“一轮完整门禁通过即封板”。这里仍然把最终写入
# 集中在 Core 的单一提交点：只有该轮摘要字段齐全、所有发布类标记均为 false 时才会
# 创建成功摘要。旧的双轮比较函数继续保留给差异定位单元测试和将来显式要求的复核，
# 但正式 G10 入口不会借用第二轮结果，也不会把单轮结论表述为可重复性或发布资格。
function Complete-WorkbenchCommandG10SingleRoundSealing {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Evidence,
        [Parameter(Mandatory)] [string]$OutputPath
    )

    Assert-WorkbenchCommandG10NonReleaseSummary -Summary $Evidence `
        -Description 'G10 单轮完整门禁摘要'
    $summary = [ordered]@{
        schemaVersion = 1
        stage = 'WorkbenchCommandG10'
        configuration = [string]$Evidence.configuration
        singleRoundVerified = $true
        repeatabilityVerified = $false
        evidence = ConvertTo-WorkbenchCommandG10CanonicalEvidence $Evidence
        rounds = @(
            [ordered]@{ round = 1; evidencePath = [string]$Evidence.evidencePath })
        sdk33PreviouslyPublished = $true
        templates13PreviouslyPublished = $true
        passed = $true
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        published = $false
        uploaded = $false
        signed = $false
        tagCreated = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    Write-WorkbenchCommandG10Json -Path $OutputPath -Value $summary
    return $summary
}

Export-ModuleMember -Function @(
    'Assert-WorkbenchCommandG10ChildPath',
    'Remove-WorkbenchCommandG10OwnedTree',
    'Write-WorkbenchCommandG10Json',
    'Get-WorkbenchCommandG10GitFiles',
    'Get-WorkbenchCommandG10WorkspaceFingerprint',
    'Copy-WorkbenchCommandG10Workspace',
    'Assert-WorkbenchCommandG10NonReleaseSummary',
    'ConvertTo-WorkbenchCommandG10CanonicalEvidence',
    'Assert-WorkbenchCommandG10EvidenceEqual',
    'Complete-WorkbenchCommandG10Sealing',
    'Complete-WorkbenchCommandG10SingleRoundSealing')
