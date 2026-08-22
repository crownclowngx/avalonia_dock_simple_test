[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\DaTangAccountingHelpPlugV2'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G10 结果目录不在仓库内：$resultRoot。"
}

# G10 是开发期非发布迁移门禁。脚本不读取或初始化 AIFLOW，不调用 Windows CI、真实窗口 Smoke、
# ReleaseAcceptance、发布总门禁、签名、上传或标签。两份 ZIP 只验证确定性与最终加载，不能发布。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$suites = @(
    [pscustomobject]@{
        Name = 'G10-Plugin'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        Filter = 'FullyQualifiedName~DaTangAccountingHelpPlugV2MigrationTests|FullyQualifiedName~DaTangDependencyCompatibilityTests|FullyQualifiedName~DaTangInvoiceInfoImportBusinessTests|FullyQualifiedName~CurrentManagedPluginLoadingTests|FullyQualifiedName~ManagedOnlyPluginLoadingTests|FullyQualifiedName~PluginHostBoundaryTests|FullyQualifiedName~PluginSdkDependencyBoundaryTests|FullyQualifiedName~VersionPolicyTests'
    },
    [pscustomobject]@{
        Name = 'G10-HeadlessUI'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        Filter = 'FullyQualifiedName~DaTangAccountingHelpPlugV2UiTests|FullyQualifiedName~ApplicationAndWindowTests'
    },
    [pscustomobject]@{
        Name = 'G10-PluginSdk'
        Project = 'Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj'
        Filter = 'FullyQualifiedName~SdkBoundaryTests|FullyQualifiedName~DocumentAndDescriptorTests'
    },
    [pscustomobject]@{
        Name = 'G10-Business'
        Project = 'Plugins\DaTangAccountingHelpPlug\DaTangAccountingHelpPlug.Tests\DaTangAccountingHelpPlug.Tests.csproj'
        Filter = ''
    }
)

function Invoke-TestSuite {
    param([Parameter(Mandatory)] $Suite)

    $suiteDirectory = Join-Path $resultRoot $Suite.Name
    New-Item -ItemType Directory -Path $suiteDirectory | Out-Null
    $arguments = @(
        'test', $Suite.Project,
        '-c', $Configuration,
        '-p:SkipPluginDeploy=true',
        '--results-directory', $suiteDirectory,
        '--logger', "trx;LogFileName=$($Suite.Name).trx",
        '--logger', 'console;verbosity=minimal'
    )
    if (-not [string]::IsNullOrWhiteSpace($Suite.Filter)) {
        $arguments += @('--filter', $Suite.Filter)
    }
    if ($NoRestore) { $arguments += '--no-restore' }

    & dotnet @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$($Suite.Name) 失败，退出码：$LASTEXITCODE。"
    }

    $trxPath = Join-Path $suiteDirectory "$($Suite.Name).trx"
    [xml]$trx = Get-Content -LiteralPath $trxPath
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.failed -ne 0 -or
        [int]$counters.notExecuted -ne 0 -or
        [int]$counters.executed -ne [int]$counters.passed) {
        throw "$($Suite.Name) TRX 未全绿。"
    }
    return [int]$counters.passed
}

function Assert-RgAbsent {
    param(
        [Parameter(Mandatory)] [string]$Pattern,
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Message
    )

    & rg --quiet $Pattern $Path -g '*.cs' -g '*.csproj'
    if ($LASTEXITCODE -eq 0) { throw $Message }
    if ($LASTEXITCODE -gt 1) { throw "无法执行 G10 结构扫描：$Path。" }
}

