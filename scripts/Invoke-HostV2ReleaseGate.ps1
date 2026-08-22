[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modulePath = Join-Path $PSScriptRoot 'HostV2ReleaseGate.Core.psm1'
Import-Module $modulePath -Force

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
    Assert-HostV2GateChildPath -Candidate $Destination -Parent $AllowedParent -Purpose 'G14 V2 证据复制'
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

    # 只收集 G14 声明的审计材料。bin/obj、临时 NuGet 缓存和 Smoke publish 目录都可重建，
    # 不能因为体积大就冒充发布证据；最终 ZIP、manifest、TRX、覆盖率和摘要则必须保留。
    Copy-EvidenceDirectory `
        (Join-Path $CloneRoot 'artifacts\test-results\HostV2ProductionSurface') `
        (Join-Path $PassRoot 'HostV2ProductionSurface') $PassRoot
    Copy-EvidenceDirectory `
        (Join-Path $CloneRoot 'artifacts\test-results\MyAvaloniaManagement') `
        (Join-Path $PassRoot 'MyAvaloniaManagement') $PassRoot
    Copy-EvidenceDirectory `
        (Join-Path $CloneRoot 'artifacts\test-results\Documentation') `
        (Join-Path $PassRoot 'Documentation') $PassRoot
    Copy-EvidenceDirectory `
        (Join-Path $CloneRoot 'artifacts\test-results\HostV2ProductionSurface\ManagedPluginPackages') `
        (Join-Path $PassRoot 'ManagedPluginPackages') $PassRoot
    Copy-EvidenceDirectory `
        (Join-Path $CloneRoot 'artifacts\test-results\WindowsSmoke') `
        (Join-Path $PassRoot 'WindowsSmoke') $PassRoot

    # 四个独立业务测试项目由 G13 聚合入口写在各自子目录。复制到统一目录只是为了让
    # 审计者一眼看到四份 TRX，不改变叶子脚本的输出所有权。
    $additionalRoot = Join-Path $PassRoot 'AdditionalSuites'
    foreach ($suite in 'PluginSdk', 'DaTang', 'MySmallTools', 'BiliDownloader') {
        Copy-EvidenceDirectory `
            (Join-Path $CloneRoot "artifacts\test-results\HostV2ProductionSurface\$suite") `
            (Join-Path $additionalRoot $suite) $PassRoot
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

    # ZIP 与外置 manifest 都是交付物。矩阵摘要已经记录 ZIP，这里补充 manifest 自身的
    # 长度和 SHA-256，使两轮比较能够发现清单被单独替换、截断或重新编码。
    foreach ($plugin in @($PackageSummary.plugins)) {
        $archiveName = [string]$plugin.archive.file
        $manifestName = [IO.Path]::GetFileNameWithoutExtension($archiveName) + '.manifest.json'
        $manifestPath = Join-Path $PackageEvidenceRoot $manifestName
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "G14 V2 插件 $($plugin.pluginId) 缺少外置清单：$manifestName"
        }
        $manifestFile = Get-Item -LiteralPath $manifestPath
        $plugin | Add-Member -NotePropertyName manifest -NotePropertyValue ([ordered]@{
            file = $manifestName
            length = $manifestFile.Length
            sha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
        }) -Force
    }
    return $PackageSummary
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
        (Join-Path $PassRoot 'HostV2ProductionSurface\summary.json') | ConvertFrom-Json
    $documentation = Get-Content -Raw `
        (Join-Path $PassRoot 'Documentation\summary.json') | ConvertFrom-Json
    $packageRoot = Join-Path $PassRoot 'ManagedPluginPackages'
    $packages = Get-Content -Raw (Join-Path $packageRoot 'summary.json') | ConvertFrom-Json
    $packages = Add-PluginManifestEvidence $packages $packageRoot
    $smoke = Get-Content -Raw (Join-Path $PassRoot 'WindowsSmoke\summary.json') | ConvertFrom-Json
    [xml]$versions = Get-Content -Raw (Join-Path $CloneRoot 'Directory.Version.props')
    $baseline = [string]$versions.Project.PropertyGroup.MyAvaloniaPluginSdkApiBaseline
    $sdkPackageVersion = [string]$versions.Project.PropertyGroup.MyAvaloniaPluginSdkVersion

    # 只挑选稳定事实进入语义摘要。叶子摘要中的 generatedAtUtc、绝对目录和耗时仍保留在
    # 原始证据中供排障，但不会让两个等价构建互相否定。
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
        # portable PDB 和 Avalonia XAML 后处理会嵌入路径。两轮共享同一稳定逻辑路径，
        # 物理文件仍位于各自隔离 TEMP，由插件构建脚本用 Junction 与 PathMap 归一化。
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
        Write-Host "[G14 V2] 第 $PassNumber 轮隔离根：$CloneRoot"
        Write-Host "[G14 V2] 源提交：$Revision；源树：$SourceTree"
        # dotnet --info 也必须在本轮克隆内执行，否则它可能向上找到
        # 调用者工作区的 global.json，导致 transcript 展示错误的配置来源。
        Invoke-NativeChecked '.NET 环境快照' 'dotnet' @('--info') $CloneRoot

        $scripts = Join-Path $CloneRoot 'scripts'
        $solution = Join-Path $CloneRoot 'MyAvaloniaManagement.sln'
        # GetNewClosure 固定当前轮路径。闭包拥有独立 SessionState，所以显式捕获调用器，
        # 不把 G14 私有进程启动逻辑扩展为 Core module 的公共命令。
        $invokeNativeChecked = ${function:Invoke-NativeChecked}
        $invokePowerShellChecked = {
            param([string]$Name, [string]$ScriptPath, [string[]]$Arguments, [string]$WorkingDirectory)
            & $invokeNativeChecked $Name 'pwsh' `
                (@('-NoLogo', '-NoProfile', '-File', $ScriptPath) + $Arguments) $WorkingDirectory
        }.GetNewClosure()
        $stages = @(
            @{ Name = 'release-gate-core-unit-tests'; Action = {
                & $invokePowerShellChecked 'G14 V2 核心单元测试' `
                    (Join-Path $scripts 'Test-HostV2ReleaseGateCore.ps1') @() $CloneRoot
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
            @{ Name = 'host-v2-production-surface'; Action = {
                & $invokePowerShellChecked 'V2 生产面全量门禁' `
                    (Join-Path $scripts 'Test-HostV2ProductionSurface.ps1') `
                    @('-Configuration', 'Release', '-NoRestore') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'plugin-sdk-v2-api-compatibility'; Action = {
                & $invokePowerShellChecked 'Plugin SDK V2 API 兼容门禁' `
                    (Join-Path $scripts 'Test-PluginSdkCompatibility.ps1') `
                    @('-Baseline', 'v2', '-Configuration', 'Release') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'windows-real-window-v2-smoke'; Action = {
                & $invokePowerShellChecked 'Windows 真实窗口 V2 Smoke' `
                    (Join-Path $scripts 'Invoke-MyAvaloniaManagementWindowsSmoke.ps1') `
                    @('-Configuration', 'Release', '-NoRestore') $CloneRoot
            }.GetNewClosure() }
        )
        $stageResults = @(Invoke-HostV2GateStageSequence -Stages $stages -StatePath $statePath)
        $stopwatch.Stop()
        Stop-Transcript | Out-Null
        $transcriptStarted = $false

        Copy-PassEvidence $CloneRoot $PassRoot
        $summary = New-PassSummary $CloneRoot $PassRoot $Revision $SourceTree $SdkVersion $stageResults $stopwatch
        Write-HostV2GateJson -Path $summaryPath -Value $summary
        Assert-HostV2GateArtifacts -PassRoot $PassRoot -Summary $summary
        return $summary
    }
    catch {
        $stopwatch.Stop()
        if ($transcriptStarted) { try { Stop-Transcript | Out-Null } catch { } }
        Copy-PassEvidence $CloneRoot $PassRoot
        $stageResults = if (Test-Path -LiteralPath $statePath) {
            @(Get-Content -Raw $statePath | ConvertFrom-Json)
        } else { @() }
        Write-HostV2GateJson -Path $summaryPath -Value ([ordered]@{
            schemaVersion = 1
            baseline = 'v2'
            sourceRevision = $Revision
            sourceTree = $SourceTree
            passed = $false
            aiflow = $false
            publishable = $false
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
    throw 'G14 V2 正式发布门禁只支持 Windows x64。'
}
if ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
    throw "G14 V2 要求 PowerShell 7 或更高版本，当前为 $($PSVersionTable.PSVersion)。"
}
foreach ($command in @('git', 'dotnet', 'pwsh')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "G14 V2 缺少必需命令：$command"
    }
}

Push-Location $repositoryRoot
try {
    $dirty = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw '无法读取 Git 工作区状态。' }
    if ($dirty.Count -ne 0) {
        throw "G14 V2 正式门禁只接受干净提交；请先审阅以下变化：`n$($dirty -join [Environment]::NewLine)"
    }
    $revision = (& git rev-parse HEAD).Trim()
    $shortRevision = (& git rev-parse --short=12 HEAD).Trim()
    $sourceTree = (& git rev-parse "$revision`^{tree}").Trim()
    if ($LASTEXITCODE -ne 0) { throw '无法解析当前提交及源树。' }
    $expectedSdk = [string](Get-Content -Raw 'global.json' | ConvertFrom-Json).sdk.version
    $actualSdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualSdk -cne $expectedSdk) {
        throw "G14 V2 要求 .NET SDK $expectedSdk，当前解析为 $actualSdk。"
    }
}
finally { Pop-Location }

