[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\PluginRegistrationOwnership'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G4 结果目录不在仓库内：$resultRoot。"
}

# 本脚本只执行 V3 G4 的本地非发布门禁。它不读取或初始化 AIFLOW，不调用 Windows
# CI/Smoke、ReleaseAcceptance、发布门禁、签名、上传、标签或任何外部发布操作。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$suites = @(
    [pscustomobject]@{
        Name = 'G4-Host'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
        Filter = 'FullyQualifiedName~PluginRegistrationOwnershipTests|FullyQualifiedName~ExplicitContributionAndPluginRegistryTests|FullyQualifiedName~IdentityAndRegistryTests'
    },
    [pscustomobject]@{
        Name = 'G4-PluginIntegration'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        Filter = 'FullyQualifiedName~PluginContainerIsolationTests|FullyQualifiedName~PluginCompatibilityTests|FullyQualifiedName~CurrentManagedPluginLoadingTests'
    }
)

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Set-Utf8File {
    param([string]$Path, [string]$Content)
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

$suiteSummary = [ordered]@{}
$totalPassed = 0
Push-Location $repositoryRoot
try {
    foreach ($suite in $suites) {
        $suiteDirectory = Join-Path $resultRoot $suite.Name
        New-Item -ItemType Directory -Path $suiteDirectory | Out-Null
        $arguments = @(
            'test', $suite.Project,
            '-c', $Configuration,
            '-p:SkipPluginDeploy=true',
            '--filter', $suite.Filter,
            '--results-directory', $suiteDirectory,
            '--logger', "trx;LogFileName=$($suite.Name).trx",
            '--logger', 'console;verbosity=minimal'
        )
        if ($NoRestore) { $arguments += '--no-restore' }

        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$($suite.Name) 失败，退出码：$LASTEXITCODE。"
        }

        [xml]$trx = Get-Content -LiteralPath (Join-Path $suiteDirectory "$($suite.Name).trx")
        $counters = $trx.TestRun.ResultSummary.Counters
        Assert-True (
            [int]$counters.failed -eq 0 -and
            [int]$counters.notExecuted -eq 0 -and
            [int]$counters.executed -eq [int]$counters.passed) `
            "$($suite.Name) TRX 未全绿。"
        $passed = [int]$counters.passed
        $suiteSummary[$suite.Name] = $passed
        $totalPassed += $passed
    }

    $registrationSource = Get-Content -Raw `
        'Host\MyAvaloniaManagement\Business\Helpers\PluginRegistrationContext.cs'
    Assert-True (-not $registrationSource.Contains(
        'Services.AddScoped<TDocument>()', [StringComparison]::Ordinal)) `
        'G4 注册入口重新把 Document 根写入插件可修改集合。'
    Assert-True (-not $registrationSource.Contains(
        'Services.AddSingleton<TTool>()', [StringComparison]::Ordinal)) `
        'G4 注册入口重新把 Tool 根写入插件可修改集合。'

    $providerOwnerSource = Get-Content -Raw `
        'Host\MyAvaloniaManagement\Business\Helpers\PluginProviderOwner.cs'
    Assert-True ($providerOwnerSource.Contains(
        'var pluginServices = new ServiceCollection();', [StringComparison]::Ordinal)) `
        '插件 Configure 前没有建立空 ServiceCollection。'
    Assert-True ($providerOwnerSource.Contains(
        'PluginServiceCommitGuard.ValidateAndCommit', [StringComparison]::Ordinal)) `
        '插件 Provider 构建前没有执行 G4 Commit Guard。'

    $guardPath = 'Host\MyAvaloniaManagement\Business\Helpers\PluginServiceCommitGuard.cs'
    Assert-True (Test-Path -LiteralPath $guardPath -PathType Leaf) 'G4 Commit Guard 文件缺失。'
    $guardSource = Get-Content -Raw $guardPath
    foreach ($symbol in @(
        'IHostEventBus',
        'IPluginWindowInteraction',
        'IDocumentLifetime',
        'PluginHostServiceRegistrationForbidden',
        'PluginContributionServiceRegistrationForbidden')) {
        Assert-True ($guardSource.Contains($symbol, [StringComparison]::Ordinal)) `
            "G4 Commit Guard 缺少必需所有权事实：$symbol。"
    }

    # G4 只收紧行为，不改变 public C# 形状；API 文本必须继续保留注册入口，同时不得出现
    # Commit Guard 等 Host internal 实现名称。
    $uiApi = Get-Content -Raw `
        'Host\MyAvaloniaManagement.PluginSdk.UI\ApiCompatibility\v3\PublicAPI.Unshipped.txt'
    Assert-True ($uiApi.Contains(
        'MyAvaloniaManagement.PluginSdk.UI.IPluginRegistration.Services.get',
        [StringComparison]::Ordinal)) 'v3 UI API 缺少 IPluginRegistration.Services。'
    Assert-True (-not $uiApi.Contains(
        'PluginServiceCommitGuard', [StringComparison]::Ordinal)) `
        'Host internal Commit Guard 意外进入 public API。'

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        suites = $suiteSummary
        passed = $totalPassed
        failed = 0
        emptyPluginCollectionVerified = $true
        hostOwnedCommitVerified = $true
        contributionIdOwnershipVerified = $true
        publicApiChanged = $false
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    Set-Utf8File (Join-Path $resultRoot 'summary.json') `
        ($summary | ConvertTo-Json -Depth 4)
    Write-Host "G4 插件注册所有权专项门禁通过：$totalPassed 项。"
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
