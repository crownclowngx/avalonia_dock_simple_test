[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
Import-Module (Join-Path $PSScriptRoot 'WorkbenchCommandG10Gate.Core.psm1') -Force

function Assert-True {
    param([Parameter(Mandatory)] [bool]$Condition, [Parameter(Mandatory)] [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-ThrowsLike {
    param(
        [Parameter(Mandatory)] [scriptblock]$Action,
        [Parameter(Mandatory)] [string]$ExpectedFragment
    )
    try { & $Action }
    catch {
        Assert-True ($_.Exception.Message.Contains($ExpectedFragment, [StringComparison]::Ordinal)) `
            "异常未包含 '$ExpectedFragment'：$($_.Exception.Message)"
        return
    }
    throw "操作本应失败并包含 '$ExpectedFragment'，但实际成功。"
}

function Copy-DeepObject {
    param([Parameter(Mandatory)] $Value)
    return $Value | ConvertTo-Json -Depth 50 | ConvertFrom-Json
}

function New-FixtureSummary {
    $value = [ordered]@{
        schemaVersion = 1
        stage = 'WorkbenchCommandG10'
        round = 1
        configuration = 'Release'
        evidencePath = 'C:\round-1\summary.json'
        generatedAtUtc = '2026-08-28T00:00:00Z'
        source = [ordered]@{
            host = [ordered]@{ revision = 'host'; files = 10; sha256 = ('A' * 64) }
            workflowStudio = [ordered]@{ revision = 'studio'; files = 5; sha256 = ('B' * 64) }
            classicGame = [ordered]@{ revision = 'game'; files = 8; sha256 = ('C' * 64) }
        }
        api = [ordered]@{
            coreShipped = [ordered]@{ entries = 127; sha256 = ('D' * 64) }
            coreUnshipped = [ordered]@{ entries = 91; sha256 = ('E' * 64) }
            uiShipped = [ordered]@{ entries = 45; sha256 = ('F' * 64) }
            uiUnshipped = [ordered]@{ entries = 66; sha256 = ('0' * 64) }
            workflowUnchanged = $true
        }
        versions = [ordered]@{
            product = '3.0.0'; sdk = '3.3.0'; templates = '1.3.0'
            workflowStudio = '1.2.0'; classicGame = '1.1.0'
        }
        schemas = [ordered]@{
            manifest = 2; documentEnvelope = 2; layout = 2
            layoutFileName = 'layout-v2.json'; dataRoot = 'v2'
        }
        host = [ordered]@{ tests = 584; lineCoverage = 87.32; branchCoverage = 72.58 }
        sdkAndTemplate = [ordered]@{ generatedSolutions = 2; deterministicRuns = 2 }
        workflowStudio = [ordered]@{ tests = 54; archiveSha256 = ('1' * 64) }
        classicGame = [ordered]@{ tests = 526; archiveSha256 = ('2' * 64) }
        externalHost = [ordered]@{ g7Plugin = 2; g7Ui = 1; g8Plugin = 1; g8Ui = 1 }
        combinedTests = [ordered]@{ plugin = 1; ui = 1; commands = 25; keyBindings = 5 }
        documentation = [ordered]@{ passed = $true; repositories = 3 }
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
    }
    return Copy-DeepObject $value
}

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $temporaryParent (
    'WorkbenchCommandG10GateCoreTests-' + [Guid]::NewGuid().ToString('N'))
Assert-WorkbenchCommandG10ChildPath -Candidate $testRoot -Parent $temporaryParent `
    -Purpose 'G10 Core 测试目录'
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $ownedRoot = Join-Path $testRoot 'owned'
    $ownedChild = Join-Path $ownedRoot 'child'
    New-Item -ItemType Directory -Path $ownedChild -Force | Out-Null
    Assert-WorkbenchCommandG10ChildPath -Candidate $ownedChild -Parent $ownedRoot
    Assert-ThrowsLike {
        Assert-WorkbenchCommandG10ChildPath -Candidate $ownedRoot -Parent $ownedRoot
    } '允许根之外'
    Assert-ThrowsLike {
        Assert-WorkbenchCommandG10ChildPath -Candidate (Join-Path $testRoot 'sibling') `
            -Parent $ownedRoot
    } '允许根之外'
    Assert-ThrowsLike {
        Assert-WorkbenchCommandG10ChildPath -Candidate (Join-Path $ownedRoot '*') `
            -Parent $ownedRoot
    } '通配符路径'

    $source = Join-Path $testRoot 'source'
    New-Item -ItemType Directory -Path $source | Out-Null
    & git -C $source init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'G10 Core 测试无法初始化 Git 仓库。' }
    [IO.File]::WriteAllText(
        (Join-Path $source 'tracked.txt'), 'tracked', [Text.UTF8Encoding]::new($false))
    New-Item -ItemType Directory -Path (Join-Path $source 'nested') | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $source 'nested\untracked.txt'), 'untracked', [Text.UTF8Encoding]::new($false))
    & git -C $source add tracked.txt
    if ($LASTEXITCODE -ne 0) { throw 'G10 Core 测试无法登记跟踪文件。' }

    $copyParent = Join-Path $testRoot 'copies'
    New-Item -ItemType Directory -Path $copyParent | Out-Null
    $destination = Join-Path $copyParent 'round-1'
    $fingerprint = Copy-WorkbenchCommandG10Workspace -SourceRoot $source `
        -DestinationRoot $destination -AllowedDestinationParent $copyParent
    Assert-True ($fingerprint.files -eq 2) '工作树复制没有同时包含跟踪和未跟踪文件。'
    Assert-True ((Test-Path -LiteralPath (Join-Path $destination 'nested\untracked.txt'))) `
        '工作树复制遗漏未跟踪文件。'

    Remove-Item -LiteralPath (Join-Path $destination 'tracked.txt')
    Assert-ThrowsLike {
        Get-WorkbenchCommandG10WorkspaceFingerprint -RepositoryRoot $destination `
            -RelativePaths @('tracked.txt', 'nested/untracked.txt')
    } '缺少实体文件'
    Copy-Item -LiteralPath (Join-Path $source 'tracked.txt') `
        -Destination (Join-Path $destination 'tracked.txt')
    [IO.File]::AppendAllText(
        (Join-Path $destination 'tracked.txt'), 'drift', [Text.UTF8Encoding]::new($false))
    $drifted = Get-WorkbenchCommandG10WorkspaceFingerprint -RepositoryRoot $destination `
        -RelativePaths @('tracked.txt', 'nested/untracked.txt')
    Assert-True ($drifted.sha256 -cne $fingerprint.sha256) '内容漂移没有改变工作树指纹。'

    $hardLink = Join-Path $source 'hard-link.txt'
    New-Item -ItemType HardLink -Path $hardLink -Target (Join-Path $source 'tracked.txt') | Out-Null
    & git -C $source add hard-link.txt
    Assert-ThrowsLike {
        Get-WorkbenchCommandG10WorkspaceFingerprint -RepositoryRoot $source
    } '链接文件'

    $first = New-FixtureSummary
    Assert-WorkbenchCommandG10NonReleaseSummary -Summary $first
    # 正式 G10 聚合器直接传入 [ordered] 字典，而 JSON 重读会得到 PSCustomObject。
    # 两种载体必须遵守同一严格契约，避免两轮全部成功后才出现类型适配假阴性。
    $inMemorySummary = [ordered]@{}
    foreach ($property in $first.PSObject.Properties) {
        $inMemorySummary[$property.Name] = $property.Value
    }
    Assert-WorkbenchCommandG10NonReleaseSummary -Summary $inMemorySummary
    foreach ($flag in @(
            'aiflow', 'windowsCi', 'windowsSmoke', 'releaseAcceptance', 'releaseGate',
            'publishable', 'published', 'uploaded', 'signed', 'tagCreated')) {
        $bad = Copy-DeepObject $first
        $bad.$flag = $true
        Assert-ThrowsLike {
            Assert-WorkbenchCommandG10NonReleaseSummary -Summary $bad
        } $flag
    }

    $same = Copy-DeepObject $first
    $same.round = 2
    $same.generatedAtUtc = '2026-08-29T00:00:00Z'
    $same.evidencePath = 'D:\round-2\summary.json'
    Assert-WorkbenchCommandG10EvidenceEqual -First $first -Second $same

    $changed = Copy-DeepObject $same
    $changed.host.tests++
    Assert-ThrowsLike {
        Assert-WorkbenchCommandG10EvidenceEqual -First $first -Second $changed
    } '$.host.tests'

    $failedOutput = Join-Path $testRoot 'failed-summary.json'
    Assert-ThrowsLike {
        Complete-WorkbenchCommandG10Sealing -First $first -Second $changed `
            -OutputPath $failedOutput
    } '$.host.tests'
    Assert-True (-not (Test-Path -LiteralPath $failedOutput)) `
        '两轮证据不一致时仍写出了成功摘要。'

    $successOutput = Join-Path $testRoot 'success-summary.json'
    $completed = Complete-WorkbenchCommandG10Sealing -First $first -Second $same `
        -OutputPath $successOutput
    Assert-True ($completed.repeatabilityVerified -and (Test-Path -LiteralPath $successOutput)) `
        '两轮稳定证据一致时没有写出成功摘要。'

    $singleOutput = Join-Path $testRoot 'single-round-summary.json'
    $singleCompleted = Complete-WorkbenchCommandG10SingleRoundSealing -Evidence $first `
        -OutputPath $singleOutput
    Assert-True (
        $singleCompleted.singleRoundVerified -and
        -not $singleCompleted.repeatabilityVerified -and
        $singleCompleted.rounds.Count -eq 1 -and
        (Test-Path -LiteralPath $singleOutput)) `
        '单轮完整门禁通过时没有按非发布口径写出成功摘要。'
    $singleFailedOutput = Join-Path $testRoot 'single-round-failed-summary.json'
    $badSingle = Copy-DeepObject $first
    $badSingle.publishable = $true
    Assert-ThrowsLike {
        Complete-WorkbenchCommandG10SingleRoundSealing -Evidence $badSingle `
            -OutputPath $singleFailedOutput
    } 'publishable'
    Assert-True (-not (Test-Path -LiteralPath $singleFailedOutput)) `
        '单轮摘要违反非发布边界时仍写出了成功摘要。'

    # $Host 是 PowerShell 的只读自动变量，且变量名大小写不敏感。聚合入口一旦误用
    # `$host = ...`，所有业务阶段都可能通过后才在汇总点失败；静态回归把该错误提前到
    # 秒级 Core 门禁，避免再次浪费完整跨仓轮次。
    $entryText = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot `
        'Test-WorkbenchCommandG10.ps1')
    Assert-True ($entryText -notmatch '(?im)^\s*\$host\s*=') `
        'G10 聚合入口不得赋值 PowerShell 只读自动变量 $Host。'

    Write-Host '[Workbench Command G10] Core 单元测试通过：路径、复制、链接、指纹、非发布标记、差异比较及单轮提交点均符合预期。'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-WorkbenchCommandG10OwnedTree -Path $testRoot -AllowedParent $temporaryParent `
            -Purpose 'G10 Core 测试清理'
    }
}
