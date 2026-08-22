[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$modulePath = Join-Path $PSScriptRoot 'HostV2ReleaseGate.Core.psm1'
Import-Module $modulePath -Force

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
    # JSON 往返刻意模拟正式入口读取 summary.json 后得到的 PSCustomObject 形态，避免测试只覆盖
    # OrderedDictionary，而遗漏真实磁盘证据的类型行为。
    return $Value | ConvertTo-Json -Depth 32 | ConvertFrom-Json
}

function New-FixtureSummary {
    return [ordered]@{
        schemaVersion = 1
        baseline = 'v2'
        sourceRevision = '0123456789abcdef'
        sourceTree = 'fedcba9876543210'
        passed = $true
        generatedAtUtc = '2026-08-22T00:00:00.0000000Z'
        durationMilliseconds = 100
        evidenceRoot = 'C:\first'
        platform = [ordered]@{ operatingSystem = 'Windows'; architecture = 'X64'; configuration = 'Release' }
        sdkVersion = '10.0.302'
        sdkPackageVersion = '2.0.0'
        aiflow = $false
        publishable = $true
        stages = @(
            [ordered]@{ name = 'core'; status = 'passed'; startedAtUtc = 'first'; durationMilliseconds = 1; error = $null },
            [ordered]@{ name = 'tests'; status = 'passed'; startedAtUtc = 'second'; durationMilliseconds = 2; error = $null }
        )
        productionSurface = [ordered]@{
            host = [ordered]@{
                suites = [ordered]@{ Unit = 168; UI = 52; Plugin = 202 }
                passed = 422
                lineCoverage = 83.19
                branchCoverage = 68.81
            }
            additionalSuites = [ordered]@{ PluginSdk = 34; DaTang = 62; MySmallTools = 184; BiliDownloader = 718 }
            gates = [ordered]@{
                sourceScan = $true; warnAsErrorBuild = $true; sdkPackageAndCompileNegatives = $true
                deterministicPluginPackageMatrix = $true; diagnosticRedaction = $true; documentation = $true
            }
        }
        sdkApi = [ordered]@{
            baseline = 'v2'
            projects = @(
                [ordered]@{ project = 'Core'; shipped = 85; unshipped = 0 },
                [ordered]@{ project = 'UI'; shipped = 46; unshipped = 0 }
            )
        }
        documentation = [ordered]@{
            documents = 45; currentDocuments = 18; localLinks = 250; commandPaths = 80; projectPaths = 44
            productVersion = '2.0.0'; sdkVersion = '2.0.0'; apiBaseline = 'v2'
            shippedApiEntries = 131; unshippedApiEntries = 0
            apiProjects = @([ordered]@{ Project = 'Core'; Shipped = 85; Unshipped = 0 })
            plugins = @([ordered]@{ Project = 'Plugin.csproj'; Version = '2.0.0'; EntryPoint = 'A.B'; SdkRange = '[2.0.0, 3.0.0)' })
        }
        managedPlugins = [ordered]@{
            schemaVersion = 1
            configuration = 'Release'
            platform = 'win-x64'
            gates = [ordered]@{ deterministicBuildsPerPlugin = 2 }
            plugins = @([ordered]@{
                pluginId = 'myavalonia.plugin.fixture'
                archive = [ordered]@{ file = 'Fixture-2.0.0-win-x64.zip'; length = 3; sha256 = '' }
                manifest = [ordered]@{ file = 'Fixture-2.0.0-win-x64.manifest.json'; length = 3; sha256 = '' }
            })
        }
        windowsSmoke = [ordered]@{
            passed = $true; exitCode = 0; layoutSaved = $true; layoutFileName = 'layout-v2.json'
            layoutSchemaVersion = 2; legacyLayoutAbsent = $true; isolatedDataDirectory = $true
        }
    }
}

