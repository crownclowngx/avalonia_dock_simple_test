[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modulePath = Join-Path $PSScriptRoot 'HostV1ReleaseGate.Core.psm1'
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
        if ($LASTEXITCODE -ne 0) {
            throw "$Name 失败，退出码 $LASTEXITCODE。"
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-PowerShellChecked {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$ScriptPath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)] [string]$WorkingDirectory
    )

    Invoke-NativeChecked -Name $Name -FilePath 'pwsh' `
        -Arguments (@('-NoLogo', '-NoProfile', '-File', $ScriptPath) + $Arguments) `
        -WorkingDirectory $WorkingDirectory
}

function Copy-EvidenceDirectory {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { return }
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

    # 只复制 G14 承诺的审计材料，不把 bin/obj、临时 NuGet 缓存或 Smoke 发布目录
    # 冒充交付证据。目标目录属于本轮唯一 artifacts 子目录，可以安全覆盖失败前的局部副本。
    Copy-EvidenceDirectory `
        (Join-Path $CloneRoot 'artifacts\test-results\MyAvaloniaManagement') `
        (Join-Path $PassRoot 'MyAvaloniaManagement')
    Copy-EvidenceDirectory `
        (Join-Path $CloneRoot 'artifacts\test-results\ManagedPluginPackages') `
        (Join-Path $PassRoot 'ManagedPluginPackages')
    Copy-EvidenceDirectory `
        (Join-Path $CloneRoot 'artifacts\test-results\WindowsSmoke') `
        (Join-Path $PassRoot 'WindowsSmoke')
}

function Get-ApiEntryCount {
    param([Parameter(Mandatory)] [string]$Path)

    $lines = @(Get-Content -LiteralPath $Path)
    if ($lines.Count -eq 0 -or $lines[0] -cne '#nullable enable') {
        throw "API 基线缺少 #nullable enable 头：$Path"
    }
    return @($lines | Select-Object -Skip 1).Count
}

