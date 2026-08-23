[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'HostV4ReleaseGate.Core.psm1') -Force

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
    Assert-HostV4GateChildPath -Candidate $Destination -Parent $AllowedParent -Purpose 'G8 V4 证据复制'
    if (Test-Path -LiteralPath $Destination) {
        Remove-HostV4GateOwnedTree -Path $Destination -AllowedParent $AllowedParent `
            -Purpose 'G8 V4 旧证据清理'
    }
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse
}

function Copy-PassEvidence {
    param(
        [Parameter(Mandatory)] [string]$CloneRoot,
        [Parameter(Mandatory)] [string]$PassRoot
    )

    # 只收集 G8 声明的审计材料。bin/obj、临时 NuGet 缓存和 Smoke publish 目录均可重建，
    # 不属于长期证据；TRX、覆盖率、摘要、最终 ZIP/manifest 和 Harness 报告必须保留。
    foreach ($entry in @(
            @{ Source = 'artifacts\test-results\HostV4\G7'; Destination = 'HostV4\G7' },
            @{ Source = 'artifacts\test-results\MyAvaloniaManagement'; Destination = 'MyAvaloniaManagement' },
            @{ Source = 'artifacts\test-results\Documentation'; Destination = 'Documentation' },
            @{ Source = 'artifacts\test-results\WindowsSmoke'; Destination = 'WindowsSmoke' })) {
        Copy-EvidenceDirectory (Join-Path $CloneRoot $entry.Source) `
            (Join-Path $PassRoot $entry.Destination) $PassRoot
    }

    # 四个 V3 专项入口拥有各自的测试、覆盖率、真实测试 ZIP、Host Loader 与资源断言。
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