$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$runName = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + $shortRevision
$runRoot = Join-Path $artifactRoot "release-gate\v2\$runName"
Assert-HostV2GateChildPath -Candidate $runRoot -Parent $artifactRoot -Purpose 'G14 V2 发布证据目录'
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
# Windows 的部分 MSBuild 目标仍会在 260 字符边界处把存在的 NuGet DLL 误判为
# “路径不存在”。使用短且随机的自有根目录；隔离性由随机名和两轮子目录保证，
# 不依赖超长可读名称。
$temporaryRoot = Join-Path $temporaryParent (
    'MAV2G-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
Assert-HostV2GateChildPath -Candidate $temporaryRoot -Parent $temporaryParent -Purpose 'G14 V2 临时根'
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

    Assert-HostV2GateEvidenceEqual -First $passSummaries[0] -Second $passSummaries[1]
    $overallStopwatch.Stop()
    Write-HostV2GateJson -Path $overallPath -Value ([ordered]@{
        schemaVersion = 1
        baseline = 'v2'
        sourceRevision = $revision
        sourceTree = $sourceTree
        passed = $true
        repeatabilityVerified = $true
        releaseEligible = $true
        publishable = $true
        aiflow = $false
        passCount = 2
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        durationMilliseconds = $overallStopwatch.ElapsedMilliseconds
        evidenceRoot = $runRoot
        passes = @($passSummaries | ForEach-Object { $_.evidenceRoot })
    })
    Write-Host "`nG14 Managed Plugin V2 发布门禁通过：两轮隔离结果一致。"
    Write-Host "发布证据：$runRoot"
}
catch {
    $overallStopwatch.Stop()
    Write-HostV2GateJson -Path $overallPath -Value ([ordered]@{
        schemaVersion = 1
        baseline = 'v2'
        sourceRevision = $revision
        sourceTree = $sourceTree
        passed = $false
        repeatabilityVerified = $false
        releaseEligible = $false
        publishable = $false
        aiflow = $false
        passCount = $passSummaries.Count
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        durationMilliseconds = $overallStopwatch.ElapsedMilliseconds
        evidenceRoot = $runRoot
        error = $_.Exception.Message
    })
    Write-Error "G14 V2 发布门禁失败；已保留证据：$runRoot；原因：$($_.Exception.Message)"
    throw
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        try {
            & dotnet build-server shutdown | Out-Host
            Remove-HostV2GateOwnedTree -Path $temporaryRoot -AllowedParent $temporaryParent `
                -Purpose 'G14 V2 临时根清理'
        }
        catch {
            # 临时清理失败只影响磁盘卫生，不得覆盖已经落盘的发布结论。
            Write-Warning "G14 V2 临时目录清理失败，已保留 '$temporaryRoot'：$($_.Exception.Message)"
        }
    }
}