function Add-PluginManifestEvidence {
    param(
        [Parameter(Mandatory)] $PackageSummary,
        [Parameter(Mandatory)] [string]$PackageEvidenceRoot
    )

    # G12 聚合摘要已经记录 ZIP 摘要，但外置 manifest 是独立交付证据，不能只检查“文件存在”。
    # 这里把 manifest 自身的长度与 SHA-256 加入 G14 语义摘要；这样即使 ZIP 不变，清单内容被
    # 改写、截断或替换，两轮比较和最终证据复核仍会明确阻断。
    foreach ($plugin in @($PackageSummary.plugins)) {
        $archiveName = [string]$plugin.archive.file
        $manifestName = [IO.Path]::GetFileNameWithoutExtension($archiveName) + '.manifest.json'
        $manifestPath = Join-Path $PackageEvidenceRoot $manifestName
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "G14 插件 $($plugin.pluginId) 缺少外置清单：$manifestName"
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

    # PowerShell 变量名不区分大小写，不能使用 $host；它会撞上内置只读变量 $Host。
    $hostSummary = Get-Content -Raw (Join-Path $PassRoot 'MyAvaloniaManagement\summary.json') | ConvertFrom-Json
    $packageEvidenceRoot = Join-Path $PassRoot 'ManagedPluginPackages'
    $packages = Get-Content -Raw (Join-Path $packageEvidenceRoot 'summary.json') | ConvertFrom-Json
    $packages = Add-PluginManifestEvidence $packages $packageEvidenceRoot
    $smoke = Get-Content -Raw (Join-Path $PassRoot 'WindowsSmoke\summary.json') | ConvertFrom-Json
    [xml]$versions = Get-Content -Raw (Join-Path $CloneRoot 'Directory.Version.props')
    $baseline = [string]$versions.Project.PropertyGroup.MyAvaloniaPluginSdkApiBaseline
    $sdkPackageVersion = [string]$versions.Project.PropertyGroup.MyAvaloniaPluginSdkVersion
    $baselineRoot = Join-Path $CloneRoot "Host\MyAvaloniaManagementCommon\ApiCompatibility\$baseline"

    # 从各叶子脚本的机器结果重新组装稳定事实。generatedAt、耗时和绝对路径仍保留在
    # 顶层供排障，但不会进入两轮语义比较。
    return [ordered]@{
        schemaVersion = 1
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
        stages = @($Stages)
        host = [ordered]@{
            suites = $hostSummary.suites
            passed = $hostSummary.passed
            lineCoverage = $hostSummary.lineCoverage
            branchCoverage = $hostSummary.branchCoverage
        }
        sdkPackage = [ordered]@{
            passed = $true
            version = $sdkPackageVersion
        }
        sdkApi = [ordered]@{
            passed = $true
            baseline = $baseline
            shipped = Get-ApiEntryCount (Join-Path $baselineRoot 'PublicAPI.Shipped.txt')
            unshipped = Get-ApiEntryCount (Join-Path $baselineRoot 'PublicAPI.Unshipped.txt')
        }
        managedPlugins = $packages
        windowsSmoke = [ordered]@{
            passed = $smoke.passed
            exitCode = $smoke.exitCode
            layoutSaved = $smoke.layoutSaved
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
        # 物理构建产物仍在各轮 TEMP；这里只提供两轮共同的稳定 Junction 根，消除 PDB/CodeView
        # 中的路径差异。入口最终统一清理 temporaryRoot，不会把该路径留在用户目录。
        MYAVALONIA_MANAGED_PLUGIN_STABLE_ROOT =
            (Join-Path (Split-Path -Parent $RuntimeRoot) 'stable-managed-plugin-build')
        DOTNET_NOLOGO = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        # 两轮都禁止复用进程外 MSBuild 节点，避免某轮把另一轮隔离 NuGet 缓存中的 DLL
        # 持有到阶段结束以后。叶子脚本仍保留有限清理重试，处理杀毒或索引器的短暂占用。
        MSBUILDDISABLENODEREUSE = '1'
        DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    }
    foreach ($pathKey in @(
            'DOTNET_CLI_HOME',
            'NUGET_PACKAGES',
            'NUGET_HTTP_CACHE_PATH',
            'TEMP',
            'MYAVALONIA_DATA_DIRECTORY',
            'MYAVALONIA_MANAGED_PLUGIN_STABLE_ROOT')) {
        New-Item -ItemType Directory -Path $values[$pathKey] -Force | Out-Null
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
        Write-Host "[G14] 第 $PassNumber 轮隔离根：$CloneRoot"
        Write-Host "[G14] 源提交：$Revision；源树：$SourceTree"
        & dotnet --info | Out-Host

        $scripts = Join-Path $CloneRoot 'scripts'
        $solution = Join-Path $CloneRoot 'MyAvaloniaManagement.sln'
        $stages = @(
            @{ Name = 'release-gate-core-unit-tests'; Action = {
                Invoke-PowerShellChecked 'G14 核心单元测试' (Join-Path $scripts 'Test-HostV1ReleaseGateCore.ps1') @() $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'locked-restore'; Action = {
                Invoke-NativeChecked '解决方案锁定还原' 'dotnet' @(
                    'restore', $solution, '--locked-mode', '-p:SkipPluginDeploy=true', '--nologo') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'release-build'; Action = {
                Invoke-NativeChecked '解决方案 Release 零警告构建' 'dotnet' @(
                    'build', $solution, '-c', 'Release', '--no-restore', '--nologo',
                    '-warnaserror', '-p:SkipPluginDeploy=true', '-p:ContinuousIntegrationBuild=true') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'host-unit-ui-plugin-tests'; Action = {
                Invoke-PowerShellChecked '宿主 Unit/UI/Plugin 门禁' `
                    (Join-Path $scripts 'Invoke-MyAvaloniaManagementTests.ps1') `
                    @('-Configuration', 'Release', '-NoRestore') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'plugin-sdk-package'; Action = {
                Invoke-PowerShellChecked 'Plugin SDK 包消费门禁' `
                    (Join-Path $scripts 'Test-PluginSdkPackage.ps1') `
                    @('-Configuration', 'Release') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'plugin-sdk-api-compatibility'; Action = {
                Invoke-PowerShellChecked 'Plugin SDK API 兼容门禁' `
                    (Join-Path $scripts 'Test-PluginSdkCompatibility.ps1') `
                    @('-Baseline', 'v1', '-Configuration', 'Release') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'managed-plugin-package-matrix'; Action = {
                Invoke-PowerShellChecked 'Managed Plugin 包矩阵' `
                    (Join-Path $scripts 'Test-ManagedPluginPackages.ps1') `
                    @('-Configuration', 'Release') $CloneRoot
            }.GetNewClosure() },
            @{ Name = 'windows-real-window-smoke'; Action = {
                Invoke-PowerShellChecked 'Windows 真实窗口 Smoke' `
                    (Join-Path $scripts 'Invoke-MyAvaloniaManagementWindowsSmoke.ps1') `
                    @('-Configuration', 'Release', '-NoRestore') $CloneRoot
            }.GetNewClosure() }
        )
        $stageResults = @(Invoke-HostV1GateStageSequence -Stages $stages -StatePath $statePath)
        $stopwatch.Stop()
        Stop-Transcript | Out-Null
        $transcriptStarted = $false

        Copy-PassEvidence -CloneRoot $CloneRoot -PassRoot $PassRoot
        $summary = New-PassSummary $CloneRoot $PassRoot $Revision $SourceTree $SdkVersion $stageResults $stopwatch
        Write-HostV1GateJson -Path $summaryPath -Value $summary
        Assert-HostV1GateArtifacts -PassRoot $PassRoot -Summary $summary
        return $summary
    }
    catch {
        $stopwatch.Stop()
        if ($transcriptStarted) {
            try { Stop-Transcript | Out-Null } catch { }
        }
        Copy-PassEvidence -CloneRoot $CloneRoot -PassRoot $PassRoot
        $stages = if (Test-Path -LiteralPath $statePath) {
            @(Get-Content -Raw $statePath | ConvertFrom-Json)
        }
        else { @() }
        $failure = [ordered]@{
            schemaVersion = 1
            sourceRevision = $Revision
            sourceTree = $SourceTree
            passed = $false
            generatedAtUtc = [DateTime]::UtcNow.ToString('O')
            durationMilliseconds = $stopwatch.ElapsedMilliseconds
            evidenceRoot = $PassRoot
            stages = $stages
            error = $_.Exception.Message
        }
        Write-HostV1GateJson -Path $summaryPath -Value $failure
        throw
    }
    finally {
        Restore-PassEnvironment $previousEnvironment
    }
}

if ($env:OS -ne 'Windows_NT' -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'G14 正式发布门禁只支持 Windows x64。'
}
if ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
    throw "G14 要求 PowerShell 7 或更高版本，当前为 $($PSVersionTable.PSVersion)。"
}
foreach ($command in @('git', 'dotnet', 'pwsh')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "G14 缺少必需命令：$command"
    }
}

Push-Location $repositoryRoot
try {
    $dirty = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw '无法读取 Git 工作区状态。' }
    if ($dirty.Count -ne 0) {
        throw "G14 正式门禁只接受干净提交；请先审阅并提交以下变化：`n$($dirty -join [Environment]::NewLine)"
    }

    $revision = (& git rev-parse HEAD).Trim()
    $shortRevision = (& git rev-parse --short=12 HEAD).Trim()
    $sourceTree = (& git rev-parse "$revision`^{tree}").Trim()
    if ($LASTEXITCODE -ne 0) { throw '无法解析当前提交及源树。' }

    $expectedSdk = [string](Get-Content -Raw 'global.json' | ConvertFrom-Json).sdk.version
    $actualSdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualSdk -cne $expectedSdk) {
        throw "G14 要求 global.json 指定的 .NET SDK $expectedSdk，当前解析为 $actualSdk。"
    }
}
finally {
    Pop-Location
}

$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$runName = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + $shortRevision
$runRoot = Join-Path $artifactRoot "release-gate\$runName"
Assert-HostV1GateChildPath -Candidate $runRoot -Parent $artifactRoot -Purpose 'G14 发布证据目录'
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryParent ('MyAvaloniaHostV1ReleaseGate-' + [Guid]::NewGuid().ToString('N'))
Assert-HostV1GateChildPath -Candidate $temporaryRoot -Parent $temporaryParent -Purpose 'G14 临时根'
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

    Assert-HostV1GateEvidenceEqual -First $passSummaries[0] -Second $passSummaries[1]
    $overallStopwatch.Stop()
    $overall = [ordered]@{
        schemaVersion = 1
        sourceRevision = $revision
        sourceTree = $sourceTree
        passed = $true
        repeatabilityVerified = $true
        releaseEligible = $true
        passCount = 2
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        durationMilliseconds = $overallStopwatch.ElapsedMilliseconds
        evidenceRoot = $runRoot
        passes = @($passSummaries | ForEach-Object { $_.evidenceRoot })
    }
    Write-HostV1GateJson -Path $overallPath -Value $overall
    Write-Host "`nG14 Windows 本地发布门禁通过：两轮隔离结果一致。"
    Write-Host "发布证据：$runRoot"
}
catch {
    $overallStopwatch.Stop()
    $overall = [ordered]@{
        schemaVersion = 1
        sourceRevision = $revision
        sourceTree = $sourceTree
        passed = $false
        repeatabilityVerified = $false
        releaseEligible = $false
        passCount = $passSummaries.Count
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        durationMilliseconds = $overallStopwatch.ElapsedMilliseconds
        evidenceRoot = $runRoot
        error = $_.Exception.Message
    }
    Write-HostV1GateJson -Path $overallPath -Value $overall
    Write-Error "G14 Windows 本地发布门禁失败；已保留证据：$runRoot；原因：$($_.Exception.Message)"
    throw
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        try {
            # MSBuild 节点会加载 NuGet 缓存中的 Avalonia.Build.Tasks.dll，并可能在门禁结束后继续
            # 常驻。先关闭本用户的构建服务器，才能在 Windows 上可靠删除两轮隔离缓存。
            & dotnet build-server shutdown | Out-Host
            Remove-HostV1GateOwnedTree `
                -Path $temporaryRoot `
                -AllowedParent $temporaryParent `
                -Purpose 'G14 临时根清理'
        }
        catch {
            # 临时目录清理属于卫生工作，不应覆盖已经写入的门禁结论；保留明确警告，便于维护者
            # 手工检查占用进程。发布资格始终由前面的阶段结果和两轮一致性决定。
            Write-Warning "G14 临时目录清理失败，已保留目录 '$temporaryRoot'：$($_.Exception.Message)"
        }
    }
}