function Write-FixtureFile {
    param([Parameter(Mandatory)] [string]$Path, [string]$Content = 'abc')
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $temporaryParent ('HostV2ReleaseGateCoreTests-' + [Guid]::NewGuid().ToString('N'))
Assert-HostV2GateChildPath -Candidate $testRoot -Parent $temporaryParent -Purpose 'G14 V2 核心测试目录'
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $first = New-FixtureSummary
    $same = Copy-DeepObject $first
    $same.generatedAtUtc = '2026-08-23T10:00:00Z'
    $same.durationMilliseconds = 999
    $same.evidenceRoot = 'D:\second'
    $same.stages[0].startedAtUtc = 'different'
    $same.stages[0].durationMilliseconds = 888
    Assert-HostV2GateEvidenceEqual -First $first -Second $same

    # 每个变异都对应 G14 的一项发布事实，期望错误必须给出可定位的 JSON 路径。
    $mutations = @(
        @{ Path = '$.productionSurface.host.suites.Unit'; Apply = { param($x) $x.productionSurface.host.suites.Unit++ } },
        @{ Path = '$.productionSurface.host.lineCoverage'; Apply = { param($x) $x.productionSurface.host.lineCoverage = 80 } },
        @{ Path = '$.productionSurface.additionalSuites.BiliDownloader'; Apply = { param($x) $x.productionSurface.additionalSuites.BiliDownloader-- } },
        @{ Path = '$.sdkPackageVersion'; Apply = { param($x) $x.sdkPackageVersion = '2.0.1' } },
        @{ Path = '$.sdkApi.projects[0].shipped'; Apply = { param($x) $x.sdkApi.projects[0].shipped-- } },
        @{ Path = '$.documentation.localLinks'; Apply = { param($x) $x.documentation.localLinks-- } },
        @{ Path = '$.stages[1].status'; Apply = { param($x) $x.stages[1].status = 'failed' } },
        @{ Path = '$.windowsSmoke.layoutFileName'; Apply = { param($x) $x.windowsSmoke.layoutFileName = 'layout-v1.json' } },
        @{ Path = '$.managedPlugins.plugins[0].archive.sha256'; Apply = { param($x) $x.managedPlugins.plugins[0].archive.sha256 = 'changed' } }
    )
    foreach ($mutation in $mutations) {
        $changed = Copy-DeepObject $first
        & $mutation.Apply $changed
        Assert-ThrowsLike {
            Assert-HostV2GateEvidenceEqual -First $first -Second $changed
        } $mutation.Path
    }

    $statePath = Join-Path $testRoot 'stage-state.json'
    $executed = [Collections.Generic.List[string]]::new()
    $stages = @(
        @{ Name = 'first'; Action = { $executed.Add('first') }.GetNewClosure() },
        @{ Name = 'broken'; Action = { $executed.Add('broken'); throw 'fixture failure' }.GetNewClosure() },
        @{ Name = 'must-not-run'; Action = { $executed.Add('must-not-run') }.GetNewClosure() }
    )
    Assert-ThrowsLike {
        Invoke-HostV2GateStageSequence -Stages $stages -StatePath $statePath
    } "阶段 'broken' 失败"
    Assert-True (($executed -join ',') -ceq 'first,broken') '失败后仍执行了后续阶段。'
    $stageState = @(Get-Content -Raw $statePath | ConvertFrom-Json)
    Assert-True ($stageState.Count -eq 2 -and $stageState[1].status -ceq 'failed') `
        '失败阶段没有写入状态证据。'

    $passRoot = Join-Path $testRoot 'pass'
    $required = @(
        'pass.log', 'stage-state.json', 'HostV2ProductionSurface\summary.json',
        'MyAvaloniaManagement\summary.json', 'MyAvaloniaManagement\Unit\Unit.trx',
        'MyAvaloniaManagement\UI\UI.trx', 'MyAvaloniaManagement\Plugin\Plugin.trx',
        'MyAvaloniaManagement\coverage\Cobertura.xml',
        'AdditionalSuites\PluginSdk\PluginSdk.trx', 'AdditionalSuites\DaTang\DaTang.trx',
        'AdditionalSuites\MySmallTools\MySmallTools.trx',
        'AdditionalSuites\BiliDownloader\BiliDownloader.trx',
        'Documentation\summary.json', 'ManagedPluginPackages\summary.json', 'WindowsSmoke\summary.json'
    )
    foreach ($relative in $required) { Write-FixtureFile (Join-Path $passRoot $relative) }
    $archivePath = Join-Path $passRoot 'ManagedPluginPackages\Fixture-2.0.0-win-x64.zip'
    $manifestPath = Join-Path $passRoot 'ManagedPluginPackages\Fixture-2.0.0-win-x64.manifest.json'
    Write-FixtureFile $archivePath
    Write-FixtureFile $manifestPath
    $first.managedPlugins.plugins[0].archive.sha256 = (Get-FileHash $archivePath -Algorithm SHA256).Hash
    $first.managedPlugins.plugins[0].manifest.sha256 = (Get-FileHash $manifestPath -Algorithm SHA256).Hash
    Assert-HostV2GateArtifacts -PassRoot $passRoot -Summary $first

    foreach ($relative in $required) {
        $path = Join-Path $passRoot $relative
        $backup = "$path.bak"
        Move-Item -LiteralPath $path -Destination $backup
        Assert-ThrowsLike {
            Assert-HostV2GateArtifacts -PassRoot $passRoot -Summary $first
        } $relative
        Move-Item -LiteralPath $backup -Destination $path
    }

    $badSmoke = Copy-DeepObject $first
    $badSmoke.windowsSmoke.layoutFileName = 'layout-v1.json'
    Assert-ThrowsLike {
        Assert-HostV2GateArtifacts -PassRoot $passRoot -Summary $badSmoke
    } 'layout-v2.json/schema 2'

    $allowedRoot = Join-Path $testRoot 'owned'
    $child = Join-Path $allowedRoot 'child'
    New-Item -ItemType Directory -Path $child -Force | Out-Null
    Assert-HostV2GateChildPath -Candidate $child -Parent $allowedRoot -Purpose '测试路径'
    Assert-ThrowsLike {
        Assert-HostV2GateChildPath -Candidate (Join-Path $testRoot 'sibling') `
            -Parent $allowedRoot -Purpose '测试路径'
    } '允许根之外'
    $readOnlyFile = Join-Path $child 'readonly.txt'
    Write-FixtureFile $readOnlyFile
    (Get-Item -LiteralPath $readOnlyFile).Attributes = [IO.FileAttributes]::ReadOnly
    Remove-HostV2GateOwnedTree -Path $child -AllowedParent $allowedRoot -Purpose '测试清理'
    Assert-True (-not (Test-Path -LiteralPath $child)) '带只读文件的自有目录没有被清理。'

    Write-Host '[G14 V2] 核心单元测试通过：规范化比较、失败即停、证据完整性、V2 Smoke 与路径安全均符合预期。'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-HostV2GateOwnedTree -Path $testRoot -AllowedParent $temporaryParent `
            -Purpose 'G14 V2 核心测试清理'
    }
}