function Get-ManagedPluginEvidence {
    param(
        [Parameter(Mandatory)] [string]$PassRoot,
        [Parameter(Mandatory)] $PluginAcceptances
    )

    # G7 的四个专项目录已经各自保存两次确定性构建结果。G8 只选择第一份已经过逐文件比较的
    # 测试 ZIP 作为审计实体，并重新计算 ZIP/manifest 长度和哈希；不再引入第二套打包实现。
    $definitions = @(
        @{ Name = 'BiliDownloader'; Directory = 'BiliDownloaderV3'; Prefix = 'BiliDownloader' },
        @{ Name = 'DaTang'; Directory = 'DaTangAccountingHelpPlugV3'; Prefix = 'DaTangAccountingHelpPlug' },
        @{ Name = 'MyPlugTest'; Directory = 'MyPlugTestV3'; Prefix = 'MyPlugTest' },
        @{ Name = 'MySmallTools'; Directory = 'MySmallToolsV3'; Prefix = 'MySmallTools' }
    )
    $plugins = foreach ($definition in $definitions) {
        $acceptance = $PluginAcceptances[$definition.Name]
        $archiveName = "$($definition.Prefix)-3.0.0-win-x64.zip"
        $manifestName = "$($definition.Prefix)-3.0.0-win-x64.manifest.json"
        $relativeRoot = "PluginAcceptances\$($definition.Directory)\package-first"
        $archiveRelativePath = "$relativeRoot\$archiveName"
        $manifestRelativePath = "$relativeRoot\$manifestName"
        $archivePath = Join-Path $PassRoot $archiveRelativePath
        $manifestPath = Join-Path $PassRoot $manifestRelativePath
        foreach ($path in @($archivePath, $manifestPath)) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "G8 V4 缺少最终插件包实体：$path"
            }
        }

        $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
        $archiveFile = Get-Item -LiteralPath $archivePath
        $manifestFile = Get-Item -LiteralPath $manifestPath
        $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
        if ($archiveHash -cne [string]$acceptance.archiveSha256) {
            throw "G8 V4 插件 $($manifest.pluginId) 的测试 ZIP 与专项摘要哈希不一致。"
        }

        [ordered]@{
            pluginId = [string]$manifest.pluginId
            pluginVersion = [string]$manifest.pluginVersion
            archive = [ordered]@{
                file = $archiveName
                relativePath = $archiveRelativePath
                length = $archiveFile.Length
                sha256 = $archiveHash
            }
            manifest = [ordered]@{
                file = $manifestName
                relativePath = $manifestRelativePath
                length = $manifestFile.Length
                sha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
                schemaVersion = [int]$manifest.schemaVersion
                sdkMinInclusive = [string]$manifest.sdk.minInclusive
                sdkMaxExclusive = [string]$manifest.sdk.maxExclusive
            }
        }
    }
    return [ordered]@{
        schemaVersion = 1
        configuration = 'Release'
        platform = 'win-x64'
        gates = [ordered]@{
            finalZipHostLoad = $true
            deterministicBuildsPerPlugin = 2
        }
        plugins = @($plugins)
    }
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
        skipped = $Summary.skipped
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

    $g7 = Get-Content -Raw `
        (Join-Path $PassRoot 'HostV4\G7\summary.json') | ConvertFrom-Json
    # PowerShell 变量名不区分大小写，`$Host` 又是内建只读变量，因此这里必须使用语义完整的名称。
    # 这也让投影职责更清楚：该对象是 Host 测试摘要，而不是 PowerShell 运行宿主。
    $hostTestSummary = Get-Content -Raw `
        (Join-Path $PassRoot 'MyAvaloniaManagement\summary.json') | ConvertFrom-Json
    $documentation = Get-Content -Raw `
        (Join-Path $PassRoot 'Documentation\summary.json') | ConvertFrom-Json
    $smoke = Get-Content -Raw (Join-Path $PassRoot 'WindowsSmoke\summary.json') | ConvertFrom-Json
    [xml]$versions = Get-Content -Raw (Join-Path $CloneRoot 'Directory.Version.props')
    $baseline = [string]$versions.Project.PropertyGroup.MyAvaloniaPluginSdkApiBaseline
    $sdkPackageVersion = [string]$versions.Project.PropertyGroup.MyAvaloniaPluginSdkVersion
    $productVersion = [string]$versions.Project.PropertyGroup.MyAvaloniaProductVersion

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
    $packages = Get-ManagedPluginEvidence -PassRoot $PassRoot -PluginAcceptances $pluginAcceptances

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
        productVersion = $productVersion
        sdkPackageVersion = $sdkPackageVersion
        aiflow = $false
        windowsCi = $false
        windowsSmokeExecuted = $true
        releaseAcceptance = $false
        releaseGate = $true
        releaseEligible = $true
        publishable = $true
        published = $false
        uploaded = $false
        tagCreated = $false
        stages = @($Stages)
        developmentGate = [ordered]@{
            schemaVersion = $g7.schemaVersion
            stage = $g7.stage
            configuration = $g7.configuration
            passed = $g7.passed
            hostSuites = $hostTestSummary.suites
            hostPassed = $g7.hostPassed
            hostLineCoverage = $g7.hostLineCoverage
            hostBranchCoverage = $g7.hostBranchCoverage
            sdkCompatibility = $g7.sdkCompatibility
            sdkPackageConsumption = $g7.sdkPackageConsumption
            diagnosticRedaction = $g7.diagnosticRedaction
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
        Write-Host "[G8 V4] 第 $PassNumber 轮隔离根：$CloneRoot"
        Write-Host "[G8 V4] 源提交：$Revision；源树：$SourceTree"
        Invoke-NativeChecked '.NET 环境快照' 'dotnet' @('--info') $CloneRoot

        $scripts = Join-Path $CloneRoot 'scripts'
        # Closure 固定本轮路径和调用器；Core module 不需要知道 pwsh、dotnet 或任何业务脚本。
        $invokeNativeChecked = ${function:Invoke-NativeChecked}
        $invokePowerShellChecked = {
            param([string]$Name, [string]$ScriptPath, [string[]]$Arguments, [string]$WorkingDirectory)
            & $invokeNativeChecked $Name 'pwsh' `
                (@('-NoLogo', '-NoProfile', '-File', $ScriptPath) + $Arguments) $WorkingDirectory
        }.GetNewClosure()
        $stages = @(
            @{ Name = 'release-gate-core-unit-tests'; Action = {
                & $invokePowerShellChecked 'G8 V4 核心单元测试' `
                    (Join-Path $scripts 'Test-HostV4ReleaseGateCore.ps1') @() $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'documentation-core-unit-tests'; Action = {
                & $invokePowerShellChecked '文档门禁核心单元测试' `
                    (Join-Path $scripts 'Test-DocumentationCore.ps1') @() $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'host-v4-g7-development-gate'; Action = {
                # G7 开发门禁已经按 SRP 拥有 locked restore、Release -warnaserror、Host 三层测试、
                # SDK API/包、诊断、四插件专项、确定性 ZIP、真实 Loader、Harness 和文档断言。
                # G8 只把它当作一个稳定叶子阶段，避免复制规则后产生两套真相。
                & $invokePowerShellChecked 'Host V4 G7 完整开发门禁' `
                    (Join-Path $scripts 'Test-HostV4DevelopmentGate.ps1') `
                    @('-Stage', 'G7', '-Configuration', 'Release') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'windows-real-window-v4-smoke'; Action = {
                & $invokePowerShellChecked 'Windows 真实窗口 V4 Smoke' `
                    (Join-Path $scripts 'Invoke-MyAvaloniaManagementWindowsSmoke.ps1') `
                    @('-Configuration', 'Release', '-NoRestore') $CloneRoot
            }.GetNewClosure() }
        )
        $stageResults = @(Invoke-HostV4GateStageSequence -Stages $stages -StatePath $statePath)
        $stopwatch.Stop()
        Stop-Transcript | Out-Null
        $transcriptStarted = $false

        Copy-PassEvidence $CloneRoot $PassRoot
        $summary = New-PassSummary $CloneRoot $PassRoot $Revision $SourceTree $SdkVersion $stageResults $stopwatch
        Write-HostV4GateJson -Path $summaryPath -Value $summary
        Assert-HostV4GateArtifacts -PassRoot $PassRoot -Summary $summary
        return $summary
    }
    catch {
        $stopwatch.Stop()
        if ($transcriptStarted) { try { Stop-Transcript | Out-Null } catch { } }
        Copy-PassEvidence $CloneRoot $PassRoot
        $stageResults = if (Test-Path -LiteralPath $statePath) {
            @(Get-Content -Raw $statePath | ConvertFrom-Json)
        } else { @() }
        Write-HostV4GateJson -Path $summaryPath -Value ([ordered]@{
            schemaVersion = 1
            baseline = 'v3'
            sourceRevision = $Revision
            sourceTree = $SourceTree
            passed = $false
            aiflow = $false
            windowsCi = $false
            windowsSmokeExecuted = $false
            releaseAcceptance = $false
            releaseGate = $true
            releaseEligible = $false
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
    throw 'G8 V4 本地封板门禁只支持 Windows x64。'
}
if ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
    throw "G8 V4 要求 PowerShell 7 或更高版本，当前为 $($PSVersionTable.PSVersion)。"
}
foreach ($command in @('git', 'dotnet', 'pwsh')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "G8 V4 缺少必需命令：$command"
    }
}

Push-Location $repositoryRoot
try {
    $dirty = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw '无法读取 Git 工作区状态。' }
    if ($dirty.Count -ne 0) {
        throw "G8 V4 封板门禁只接受干净提交；请先审阅以下变化：`n$($dirty -join [Environment]::NewLine)"
    }
    $revision = (& git rev-parse HEAD).Trim()
    $shortRevision = (& git rev-parse --short=12 HEAD).Trim()
    $sourceTree = (& git rev-parse "$revision`^{tree}").Trim()
    if ($LASTEXITCODE -ne 0) { throw '无法解析当前提交及源树。' }
    $expectedSdk = [string](Get-Content -Raw 'global.json' | ConvertFrom-Json).sdk.version
    $actualSdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualSdk -cne $expectedSdk) {
        throw "G8 V4 要求 .NET SDK $expectedSdk，当前解析为 $actualSdk。"
    }
}
finally { Pop-Location }

