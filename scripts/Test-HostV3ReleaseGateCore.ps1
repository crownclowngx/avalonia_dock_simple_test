[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
Import-Module (Join-Path $PSScriptRoot 'HostV3ReleaseGate.Core.psm1') -Force

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
    # JSON 往返刻意模拟正式入口从 summary.json 读取 PSCustomObject 的真实形态，
    # 避免单元测试只覆盖 OrderedDictionary 而漏掉磁盘反序列化行为。
    return $Value | ConvertTo-Json -Depth 40 | ConvertFrom-Json
}

# 正式入口包含少量只服务于摘要投影的本地函数，不能通过 Import-Module 直接调用。
# 从 PowerShell AST 精确提取可选字段读取函数并真实执行，防止“语法可解析、运行时却把
# 条件表达式当作命令”的缺陷再次等到整轮业务验收结束后才暴露。
$entryPath = Join-Path $PSScriptRoot 'Invoke-HostV3ReleaseGate.ps1'
$parseTokens = $null
$parseErrors = $null
$entryAst = [Management.Automation.Language.Parser]::ParseFile(
    $entryPath, [ref]$parseTokens, [ref]$parseErrors)
Assert-True (@($parseErrors).Count -eq 0) 'G14 V3 正式入口存在 PowerShell 语法错误。'
$optionalPropertyFunction = $entryAst.Find({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -ceq 'Get-OptionalPropertyValue'
}, $true)
Assert-True ($null -ne $optionalPropertyFunction) '正式入口缺少可选摘要字段读取函数。'
Invoke-Expression $optionalPropertyFunction.Extent.Text
$projectionFixture = [pscustomobject]@{ present = 42 }
Assert-True ((Get-OptionalPropertyValue $projectionFixture 'present') -eq 42) `
    '可选摘要字段存在时未返回原值。'
Assert-True ($null -eq (Get-OptionalPropertyValue $projectionFixture 'missing')) `
    '可选摘要字段缺失时必须返回 null。'

function New-FixtureAcceptance {
    param([Parameter(Mandatory)] [string]$PluginId)
    return [ordered]@{
        schemaVersion = 1
        configuration = 'Release'
        suites = [ordered]@{ Unit = 10 }
        passed = 10
        failed = 0
        hostCoverage = [ordered]@{ line = 84.39; branch = 70.58 }
        manifest = [ordered]@{
            schemaVersion = 2
            pluginId = $PluginId
            pluginVersion = '3.0.0'
            sdkMinInclusive = '3.0.0'
            sdkMaxExclusive = '4.0.0'
        }
        archiveSha256 = 'fixture'
        packageFiles = 10
        deterministicBuilds = 2
    }
}