$suiteSummary = [ordered]@{}
$totalPassed = 0
Push-Location $repositoryRoot
try {
    foreach ($suite in $suites) {
        $passed = Invoke-TestSuite $suite
        $suiteSummary[$suite.Name] = $passed
        $totalPassed += $passed
    }

    $pluginRoot = Join-Path $repositoryRoot `
        'Plugins\DaTangAccountingHelpPlug\DaTangAccountingHelpPlug'
    Assert-RgAbsent `
        'MyAvaloniaManagementCommon|LegacyPluginContracts|Dock\.Model|IDocumentCreationStrategy|IDocumentScopeFactory|DocumentContentSnapshot|ISavableDocument|IDocumentSaveState|Newtonsoft\.Json|LegacyIds|Application\.Current|IClassicDesktopStyleApplicationLifetime|StorageProvider|\.Clipboard' `
        $pluginRoot `
        'G10 DaTang 生产代码重新出现 Legacy、Dock、旧保存或直接窗口访问。'

    $projectPath = Join-Path $pluginRoot 'DaTangAccountingHelpPlug.csproj'
    foreach ($requiredReference in
        'MyAvaloniaManagement.PluginSdk.csproj',
        'MyAvaloniaManagement.PluginSdk.UI.csproj') {
        & rg --quiet ([Regex]::Escape($requiredReference)) $projectPath
        if ($LASTEXITCODE -ne 0) { throw "G10 DaTang 缺少最终 SDK 引用：$requiredReference。" }
    }
    Assert-RgAbsent `
        'ManagedPluginUseV2EntryContract|ManagedPluginHostApi|ManagedPluginCommonContract' `
        $projectPath `
        'G13 后 DaTang 项目不得恢复过渡入口开关或 Host/Common 双区间。'

    $firstPackageRoot = Join-Path $resultRoot 'package-first'
    $secondPackageRoot = Join-Path $resultRoot 'package-second'
    & (Join-Path $PSScriptRoot 'Build-ManagedPluginPackage.ps1') `
        -Project $projectPath -Configuration $Configuration -OutputDirectory $firstPackageRoot
    if ($LASTEXITCODE -ne 0) { throw 'G10 第一次隔离测试 ZIP 构建失败。' }
    & (Join-Path $PSScriptRoot 'Build-ManagedPluginPackage.ps1') `
        -Project $projectPath -Configuration $Configuration -OutputDirectory $secondPackageRoot
    if ($LASTEXITCODE -ne 0) { throw 'G10 第二次隔离测试 ZIP 构建失败。' }

    $sidecars = @(Get-ChildItem -LiteralPath $firstPackageRoot `
        -Filter 'DaTangAccountingHelpPlug-*-win-x64.manifest.json')
    if ($sidecars.Count -ne 1) {
        throw "G10 预期唯一机器清单，实际为 $($sidecars.Count) 份。"
    }
    $baseName = $sidecars[0].Name -replace '\.manifest\.json$', ''
    $firstSidecar = Get-Content -Raw -LiteralPath `
        (Join-Path $firstPackageRoot "$baseName.manifest.json") | ConvertFrom-Json
    $secondSidecar = Get-Content -Raw -LiteralPath `
        (Join-Path $secondPackageRoot "$baseName.manifest.json") | ConvertFrom-Json
    if ($firstSidecar.archive.sha256 -ne $secondSidecar.archive.sha256) {
        throw 'G10 两次隔离测试 ZIP 的归档摘要不一致。'
    }
    $firstFiles = @($firstSidecar.files | ForEach-Object {
        "$($_.path)|$($_.length)|$($_.sha256)"
    })
    $secondFiles = @($secondSidecar.files | ForEach-Object {
        "$($_.path)|$($_.length)|$($_.sha256)"
    })
    if (Compare-Object $firstFiles $secondFiles) {
        throw 'G10 两次隔离测试 ZIP 的文件事实不一致。'
    }
    $forbiddenPackageFiles = @($firstSidecar.files.path | Where-Object {
        $_ -match '(^|/)(?:MyAvaloniaManagement(?:Common|\.PluginSdk(?:\.UI)?)?|Avalonia(?:\.|$)|Dock\.|Newtonsoft\.Json|Microsoft\.Extensions\.).*\.dll$'
    })
    if ($forbiddenPackageFiles.Count -ne 0) {
        throw "G10 测试 ZIP 混入宿主共享程序集：$($forbiddenPackageFiles -join ', ')"
    }

    $packageLoadRoot = Join-Path $resultRoot 'package-load'
    Expand-Archive -LiteralPath (Join-Path $firstPackageRoot "$baseName.zip") `
        -DestinationPath $packageLoadRoot
    $previousPackageRoot = [Environment]::GetEnvironmentVariable('MYAVALONIA_G10_PACKAGE_ROOT')
    try {
        [Environment]::SetEnvironmentVariable(
            'MYAVALONIA_G10_PACKAGE_ROOT',
            (Join-Path $packageLoadRoot 'Controls'))
        $zipSuite = [pscustomobject]@{
            Name = 'G10-FinalZip'
            Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
            Filter = 'FullyQualifiedName~G10最终测试Zip通过真实发现组合并发布两个Document'
        }
        $zipPassed = Invoke-TestSuite $zipSuite
        $suiteSummary[$zipSuite.Name] = $zipPassed
        $totalPassed += $zipPassed
    }
    finally {
        [Environment]::SetEnvironmentVariable(
            'MYAVALONIA_G10_PACKAGE_ROOT', $previousPackageRoot)
    }

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        suites = $suiteSummary
        passed = $totalPassed
        failed = 0
        archiveSha256 = $firstSidecar.archive.sha256
        packageFiles = $firstSidecar.files.Count
        deterministicBuilds = 2
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))
    Write-Host "G10 DaTang V2 专项门禁通过：$totalPassed 项；测试 ZIP $($firstSidecar.files.Count) 个文件。"
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
