[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$modulePath = Join-Path $PSScriptRoot 'HostV1ReleaseGate.Core.psm1'
Import-Module $modulePath -Force

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )

    if (-not $Condition) { throw $Message }
}

function Assert-ThrowsLike {
    param(
        [Parameter(Mandatory)] [scriptblock]$Action,
        [Parameter(Mandatory)] [string]$ExpectedFragment
    )

    try {
        & $Action
    }
    catch {
        Assert-True (
            $_.Exception.Message.Contains($ExpectedFragment, [StringComparison]::Ordinal)) (
            "异常没有包含 '$ExpectedFragment'：$($_.Exception.Message)")
        return
    }
    throw "操作本应失败并包含 '$ExpectedFragment'，但实际成功。"
}

function New-TestSummary {
    # 通过 JSON 往返构造与真实汇总相同的 PSCustomObject，避免测试只覆盖 Hashtable，
    # 却遗漏 PowerShell 读取 summary.json 后实际得到的对象形态。
    $value = [ordered]@{
        schemaVersion = 1
        sourceRevision = '0123456789abcdef'
        sourceTree = 'tree-001'
        generatedAtUtc = '2026-08-20T01:00:00Z'
        durationMilliseconds = 100
        evidenceRoot = 'D:\first'
        platform = [ordered]@{
            operatingSystem = 'Windows'
            architecture = 'X64'
            configuration = 'Release'
        }
        sdkVersion = '10.0.302'
        stages = @(
            [ordered]@{ name = 'restore'; status = 'passed'; durationMilliseconds = 10 },
            [ordered]@{ name = 'build'; status = 'passed'; durationMilliseconds = 20 }
        )
        host = [ordered]@{
            suites = [ordered]@{ Unit = 10; UI = 3; Plugin = 7 }
            passed = 20
            lineCoverage = 80.5
            branchCoverage = 65.2
        }
        sdkPackage = [ordered]@{ passed = $true; version = '1.0.0' }
        sdkApi = [ordered]@{
            passed = $true
            baseline = 'v1'
            shipped = 243
            unshipped = 0
        }
        managedPlugins = [ordered]@{
            schemaVersion = 1
            gates = [ordered]@{ deterministicBuildsPerPlugin = 2; finalZipHostLoad = $true }
            plugins = @(
                [ordered]@{
                    pluginId = 'plugin.one'
                    files = 4
                    archive = [ordered]@{
                        file = 'Plugin.One-1.0.0-win-x64.zip'
                        length = 1234
                        sha256 = 'AABBCC'
                    }
                    manifest = [ordered]@{
                        file = 'Plugin.One-1.0.0-win-x64.manifest.json'
                        length = 567
                        sha256 = 'DDEEFF'
                    }
                }
            )
        }
        windowsSmoke = [ordered]@{ passed = $true; exitCode = 0; layoutSaved = $true }
    }
    return $value | ConvertTo-Json -Depth 16 | ConvertFrom-Json
}