$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$runName = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + $shortRevision
$runRoot = Join-Path $artifactRoot "release-gate\v4\$runName"
Assert-HostV4GateChildPath -Candidate $runRoot -Parent $artifactRoot -Purpose 'G8 V4 发布证据目录'
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
# 使用短且随机的自有根目录，避免嵌套 NuGet/Avalonia 输出越过 Windows 传统路径上限；
# 隔离性由随机根、两个无硬链接克隆和各自 runtime 目录共同保证。
$temporaryRoot = Join-Path $temporaryParent ('MAV4G-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
Assert-HostV4GateChildPath -Candidate $temporaryRoot -Parent $temporaryParent -Purpose 'G8 V4 临时根'
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

    Assert-HostV4GateEvidenceEqual -First $passSummaries[0] -Second $passSummaries[1]
    $overallStopwatch.Stop()
    Write-HostV4GateJson -Path $overallPath -Value ([ordered]@{
        schemaVersion = 1
        baseline = 'v3'
        sourceRevision = $revision
        sourceTree = $sourceTree
        passed = $true
        repeatabilityVerified = $true
        releaseEligible = $true
        windowsCi = $false
        windowsSmoke = $true
        releaseAcceptance = $false
        releaseGate = $true
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
    Write-Host "`nG8 Host V4 封板门禁通过：两轮隔离结果一致。"
    Write-Host "封板证据：$runRoot"
}
catch {
    $overallStopwatch.Stop()
    Write-HostV4GateJson -Path $overallPath -Value ([ordered]@{
        schemaVersion = 1
        baseline = 'v3'
        sourceRevision = $revision
        sourceTree = $sourceTree
        passed = $false
        repeatabilityVerified = $false
        releaseEligible = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $true
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
    Write-Error "G8 V4 封板门禁失败；已保留证据：$runRoot；原因：$($_.Exception.Message)"
    throw
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        try {
            & dotnet build-server shutdown | Out-Host
            Remove-HostV4GateOwnedTree -Path $temporaryRoot -AllowedParent $temporaryParent `
                -Purpose 'G8 V4 临时根清理'
        }
        catch {
            # 临时清理失败只影响磁盘卫生，不得覆盖已经落盘的通过或失败结论。
            Write-Warning "G8 V4 临时目录清理失败，已保留 '$temporaryRoot'：$($_.Exception.Message)"
        }
    }
}
