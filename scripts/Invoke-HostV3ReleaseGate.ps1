[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'HostV3ReleaseGate.Core.psm1') -Force

function Invoke-NativeChecked {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$WorkingDirectory
    )

    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "$Name 失败，退出码 $LASTEXITCODE。" }
    }
    finally { Pop-Location }
}

function Copy-EvidenceDirectory {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination,
        [Parameter(Mandatory)] [string]$AllowedParent
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { return }
    Assert-HostV3GateChildPath -Candidate $Destination -Parent $AllowedParent -Purpose 'G14 V3 证据复制'
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse
}

function Copy-PassEvidence {
    param(
        [Parameter(Mandatory)] [string]$CloneRoot,
        [Parameter(Mandatory)] [string]$PassRoot
    )

    # 只收集 G14 声明的审计材料。bin/obj、临时 NuGet 缓存和 Smoke publish 目录均可重建，
    # 不能因为体积大就冒充发布证据；TRX、覆盖率、摘要、最终 ZIP/manifest 和 Harness 报告必须保留。
    foreach ($entry in @(
            @{ Source = 'artifacts\test-results\HostV3ProductionSurface'; Destination = 'HostV3ProductionSurface' },
            @{ Source = 'artifacts\test-results\MyAvaloniaManagement'; Destination = 'MyAvaloniaManagement' },
            @{ Source = 'artifacts\test-results\Documentation'; Destination = 'Documentation' },
            @{ Source = 'artifacts\test-results\HostV3ProductionSurface\ManagedPluginPackages'; Destination = 'ManagedPluginPackages' },
            @{ Source = 'artifacts\test-results\WindowsSmoke'; Destination = 'WindowsSmoke' })) {
        Copy-EvidenceDirectory (Join-Path $CloneRoot $entry.Source) `
            (Join-Path $PassRoot $entry.Destination) $PassRoot
    }

    # G13 聚合入口把五个独立测试项目写入自己的结果目录。这里统一复制只为便于审计，
    # 不改变叶子测试项目的所有权和零失败、零跳过断言。
    $additionalRoot = Join-Path $PassRoot 'AdditionalSuites'
    foreach ($suite in 'PluginSdk', 'MyPlugTest', 'DaTang', 'MySmallTools', 'BiliDownloader') {
        Copy-EvidenceDirectory `
            (Join-Path $CloneRoot "artifacts\test-results\HostV3ProductionSurface\$suite") `
            (Join-Path $additionalRoot $suite) $PassRoot
    }

    # G9–G12 专项脚本包含生产面聚合之外的最终 Workspace、覆盖率、真实测试 ZIP 与资源断言。
    # 独立保存四份目录，避免后续聚合时丢掉 MySmallTools 真实媒体 Harness 等高价值证据。
    $acceptanceRoot = Join-Path $PassRoot 'PluginAcceptances'
    foreach ($suite in 'MyPlugTestV3', 'DaTangAccountingHelpPlugV3', 'MySmallToolsV3', 'BiliDownloaderV3') {
        Copy-EvidenceDirectory `
            (Join-Path $CloneRoot "artifacts\test-results\$suite") `
            (Join-Path $acceptanceRoot $suite) $PassRoot
    }
}

function Get-ApiEntryCount {
    param([Parameter(Mandatory)] [string]$Path)

    $lines = @(Get-Content -LiteralPath $Path)
    if ($lines.Count -eq 0 -or $lines[0] -cne '#nullable enable') {
        throw "API 基线缺少 #nullable enable 头：$Path"
    }
    return @($lines | Select-Object -Skip 1 |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
}

function Get-SdkApiEvidence {
    param(
        [Parameter(Mandatory)] [string]$CloneRoot,
        [Parameter(Mandatory)] [string]$Baseline
    )

    $definitions = @(
        @{ Name = 'Core'; Path = 'Host\MyAvaloniaManagement.PluginSdk\ApiCompatibility' },
        @{ Name = 'UI'; Path = 'Host\MyAvaloniaManagement.PluginSdk.UI\ApiCompatibility' }
    )
    return @($definitions | ForEach-Object {
        $root = Join-Path $CloneRoot (Join-Path $_.Path $Baseline)
        [ordered]@{
            project = $_.Name
            shipped = Get-ApiEntryCount (Join-Path $root 'PublicAPI.Shipped.txt')
            unshipped = Get-ApiEntryCount (Join-Path $root 'PublicAPI.Unshipped.txt')
        }
    })
}

function Add-PluginManifestEvidence {
    param(
        [Parameter(Mandatory)] $PackageSummary,
        [Parameter(Mandatory)] [string]$PackageEvidenceRoot
    )

    # ZIP 与外置 manifest 都是交付物。矩阵摘要已经记录 ZIP，这里读取并复核 manifest 的
    # schema、SDK 区间、长度与 SHA-256，使两轮比较能发现清单被替换、截断或重新编码。
    foreach ($plugin in @($PackageSummary.plugins)) {
        $archiveName = [string]$plugin.archive.file
        $manifestName = [IO.Path]::GetFileNameWithoutExtension($archiveName) + '.manifest.json'
        $manifestPath = Join-Path $PackageEvidenceRoot $manifestName
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "G14 V3 插件 $($plugin.pluginId) 缺少外置清单：$manifestName"
        }
        $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
        $manifestFile = Get-Item -LiteralPath $manifestPath
        $plugin | Add-Member -NotePropertyName manifest -NotePropertyValue ([ordered]@{
            file = $manifestName
            length = $manifestFile.Length
            sha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
            schemaVersion = [int]$manifest.schemaVersion
            sdkMinInclusive = [string]$manifest.sdk.minInclusive
            sdkMaxExclusive = [string]$manifest.sdk.maxExclusive
        }) -Force
    }
    return $PackageSummary
}

function Get-OptionalPropertyValue {
    param([Parameter(Mandatory)] $Value, [Parameter(Mandatory)] [string]$Name)
    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function ConvertTo-StablePluginAcceptance {
    param([Parameter(Mandatory)] $Summary)

    # 四个专项摘要具有少量插件特有字段。这里显式投影所有稳定事实，只排除生成时间；
    # 不使用反射阶段发现，也不让绝对结果目录或耗时进入两轮可重复性判断。
    return [ordered]@{
        schemaVersion = $Summary.schemaVersion
        configuration = $Summary.configuration
        suites = $Summary.suites
        passed = $Summary.passed
        failed = $Summary.failed
        hostCoverage = $Summary.hostCoverage
        pluginCoverage = Get-OptionalPropertyValue $Summary 'pluginCoverage'
        myPlugTestCoverage = Get-OptionalPropertyValue $Summary 'myPlugTestCoverage'
        harness = Get-OptionalPropertyValue $Summary 'harness'
        manifest = $Summary.manifest
        archiveSha256 = $Summary.archiveSha256
        packageFiles = $Summary.packageFiles
        deterministicBuilds = $Summary.deterministicBuilds
        runtimeIdentifiers = Get-OptionalPropertyValue $Summary 'runtimeIdentifiers'
        workspaceDocuments = Get-OptionalPropertyValue $Summary 'workspaceDocuments'
        workspaceCreationIntents = Get-OptionalPropertyValue $Summary 'workspaceCreationIntents'
        workspaceTools = Get-OptionalPropertyValue $Summary 'workspaceTools'
        pluginLifecycles = Get-OptionalPropertyValue $Summary 'pluginLifecycles'
        aiflow = $Summary.aiflow
        windowsCi = $Summary.windowsCi
        windowsSmoke = $Summary.windowsSmoke
        releaseAcceptance = $Summary.releaseAcceptance
        releaseGate = $Summary.releaseGate
        publishable = $Summary.publishable
    }
}

function New-PassSummary {
    param(
        [Parameter(Mandatory)] [string]$CloneRoot,
        [Parameter(Mandatory)] [string]$PassRoot,
        [Parameter(Mandatory)] [string]$Revision,
        [Parameter(Mandatory)] [string]$SourceTree,
        [Parameter(Mandatory)] [string]$SdkVersion,
        [Parameter(Mandatory)] [object[]]$Stages,
        [Parameter(Mandatory)] [Diagnostics.Stopwatch]$Stopwatch
    )

    $production = Get-Content -Raw `
        (Join-Path $PassRoot 'HostV3ProductionSurface\summary.json') | ConvertFrom-Json
    $documentation = Get-Content -Raw `
        (Join-Path $PassRoot 'Documentation\summary.json') | ConvertFrom-Json
    $packageRoot = Join-Path $PassRoot 'ManagedPluginPackages'
    $packages = Get-Content -Raw (Join-Path $packageRoot 'summary.json') | ConvertFrom-Json
    $packages = Add-PluginManifestEvidence $packages $packageRoot
    $smoke = Get-Content -Raw (Join-Path $PassRoot 'WindowsSmoke\summary.json') | ConvertFrom-Json
    [xml]$versions = Get-Content -Raw (Join-Path $CloneRoot 'Directory.Version.props')
    $baseline = [string]$versions.Project.PropertyGroup.MyAvaloniaPluginSdkApiBaseline
    $sdkPackageVersion = [string]$versions.Project.PropertyGroup.MyAvaloniaPluginSdkVersion

    $acceptanceRoot = Join-Path $PassRoot 'PluginAcceptances'
    $pluginAcceptances = [ordered]@{}
    foreach ($entry in @(
            @{ Name = 'MyPlugTest'; Directory = 'MyPlugTestV3' },
            @{ Name = 'DaTang'; Directory = 'DaTangAccountingHelpPlugV3' },
            @{ Name = 'MySmallTools'; Directory = 'MySmallToolsV3' },
            @{ Name = 'BiliDownloader'; Directory = 'BiliDownloaderV3' })) {
        $raw = Get-Content -Raw `
            (Join-Path $acceptanceRoot "$($entry.Directory)\summary.json") | ConvertFrom-Json
        $pluginAcceptances[$entry.Name] = ConvertTo-StablePluginAcceptance $raw
    }

    return [ordered]@{
        schemaVersion = 1
        baseline = $baseline
        sourceRevision = $Revision
        sourceTree = $SourceTree
        passed = $true
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        durationMilliseconds = $Stopwatch.ElapsedMilliseconds
        evidenceRoot = $PassRoot
        platform = [ordered]@{
            operatingSystem = 'Windows'
            architecture = 'X64'
            configuration = 'Release'
        }
        sdkVersion = $SdkVersion
        sdkPackageVersion = $sdkPackageVersion
        aiflow = $false
        publishable = $true
        published = $false
        uploaded = $false
        tagCreated = $false
        stages = @($Stages)
        productionSurface = [ordered]@{
            host = [ordered]@{
                suites = $production.host.suites
                passed = $production.host.passed
                lineCoverage = $production.host.lineCoverage
                branchCoverage = $production.host.branchCoverage
            }
            additionalSuites = $production.additionalSuites
            gates = $production.gates
        }
        sdkApi = [ordered]@{
            baseline = $baseline
            projects = @(Get-SdkApiEvidence $CloneRoot $baseline)
        }
        documentation = [ordered]@{
            documents = $documentation.documents
            currentDocuments = $documentation.currentDocuments
            localLinks = $documentation.localLinks
            commandPaths = $documentation.commandPaths
            projectPaths = $documentation.projectPaths
            productVersion = $documentation.productVersion
            sdkVersion = $documentation.sdkVersion
            apiBaseline = $documentation.apiBaseline
            shippedApiEntries = $documentation.shippedApiEntries
            unshippedApiEntries = $documentation.unshippedApiEntries
            apiProjects = $documentation.apiProjects
            plugins = $documentation.plugins
        }
        pluginAcceptances = $pluginAcceptances
        managedPlugins = $packages
        windowsSmoke = [ordered]@{
            passed = $smoke.passed
            exitCode = $smoke.exitCode
            layoutSaved = $smoke.layoutSaved
            layoutFileName = $smoke.layoutFileName
            layoutSchemaVersion = $smoke.layoutSchemaVersion
            legacyLayoutAbsent = $smoke.legacyLayoutAbsent
            isolatedDataDirectory = $smoke.isolatedDataDirectory
        }
    }
}