function New-FixtureSummary {
    $myPlugTest = New-FixtureAcceptance 'myavalonia.plugin.my-plug-test'
    $daTang = New-FixtureAcceptance 'myavalonia.plugin.datang-accounting-help'
    $mySmallTools = New-FixtureAcceptance 'myavalonia.plugin.my-small-tools'
    $mySmallTools.harness = [ordered]@{
        suite = 'g3'; cycles = 20; success = $true; allFinalResourcesZero = $true
        aliveClosedDocuments = 0; aliveClosedViews = 0; aliveDisposedEncryptedStreams = 0
        report = 'real-media-harness.json'
    }
    $bili = New-FixtureAcceptance 'myavalonia.plugin.bili-downloader'
    $packagePlugins = @(
        @{ Id = 'myavalonia.plugin.bili-downloader'; Name = 'BiliDownloader' },
        @{ Id = 'myavalonia.plugin.datang-accounting-help'; Name = 'DaTangAccountingHelpPlug' },
        @{ Id = 'myavalonia.plugin.my-plug-test'; Name = 'MyPlugTest' },
        @{ Id = 'myavalonia.plugin.my-small-tools'; Name = 'MySmallTools' }
    ) | ForEach-Object {
        [ordered]@{
            pluginId = $_.Id
            pluginVersion = '3.0.0'
            archive = [ordered]@{
                file = "$($_.Name)-3.0.0-win-x64.zip"; length = 3; sha256 = ''
            }
            manifest = [ordered]@{
                file = "$($_.Name)-3.0.0-win-x64.manifest.json"; length = 3; sha256 = ''
                schemaVersion = 2; sdkMinInclusive = '3.0.0'; sdkMaxExclusive = '4.0.0'
            }
        }
    }
    $value = [ordered]@{
        schemaVersion = 1
        baseline = 'v3'
        sourceRevision = '0123456789abcdef'
        sourceTree = 'fedcba9876543210'
        passed = $true
        generatedAtUtc = '2026-08-23T00:00:00Z'
        durationMilliseconds = 100
        evidenceRoot = 'C:\first'
        platform = [ordered]@{ operatingSystem = 'Windows'; architecture = 'X64'; configuration = 'Release' }
        sdkVersion = '10.0.302'
        sdkPackageVersion = '3.0.0'
        aiflow = $false
        publishable = $true
        published = $false
        uploaded = $false
        tagCreated = $false
        stages = @(
            [ordered]@{ name = 'core'; status = 'passed'; startedAtUtc = 'first'; durationMilliseconds = 1; error = $null },
            [ordered]@{ name = 'tests'; status = 'passed'; startedAtUtc = 'second'; durationMilliseconds = 2; error = $null }
        )
        productionSurface = [ordered]@{
            host = [ordered]@{
                suites = [ordered]@{ Unit = 173; UI = 58; Plugin = 224 }
                passed = 455; lineCoverage = 84.39; branchCoverage = 70.58
            }
            additionalSuites = [ordered]@{
                PluginSdk = 34; MyPlugTest = 13; DaTang = 64; MySmallTools = 183; BiliDownloader = 734
            }
            gates = [ordered]@{ sourceScan = $true; finalZipHostLoad = $true }
        }
        sdkApi = [ordered]@{
            baseline = 'v3'
            projects = @(
                [ordered]@{ project = 'Core'; shipped = 127; unshipped = 0 },
                [ordered]@{ project = 'UI'; shipped = 45; unshipped = 0 }
            )
        }
        documentation = [ordered]@{
            documents = 50; currentDocuments = 30; localLinks = 300; commandPaths = 100; projectPaths = 50
            productVersion = '3.0.0'; sdkVersion = '3.0.0'; apiBaseline = 'v3'
            shippedApiEntries = 172; unshippedApiEntries = 0
        }
        pluginAcceptances = [ordered]@{
            MyPlugTest = $myPlugTest
            DaTang = $daTang
            MySmallTools = $mySmallTools
            BiliDownloader = $bili
        }
        managedPlugins = [ordered]@{
            schemaVersion = 1
            configuration = 'Release'
            platform = 'win-x64'
            gates = [ordered]@{ finalZipHostLoad = $true; deterministicBuildsPerPlugin = 2 }
            plugins = @($packagePlugins)
        }
        windowsSmoke = [ordered]@{
            passed = $true; exitCode = 0; layoutSaved = $true; layoutFileName = 'layout-v2.json'
            layoutSchemaVersion = 2; legacyLayoutAbsent = $true; isolatedDataDirectory = $true
        }
    }
    return Copy-DeepObject $value
}