function Copy-TestSummary {
    param([Parameter(Mandatory)] $Summary)
    return $Summary | ConvertTo-Json -Depth 16 | ConvertFrom-Json
}

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $temporaryParent ('HostV1ReleaseGateCoreTests-' + [Guid]::NewGuid().ToString('N'))
Assert-HostV1GateChildPath -Candidate $testRoot -Parent $temporaryParent -Purpose 'G14 核心测试目录'
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $first = New-TestSummary
    $same = Copy-TestSummary $first
    $same.generatedAtUtc = '2026-08-20T02:00:00Z'
    $same.durationMilliseconds = 9999
    $same.evidenceRoot = 'E:\second'
    $same.stages[0].durationMilliseconds = 888
    Assert-HostV1GateEvidenceEqual -First $first -Second $same

    $semanticMutations = @(
        @{ path = '$.host.suites.Unit'; fragment = '$.host.suites.Unit'; apply = { param($x) $x.host.suites.Unit++ } },
        @{ path = '$.host.suites.UI'; fragment = '$.host.suites.UI'; apply = { param($x) $x.host.suites.UI++ } },
        @{ path = '$.host.suites.Plugin'; fragment = '$.host.suites.Plugin'; apply = { param($x) $x.host.suites.Plugin++ } },
        @{ path = '$.host.lineCoverage'; fragment = '$.host.lineCoverage'; apply = { param($x) $x.host.lineCoverage = 79.9 } },
        @{ path = '$.host.branchCoverage'; fragment = '$.host.branchCoverage'; apply = { param($x) $x.host.branchCoverage = 64.9 } },
        @{ path = '$.sdkApi.baseline'; fragment = '$.sdkApi.baseline'; apply = { param($x) $x.sdkApi.baseline = 'v2' } },
        @{ path = '$.sdkApi.shipped'; fragment = '$.sdkApi.shipped'; apply = { param($x) $x.sdkApi.shipped-- } },
        @{ path = '$.stages[1].status'; fragment = '$.stages[1].status'; apply = { param($x) $x.stages[1].status = 'failed' } },
        @{ path = '$.windowsSmoke.passed'; fragment = '$.windowsSmoke.passed'; apply = { param($x) $x.windowsSmoke.passed = $false } },
        @{ path = '$.managedPlugins.plugins[0].archive.sha256'; fragment = '$.managedPlugins.plugins[0].archive.sha256'; apply = { param($x) $x.managedPlugins.plugins[0].archive.sha256 = 'CHANGED' } },
        @{ path = '$.managedPlugins.plugins[0].manifest.sha256'; fragment = '$.managedPlugins.plugins[0].manifest.sha256'; apply = { param($x) $x.managedPlugins.plugins[0].manifest.sha256 = 'CHANGED' } }
    )
    foreach ($mutation in $semanticMutations) {
        $changed = Copy-TestSummary $first
        & $mutation.apply $changed
        Assert-ThrowsLike {
            Assert-HostV1GateEvidenceEqual -First $first -Second $changed
        } $mutation.fragment
    }

    $stageStatePath = Join-Path $testRoot 'stage-state.json'
    $executed = [Collections.Generic.List[string]]::new()
    $stages = @(
        @{ Name = 'first'; Action = { $executed.Add('first') } },
        @{ Name = 'failed'; Action = { $executed.Add('failed'); throw 'fixture failure' } },
        @{ Name = 'must-not-run'; Action = { $executed.Add('must-not-run') } }
    )
    Assert-ThrowsLike {
        Invoke-HostV1GateStageSequence -Stages $stages -StatePath $stageStatePath
    } "阶段 'failed' 失败"
    Assert-True ($executed.Count -eq 2) '阶段失败后仍执行了后续阶段。'
    Assert-True ($executed[0] -ceq 'first' -and $executed[1] -ceq 'failed') '阶段执行顺序不正确。'
    $stageState = @(Get-Content -Raw $stageStatePath | ConvertFrom-Json)
    Assert-True ($stageState.Count -eq 2 -and $stageState[1].status -ceq 'failed') '失败阶段没有写入状态证据。'

    $passRoot = Join-Path $testRoot 'complete-pass'
    $required = @(
        'pass.log',
        'MyAvaloniaManagement\summary.json',
        'MyAvaloniaManagement\Unit\Unit.trx',
        'MyAvaloniaManagement\UI\UI.trx',
        'MyAvaloniaManagement\Plugin\Plugin.trx',
        'MyAvaloniaManagement\coverage\Cobertura.xml',
        'ManagedPluginPackages\summary.json',
        'ManagedPluginPackages\Plugin.One-1.0.0-win-x64.zip',
        'ManagedPluginPackages\Plugin.One-1.0.0-win-x64.manifest.json',
        'WindowsSmoke\summary.json'
    )
    foreach ($relativePath in $required) {
        $path = Join-Path $passRoot $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, 'fixture')
    }
    # 完整性检查不仅要求文件存在，还要复核 ZIP 与外置清单的长度和 SHA-256。夹具在落盘后
    # 动态回填摘要，避免把测试依赖于某个手写哈希常量。
    $archiveFixture = Join-Path $passRoot 'ManagedPluginPackages\Plugin.One-1.0.0-win-x64.zip'
    $manifestFixture = Join-Path $passRoot 'ManagedPluginPackages\Plugin.One-1.0.0-win-x64.manifest.json'
    $first.managedPlugins.plugins[0].archive.length = (Get-Item -LiteralPath $archiveFixture).Length
    $first.managedPlugins.plugins[0].archive.sha256 = (Get-FileHash -LiteralPath $archiveFixture -Algorithm SHA256).Hash
    $first.managedPlugins.plugins[0].manifest.length = (Get-Item -LiteralPath $manifestFixture).Length
    $first.managedPlugins.plugins[0].manifest.sha256 = (Get-FileHash -LiteralPath $manifestFixture -Algorithm SHA256).Hash
    Assert-HostV1GateArtifacts -PassRoot $passRoot -Summary $first

    foreach ($missing in @(
        'pass.log',
        'MyAvaloniaManagement\summary.json',
        'MyAvaloniaManagement\Unit\Unit.trx',
        'MyAvaloniaManagement\UI\UI.trx',
        'MyAvaloniaManagement\Plugin\Plugin.trx',
        'MyAvaloniaManagement\coverage\Cobertura.xml',
        'ManagedPluginPackages\summary.json',
        'ManagedPluginPackages\Plugin.One-1.0.0-win-x64.zip',
        'ManagedPluginPackages\Plugin.One-1.0.0-win-x64.manifest.json',
        'WindowsSmoke\summary.json')) {
        $path = Join-Path $passRoot $missing
        Remove-Item -LiteralPath $path -Force
        Assert-ThrowsLike {
            Assert-HostV1GateArtifacts -PassRoot $passRoot -Summary $first
        } ([IO.Path]::GetFileName($missing))
        [IO.File]::WriteAllText($path, 'fixture')
    }

    $allowedRoot = Join-Path $testRoot 'allowed'
    $child = Join-Path $allowedRoot 'child'
    Assert-HostV1GateChildPath -Candidate $child -Parent $allowedRoot -Purpose '测试安全路径'
    Assert-ThrowsLike {
        Assert-HostV1GateChildPath -Candidate (Join-Path $testRoot 'sibling') -Parent $allowedRoot -Purpose '测试安全路径'
    } '允许根之外'

    $readOnlyTree = Join-Path $allowedRoot 'readonly-tree'
    New-Item -ItemType Directory -Path $readOnlyTree -Force | Out-Null
    $readOnlyFile = Join-Path $readOnlyTree 'readonly.dll'
    [IO.File]::WriteAllText($readOnlyFile, 'fixture')
    (Get-Item -LiteralPath $readOnlyFile).Attributes =
        (Get-Item -LiteralPath $readOnlyFile).Attributes -bor [IO.FileAttributes]::ReadOnly
    Remove-HostV1GateOwnedTree -Path $readOnlyTree -AllowedParent $allowedRoot -Purpose '测试只读目录清理'
    Assert-True (-not (Test-Path -LiteralPath $readOnlyTree)) '带只读文件的自有目录没有被清理。'
    Assert-ThrowsLike {
        Remove-HostV1GateOwnedTree `
            -Path (Join-Path $testRoot 'sibling') `
            -AllowedParent $allowedRoot `
            -Purpose '测试越界清理'
    } '允许根之外'

    Write-Host '[G14] 核心单元测试通过：规范化、语义漂移、失败即停止、证据完整性、路径安全与只读文件清理均符合预期。'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Assert-HostV1GateChildPath -Candidate $testRoot -Parent $temporaryParent -Purpose 'G14 核心测试清理'
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