function Set-PassEnvironment {
    param([Parameter(Mandatory)] [string]$RuntimeRoot)

    New-Item -ItemType Directory -Path $RuntimeRoot -Force | Out-Null
    $values = [ordered]@{
        CI = 'true'
        DOTNET_CLI_HOME = (Join-Path $RuntimeRoot 'dotnet-home')
        NUGET_PACKAGES = (Join-Path $RuntimeRoot 'nuget-packages')
        NUGET_HTTP_CACHE_PATH = (Join-Path $RuntimeRoot 'nuget-http-cache')
        TEMP = (Join-Path $RuntimeRoot 'temp')
        TMP = (Join-Path $RuntimeRoot 'temp')
        MYAVALONIA_DATA_DIRECTORY = (Join-Path $RuntimeRoot 'host-data')
        # portable PDB 与 Avalonia XAML 后处理会嵌入路径。两轮共享同一稳定逻辑路径，
        # 物理文件仍位于各自隔离 TEMP，由插件构建入口使用 Junction 与 PathMap 归一化。
        MYAVALONIA_MANAGED_PLUGIN_STABLE_ROOT =
            (Join-Path (Split-Path -Parent $RuntimeRoot) 'stable-managed-plugin-build')
        DOTNET_NOLOGO = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        MSBUILDDISABLENODEREUSE = '1'
        DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    }
    foreach ($key in @(
            'DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'TEMP',
            'MYAVALONIA_DATA_DIRECTORY', 'MYAVALONIA_MANAGED_PLUGIN_STABLE_ROOT')) {
        New-Item -ItemType Directory -Path $values[$key] -Force | Out-Null
    }

    $previous = @{}
    foreach ($entry in $values.GetEnumerator()) {
        $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
    return $previous
}

function Restore-PassEnvironment {
    param([Parameter(Mandatory)] [hashtable]$Previous)
    foreach ($entry in $Previous.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
}

function Invoke-ReleaseGatePass {
    param(
        [Parameter(Mandatory)] [int]$PassNumber,
        [Parameter(Mandatory)] [string]$CloneRoot,
        [Parameter(Mandatory)] [string]$PassRoot,
        [Parameter(Mandatory)] [string]$Revision,
        [Parameter(Mandatory)] [string]$SourceTree,
        [Parameter(Mandatory)] [string]$SdkVersion
    )

    New-Item -ItemType Directory -Path $PassRoot -Force | Out-Null
    $statePath = Join-Path $PassRoot 'stage-state.json'
    $summaryPath = Join-Path $PassRoot 'summary.json'
    $logPath = Join-Path $PassRoot 'pass.log'
    $runtimeRoot = Join-Path (Split-Path -Parent $CloneRoot) "runtime-pass-$PassNumber"
    $previousEnvironment = Set-PassEnvironment $runtimeRoot
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $transcriptStarted = $false
    try {
        Start-Transcript -LiteralPath $logPath -UseMinimalHeader | Out-Null
        $transcriptStarted = $true
        Write-Host "[G14 V3] 第 $PassNumber 轮隔离根：$CloneRoot"
        Write-Host "[G14 V3] 源提交：$Revision；源树：$SourceTree"
        Invoke-NativeChecked '.NET 环境快照' 'dotnet' @('--info') $CloneRoot

        $scripts = Join-Path $CloneRoot 'scripts'
        $solution = Join-Path $CloneRoot 'MyAvaloniaManagement.sln'
        # Closure 固定本轮路径和调用器；Core module 不需要知道 pwsh、dotnet 或任何业务脚本。
        $invokeNativeChecked = ${function:Invoke-NativeChecked}
        $invokePowerShellChecked = {
            param([string]$Name, [string]$ScriptPath, [string[]]$Arguments, [string]$WorkingDirectory)
            & $invokeNativeChecked $Name 'pwsh' `
                (@('-NoLogo', '-NoProfile', '-File', $ScriptPath) + $Arguments) $WorkingDirectory
        }.GetNewClosure()
        $stages = @(
            @{ Name = 'release-gate-core-unit-tests'; Action = {
                & $invokePowerShellChecked 'G14 V3 核心单元测试' `
                    (Join-Path $scripts 'Test-HostV3ReleaseGateCore.ps1') @() $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'documentation-core-unit-tests'; Action = {
                & $invokePowerShellChecked '文档门禁核心单元测试' `
                    (Join-Path $scripts 'Test-DocumentationCore.ps1') @() $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'locked-restore'; Action = {
                & $invokeNativeChecked '解决方案锁定还原' 'dotnet' @(
                    'restore', $solution, '--locked-mode', '--disable-parallel',
                    '-p:SkipPluginDeploy=true', '--nologo') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'release-ci-build'; Action = {
                & $invokeNativeChecked '解决方案 Release CI 零警告构建' 'dotnet' @(
                    'build', $solution, '-c', 'Release', '--no-restore', '--nologo', '-warnaserror',
                    '-p:SkipPluginDeploy=true', '-p:ContinuousIntegrationBuild=true') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'host-v3-production-surface'; Action = {
                & $invokePowerShellChecked 'V3 生产面全量门禁' `
                    (Join-Path $scripts 'Test-HostV3ProductionSurface.ps1') `
                    @('-Configuration', 'Release', '-NoRestore') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'plugin-sdk-v3-api-compatibility'; Action = {
                & $invokePowerShellChecked 'Plugin SDK V3 API 兼容门禁' `
                    (Join-Path $scripts 'Test-PluginSdkCompatibility.ps1') `
                    @('-Baseline', 'v3', '-Configuration', 'Release') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'plugin-sdk-v3-package'; Action = {
                & $invokePowerShellChecked 'Plugin SDK V3 独立包消费门禁' `
                    (Join-Path $scripts 'Test-PluginSdkPackage.ps1') @('-Configuration', 'Release') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'my-plug-test-v3-acceptance'; Action = {
                & $invokePowerShellChecked 'MyPlugTest V3 专项验收' `
                    (Join-Path $scripts 'Test-MyPlugTestV3.ps1') `
                    @('-Configuration', 'Release', '-NoRestore') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'datang-v3-acceptance'; Action = {
                & $invokePowerShellChecked 'DaTang V3 专项验收' `
                    (Join-Path $scripts 'Test-DaTangAccountingHelpPlugV3.ps1') `
                    @('-Configuration', 'Release', '-NoRestore') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'my-small-tools-v3-acceptance'; Action = {
                & $invokePowerShellChecked 'MySmallTools V3 专项验收与 20 轮资源 Harness' `
                    (Join-Path $scripts 'Test-MySmallToolsV3.ps1') `
                    @('-Configuration', 'Release', '-NoRestore', '-HarnessCycles', '20') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'bili-downloader-v3-acceptance'; Action = {
                & $invokePowerShellChecked 'BiliDownloader V3 专项验收' `
                    (Join-Path $scripts 'Test-BiliDownloaderV3.ps1') `
                    @('-Configuration', 'Release', '-NoRestore') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'windows-real-window-v3-smoke'; Action = {
                & $invokePowerShellChecked 'Windows 真实窗口 V3 Smoke' `
                    (Join-Path $scripts 'Invoke-MyAvaloniaManagementWindowsSmoke.ps1') `
                    @('-Configuration', 'Release', '-NoRestore') $CloneRoot
            }.GetNewClosure() }
        )
        $stageResults = @(Invoke-HostV3GateStageSequence -Stages $stages -StatePath $statePath)
        $stopwatch.Stop()
        Stop-Transcript | Out-Null
        $transcriptStarted = $false

        Copy-PassEvidence $CloneRoot $PassRoot
        $summary = New-PassSummary $CloneRoot $PassRoot $Revision $SourceTree $SdkVersion $stageResults $stopwatch
        Write-HostV3GateJson -Path $summaryPath -Value $summary
        Assert-HostV3GateArtifacts -PassRoot $PassRoot -Summary $summary
        return $summary
    }
    catch {
        $stopwatch.Stop()
        if ($transcriptStarted) { try { Stop-Transcript | Out-Null } catch { } }
        Copy-PassEvidence $CloneRoot $PassRoot
        $stageResults = if (Test-Path -LiteralPath $statePath) {
            @(Get-Content -Raw $statePath | ConvertFrom-Json)
        } else { @() }
        Write-HostV3GateJson -Path $summaryPath -Value ([ordered]@{
            schemaVersion = 1
            baseline = 'v3'
            sourceRevision = $Revision
            sourceTree = $SourceTree
            passed = $false
            aiflow = $false
            publishable = $false
            published = $false
            uploaded = $false
            tagCreated = $false
            generatedAtUtc = [DateTime]::UtcNow.ToString('O')
            durationMilliseconds = $stopwatch.ElapsedMilliseconds
            evidenceRoot = $PassRoot
            stages = $stageResults
            error = $_.Exception.Message
        })
        throw
    }
    finally { Restore-PassEnvironment $previousEnvironment }
}

if ($env:OS -ne 'Windows_NT' -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'G14 V3 正式发布门禁只支持 Windows x64。'
}
if ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
    throw "G14 V3 要求 PowerShell 7 或更高版本，当前为 $($PSVersionTable.PSVersion)。"
}
foreach ($command in @('git', 'dotnet', 'pwsh')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "G14 V3 缺少必需命令：$command"
    }
}

Push-Location $repositoryRoot
try {
    $dirty = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw '无法读取 Git 工作区状态。' }
    if ($dirty.Count -ne 0) {
        throw "G14 V3 正式门禁只接受干净提交；请先审阅以下变化：`n$($dirty -join [Environment]::NewLine)"
    }
    $revision = (& git rev-parse HEAD).Trim()
    $shortRevision = (& git rev-parse --short=12 HEAD).Trim()
    $sourceTree = (& git rev-parse "$revision`^{tree}").Trim()
    if ($LASTEXITCODE -ne 0) { throw '无法解析当前提交及源树。' }
    $expectedSdk = [string](Get-Content -Raw 'global.json' | ConvertFrom-Json).sdk.version
    $actualSdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualSdk -cne $expectedSdk) {
        throw "G14 V3 要求 .NET SDK $expectedSdk，当前解析为 $actualSdk。"
    }
}
finally { Pop-Location }

$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$runName = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + $shortRevision
$runRoot = Join-Path $artifactRoot "release-gate\v3\$runName"
Assert-HostV3GateChildPath -Candidate $runRoot -Parent $artifactRoot -Purpose 'G14 V3 发布证据目录'
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
# 使用短且随机的自有根目录，避免嵌套 NuGet/Avalonia 输出越过 Windows 传统路径上限；
# 隔离性由随机根、两个无硬链接克隆和各自 runtime 目录共同保证。
$temporaryRoot = Join-Path $temporaryParent ('MAV3G-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
Assert-HostV3GateChildPath -Candidate $temporaryRoot -Parent $temporaryParent -Purpose 'G14 V3 临时根'
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

$overallPath = Join-Path $runRoot 'summary.json'
$passSummaries = [Collections.Generic.List[object]]::new()
$overallStopwatch = [Diagnostics.Stopwatch]::StartNew()
try {
    for ($pass = 1; $pass -le 2; $pass++) {
        $cloneRoot = Join-Path $temporaryRoot "source-pass-$pass"
        Invoke-NativeChecked "创建第 $pass 轮独立本地克隆" 'git' @(
            'clone', '--no-hardlinks', '--quiet', $repositoryRoot, $cloneRoot) $repositoryRoot
        Invoke-NativeChecked "第 $pass 轮固定源提交" 'git' @(
            'checkout', '--detach', '--quiet', $revision) $cloneRoot
        $cloneStatus = @(& git -C $cloneRoot status --porcelain --untracked-files=all)
        if ($LASTEXITCODE -ne 0 -or $cloneStatus.Count -ne 0) {
            throw "第 $pass 轮隔离克隆不是干净提交。"
        }

        $passRoot = Join-Path $runRoot "pass-$pass"
        $passSummary = Invoke-ReleaseGatePass $pass $cloneRoot $passRoot $revision $sourceTree $actualSdk
        $passSummaries.Add($passSummary)
    }

    Assert-HostV3GateEvidenceEqual -First $passSummaries[0] -Second $passSummaries[1]
    $overallStopwatch.Stop()
    Write-HostV3GateJson -Path $overallPath -Value ([ordered]@{
        schemaVersion = 1
        baseline = 'v3'
        sourceRevision = $revision
        sourceTree = $sourceTree
        passed = $true
        repeatabilityVerified = $true
        releaseEligible = $true
        publishable = $true
        published = $false
        uploaded = $false
        tagCreated = $false
        aiflow = $false
        passCount = 2
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        durationMilliseconds = $overallStopwatch.ElapsedMilliseconds
        evidenceRoot = $runRoot
        passes = @($passSummaries | ForEach-Object { $_.evidenceRoot })
    })
    Write-Host "`nG14 Managed Plugin V3 发布门禁通过：两轮隔离结果一致。"
    Write-Host "发布证据：$runRoot"
}
catch {
    $overallStopwatch.Stop()
    Write-HostV3GateJson -Path $overallPath -Value ([ordered]@{
        schemaVersion = 1
        baseline = 'v3'
        sourceRevision = $revision
        sourceTree = $sourceTree
        passed = $false
        repeatabilityVerified = $false
        releaseEligible = $false
        publishable = $false
        published = $false
        uploaded = $false
        tagCreated = $false
        aiflow = $false
        passCount = $passSummaries.Count
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        durationMilliseconds = $overallStopwatch.ElapsedMilliseconds
        evidenceRoot = $runRoot
        error = $_.Exception.Message
    })
    Write-Error "G14 V3 发布门禁失败；已保留证据：$runRoot；原因：$($_.Exception.Message)"
    throw
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        try {
            & dotnet build-server shutdown | Out-Host
            Remove-HostV3GateOwnedTree -Path $temporaryRoot -AllowedParent $temporaryParent `
                -Purpose 'G14 V3 临时根清理'
        }
        catch {
            # 临时清理失败只影响磁盘卫生，不得覆盖已经落盘的通过或失败结论。
            Write-Warning "G14 V3 临时目录清理失败，已保留 '$temporaryRoot'：$($_.Exception.Message)"
        }
    }
}