function Write-FixtureFile {
    param([Parameter(Mandatory)] [string]$Path, [string]$Content = 'abc')
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $temporaryParent ('HostV3ReleaseGateCoreTests-' + [Guid]::NewGuid().ToString('N'))
Assert-HostV3GateChildPath -Candidate $testRoot -Parent $temporaryParent -Purpose 'G14 V3 核心测试目录'
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $first = New-FixtureSummary
    $same = Copy-DeepObject $first
    $same.generatedAtUtc = '2026-08-24T00:00:00Z'
    $same.durationMilliseconds = 999
    $same.evidenceRoot = 'D:\second'
    $same.stages[0].startedAtUtc = 'different'
    $same.stages[0].durationMilliseconds = 888
    Assert-HostV3GateEvidenceEqual -First $first -Second $same

    # 每个变异都对应 G14 的一项发布事实，错误必须包含可定位的 JSON 路径。
    $mutations = @(
        @{ Path = '$.productionSurface.host.suites.Unit'; Apply = { param($x) $x.productionSurface.host.suites.Unit++ } },
        @{ Path = '$.productionSurface.host.lineCoverage'; Apply = { param($x) $x.productionSurface.host.lineCoverage = 80 } },
        @{ Path = '$.sdkApi.projects[0].shipped'; Apply = { param($x) $x.sdkApi.projects[0].shipped-- } },
        @{ Path = '$.documentation.localLinks'; Apply = { param($x) $x.documentation.localLinks-- } },
        @{ Path = '$.pluginAcceptances.MySmallTools.harness.cycles'; Apply = { param($x) $x.pluginAcceptances.MySmallTools.harness.cycles = 19 } },
        @{ Path = '$.managedPlugins.plugins[0].archive.sha256'; Apply = { param($x) $x.managedPlugins.plugins[0].archive.sha256 = 'changed' } },
        @{ Path = '$.windowsSmoke.layoutFileName'; Apply = { param($x) $x.windowsSmoke.layoutFileName = 'layout-v3.json' } },
        @{ Path = '$.stages[1].status'; Apply = { param($x) $x.stages[1].status = 'failed' } }
    )
    foreach ($mutation in $mutations) {
        $changed = Copy-DeepObject $first
        & $mutation.Apply $changed
        Assert-ThrowsLike {
            Assert-HostV3GateEvidenceEqual -First $first -Second $changed
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
        Invoke-HostV3GateStageSequence -Stages $stages -StatePath $statePath
    } "阶段 'broken' 失败"
    Assert-True (($executed -join ',') -ceq 'first,broken') '失败后仍执行了后续阶段。'
    $stageState = @(Get-Content -Raw $statePath | ConvertFrom-Json)
    Assert-True ($stageState.Count -eq 2 -and $stageState[1].status -ceq 'failed') `
        '失败阶段没有写入状态证据。'

    $passRoot = Join-Path $testRoot 'pass'
    $required = @(
        'pass.log', 'stage-state.json', 'HostV3ProductionSurface\summary.json',
        'MyAvaloniaManagement\summary.json', 'MyAvaloniaManagement\Unit\Unit.trx',
        'MyAvaloniaManagement\UI\UI.trx', 'MyAvaloniaManagement\Plugin\Plugin.trx',
        'MyAvaloniaManagement\coverage\Cobertura.xml',
        'AdditionalSuites\PluginSdk\PluginSdk.trx', 'AdditionalSuites\MyPlugTest\MyPlugTest.trx',
        'AdditionalSuites\DaTang\DaTang.trx', 'AdditionalSuites\MySmallTools\MySmallTools.trx',
        'AdditionalSuites\BiliDownloader\BiliDownloader.trx', 'Documentation\summary.json',
        'ManagedPluginPackages\summary.json', 'PluginAcceptances\MyPlugTestV3\summary.json',
        'PluginAcceptances\DaTangAccountingHelpPlugV3\summary.json',
        'PluginAcceptances\MySmallToolsV3\summary.json',
        'PluginAcceptances\MySmallToolsV3\real-media-harness.json',
        'PluginAcceptances\BiliDownloaderV3\summary.json', 'WindowsSmoke\summary.json'
    )
    foreach ($relative in $required) { Write-FixtureFile (Join-Path $passRoot $relative) }
    foreach ($plugin in @($first.managedPlugins.plugins)) {
        $archivePath = Join-Path $passRoot ('ManagedPluginPackages\' + $plugin.archive.file)
        $manifestPath = Join-Path $passRoot ('ManagedPluginPackages\' + $plugin.manifest.file)
        Write-FixtureFile $archivePath
        Write-FixtureFile $manifestPath
        $plugin.archive.sha256 = (Get-FileHash $archivePath -Algorithm SHA256).Hash
        $plugin.manifest.sha256 = (Get-FileHash $manifestPath -Algorithm SHA256).Hash
    }
    Assert-HostV3GateArtifacts -PassRoot $passRoot -Summary $first

    foreach ($relative in $required) {
        $path = Join-Path $passRoot $relative
        $backup = "$path.bak"
        Move-Item -LiteralPath $path -Destination $backup
        Assert-ThrowsLike {
            Assert-HostV3GateArtifacts -PassRoot $passRoot -Summary $first
        } $relative
        Move-Item -LiteralPath $backup -Destination $path
    }

    $badManifest = Copy-DeepObject $first
    $badManifest.pluginAcceptances.MyPlugTest.manifest.schemaVersion = 3
    Assert-ThrowsLike {
        Assert-HostV3GateArtifacts -PassRoot $passRoot -Summary $badManifest
    } 'manifest'
    $badSdk = Copy-DeepObject $first
    $badSdk.managedPlugins.plugins[0].manifest.sdkMaxExclusive = '5.0.0'
    Assert-ThrowsLike {
        Assert-HostV3GateArtifacts -PassRoot $passRoot -Summary $badSdk
    } 'manifest 契约'
    $badSmoke = Copy-DeepObject $first
    $badSmoke.windowsSmoke.layoutSchemaVersion = 3
    Assert-ThrowsLike {
        Assert-HostV3GateArtifacts -PassRoot $passRoot -Summary $badSmoke
    } 'layout-v2.json/schema 2'

    $allowedRoot = Join-Path $testRoot 'owned'
    $child = Join-Path $allowedRoot 'child'
    New-Item -ItemType Directory -Path $child -Force | Out-Null
    Assert-HostV3GateChildPath -Candidate $child -Parent $allowedRoot -Purpose '测试路径'
    Assert-ThrowsLike {
        Assert-HostV3GateChildPath -Candidate (Join-Path $testRoot 'sibling') `
            -Parent $allowedRoot -Purpose '测试路径'
    } '允许根之外'
    $readOnlyFile = Join-Path $child 'readonly.txt'
    Write-FixtureFile $readOnlyFile
    (Get-Item -LiteralPath $readOnlyFile).Attributes = [IO.FileAttributes]::ReadOnly
    Remove-HostV3GateOwnedTree -Path $child -AllowedParent $allowedRoot -Purpose '测试清理'
    Assert-True (-not (Test-Path -LiteralPath $child)) '带只读文件的自有目录没有被清理。'

    Write-Host '[G14 V3] 核心单元测试通过：规范化比较、失败即停、证据完整性、API、四插件 Harness、Smoke 与路径安全均符合预期。'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-HostV3GateOwnedTree -Path $testRoot -AllowedParent $temporaryParent `
            -Purpose 'G14 V3 核心测试清理'
    }
}
