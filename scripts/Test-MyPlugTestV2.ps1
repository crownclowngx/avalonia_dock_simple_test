[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\MyPlugTestV2'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G9 结果目录不在仓库内：$resultRoot。"
}

# G9 是开发阶段的非发布迁移门禁。脚本只执行本地自动化测试、静态扫描和不可发布的确定性测试 ZIP；
# 不读取或初始化 AIFLOW，不调用 Windows CI、真实窗口 Smoke、ReleaseAcceptance、签名、上传、标签
# 或任何正式发布门禁。测试 ZIP 的 publishable 固定为 false。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$suites = @(
    [pscustomobject]@{
        Name = 'G9-Plugin'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        Filter = 'FullyQualifiedName~MyPlugTestV2MigrationTests|FullyQualifiedName~BatchHttpGetDocumentTests|FullyQualifiedName~ExcelGetUrlGeneratorTests|FullyQualifiedName~CurrentManagedPluginLoadingTests|FullyQualifiedName~PluginHostBoundaryTests|FullyQualifiedName~PluginSdkDependencyBoundaryTests|FullyQualifiedName~VersionPolicyTests'
    },
    [pscustomobject]@{
        Name = 'G9-HeadlessUI'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        Filter = 'FullyQualifiedName~MyPlugTestV2UiTests|FullyQualifiedName~ExcelGetUrlGeneratorViewTests|FullyQualifiedName~ApplicationAndWindowTests'
    },
    [pscustomobject]@{
        Name = 'G9-PluginSdk'
        Project = 'Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj'
        Filter = 'FullyQualifiedName~SdkBoundaryTests|FullyQualifiedName~DocumentAndDescriptorTests'
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
        '--filter', $Suite.Filter,
        '--results-directory', $suiteDirectory,
        '--logger', "trx;LogFileName=$($Suite.Name).trx",
        '--logger', 'console;verbosity=minimal'
    )
    if ($NoRestore) {
        $arguments += '--no-restore'
    }

    # PowerShell 会把函数体内未消费的标准输出也作为返回值。这里显式送到宿主，
    # 保证调用方只收到末尾的通过数量，而不是“控制台文本 + 数字”的 Object[]。
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
    if ($LASTEXITCODE -eq 0) {
        throw $Message
    }
    if ($LASTEXITCODE -gt 1) {
        throw "无法执行 G9 结构扫描：$Path。"
    }
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

    $pluginRoot = Join-Path $repositoryRoot 'Plugins\MyPlugTest\MyPlugTest'
    Assert-RgAbsent `
        'MyAvaloniaManagementCommon|LegacyPluginContracts|Dock\.Model|IDocumentCreationStrategy|IToolCreationStrategy|IDocumentScopeFactory|DocumentContentSnapshot|ISavableDocument|IDocumentSaveState|Newtonsoft\.Json|LegacyIds' `
        $pluginRoot `
        'G9 MyPlugTest 生产代码重新出现 Legacy、Dock、Strategy、旧保存或 Newtonsoft 契约。'

    $projectPath = Join-Path $pluginRoot 'MyPlugTest.csproj'
    foreach ($requiredReference in
        'MyAvaloniaManagement.PluginSdk.csproj',
        'MyAvaloniaManagement.PluginSdk.UI.csproj') {
        & rg --quiet ([Regex]::Escape($requiredReference)) $projectPath
        if ($LASTEXITCODE -ne 0) {
            throw "G9 MyPlugTest 缺少最终 SDK 引用：$requiredReference。"
        }
    }
    Assert-RgAbsent `
        'ManagedPluginUseV2EntryContract|ManagedPluginHostApi|ManagedPluginCommonContract' `
        $projectPath `
        'G13 后 MyPlugTest 项目不得恢复过渡入口开关或 Host/Common 双区间。'

    $firstPackageRoot = Join-Path $resultRoot 'package-first'
    $secondPackageRoot = Join-Path $resultRoot 'package-second'
    & (Join-Path $PSScriptRoot 'Build-ManagedPluginPackage.ps1') `
        -Project $projectPath -Configuration $Configuration -OutputDirectory $firstPackageRoot
    if ($LASTEXITCODE -ne 0) { throw 'G9 第一次隔离测试 ZIP 构建失败。' }
    & (Join-Path $PSScriptRoot 'Build-ManagedPluginPackage.ps1') `
        -Project $projectPath -Configuration $Configuration -OutputDirectory $secondPackageRoot
    if ($LASTEXITCODE -ne 0) { throw 'G9 第二次隔离测试 ZIP 构建失败。' }

    $firstSidecarPath = Join-Path $firstPackageRoot 'MyPlugTest-2.0.0-win-x64.manifest.json'
    $secondSidecarPath = Join-Path $secondPackageRoot 'MyPlugTest-2.0.0-win-x64.manifest.json'
    $firstSidecar = Get-Content -Raw -LiteralPath $firstSidecarPath | ConvertFrom-Json
    $secondSidecar = Get-Content -Raw -LiteralPath $secondSidecarPath | ConvertFrom-Json
    if ($firstSidecar.archive.sha256 -ne $secondSidecar.archive.sha256) {
        throw 'G9 两次隔离测试 ZIP 的归档摘要不一致。'
    }
    $firstFiles = @($firstSidecar.files | ForEach-Object {
        "$($_.path)|$($_.length)|$($_.sha256)"
    })
    $secondFiles = @($secondSidecar.files | ForEach-Object {
        "$($_.path)|$($_.length)|$($_.sha256)"
    })
    if (Compare-Object $firstFiles $secondFiles) {
        throw 'G9 两次隔离测试 ZIP 的文件事实不一致。'
    }
    $forbiddenPackageFiles = @($firstSidecar.files.path | Where-Object {
        $_ -match '(^|/)(?:MyAvaloniaManagement(?:Common|\.PluginSdk(?:\.UI)?)?|Avalonia(?:\.|$)|Dock\.|Newtonsoft\.Json|Microsoft\.Extensions\.).*\.dll$'
    })
    if ($forbiddenPackageFiles.Count -ne 0) {
        throw "G9 测试 ZIP 混入宿主共享程序集：$($forbiddenPackageFiles -join ', ')"
    }

    $packageLoadRoot = Join-Path $resultRoot 'package-load'
    Expand-Archive `
        -LiteralPath (Join-Path $firstPackageRoot 'MyPlugTest-2.0.0-win-x64.zip') `
        -DestinationPath $packageLoadRoot
    $previousPackageRoot = [Environment]::GetEnvironmentVariable('MYAVALONIA_G9_PACKAGE_ROOT')
    try {
        [Environment]::SetEnvironmentVariable(
            'MYAVALONIA_G9_PACKAGE_ROOT',
            (Join-Path $packageLoadRoot 'Controls'))
        $zipSuite = [pscustomobject]@{
            Name = 'G9-FinalZip'
            Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
            Filter = 'FullyQualifiedName~G9最终测试Zip通过真实发现组合并发布完整Registry'
        }
        $zipPassed = Invoke-TestSuite $zipSuite
        $suiteSummary[$zipSuite.Name] = $zipPassed
        $totalPassed += $zipPassed
    }
    finally {
        [Environment]::SetEnvironmentVariable('MYAVALONIA_G9_PACKAGE_ROOT', $previousPackageRoot)
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
    Write-Host "G9 MyPlugTest V2 专项门禁通过：$totalPassed 项；测试 ZIP $($firstSidecar.files.Count) 个文件。"
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
