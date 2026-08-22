[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\DeclarativeContributionCatalog'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G5 结果目录不在仓库内：$resultRoot。"
}

# G5 是开发阶段专项门禁：串行执行以避免共享构建输出和 Headless UI 资源互相干扰。
# 本脚本不调用 Windows CI、Smoke、发布验收、打包或发布脚本。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$suites = @(
    [pscustomobject]@{
        Name = 'G5-Unit'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
        Filter = 'FullyQualifiedName~ExplicitContributionAndPluginRegistryTests|FullyQualifiedName~IdentityAndRegistryTests'
    },
    [pscustomobject]@{
        Name = 'G5-Plugin'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        Filter = 'FullyQualifiedName~PluginContainerIsolationTests|FullyQualifiedName~CurrentManagedPluginLoadingTests'
    },
    [pscustomobject]@{
        Name = 'G5-UI'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        Filter = 'FullyQualifiedName~HostToolVisualTests|FullyQualifiedName~ApplicationAndWindowTests'
    }
)

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
        if ($NoRestore) {
            $arguments += '--no-restore'
        }

        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$($suite.Name) 失败，退出码：$LASTEXITCODE。"
        }

        $trxPath = Join-Path $suiteDirectory "$($suite.Name).trx"
        [xml]$trx = Get-Content -LiteralPath $trxPath
        $counters = $trx.TestRun.ResultSummary.Counters
        if ([int]$counters.failed -ne 0 -or
            [int]$counters.notExecuted -ne 0 -or
            [int]$counters.executed -ne [int]$counters.passed) {
            throw "$($suite.Name) TRX 未全绿。"
        }

        $passed = [int]$counters.passed
        $suiteSummary[$suite.Name] = $passed
        $totalPassed += $passed
    }

    # 结构门禁只扫描生产目录的声明、组合与查询路径。Legacy 阶段插件源码允许继续包含旧符号，
    # 但它们不能重新进入 G5 的生产 Registry 或 Host 消费者。
    $productionFiles = @(
        'Host\MyAvaloniaManagement\Business\Helpers\PluginRegistry.cs',
        'Host\MyAvaloniaManagement\Business\Helpers\PluginRegistryBuilder.cs',
        'Host\MyAvaloniaManagement\Business\Helpers\PluginProviderOwner.cs',
        'Host\MyAvaloniaManagement\Business\Helpers\PluginRegistrationContext.cs',
        'Host\MyAvaloniaManagement\Business\Helpers\ServiceCollectionExtensions.cs',
        'Host\MyAvaloniaManagement\Business\Workspace\WorkspaceSession.cs',
        'Host\MyAvaloniaManagement\ViewLocator.cs'
    ) | ForEach-Object { Join-Path $repositoryRoot $_ }
    $forbidden = @(
        'PluginStrategy',
        'GetMetadata(',
        'IPluginDocumentCreationIntentProvider',
        'AddView('
    )
    foreach ($symbol in $forbidden) {
        & rg --fixed-strings --quiet $symbol @productionFiles
        if ($LASTEXITCODE -eq 0) {
            throw "G5 生产目录仍引用已移除的并行注册符号：$symbol。"
        }
        if ($LASTEXITCODE -gt 1) {
            throw "无法扫描 G5 生产目录符号：$symbol。"
        }
    }

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        suites = $suiteSummary
        passed = $totalPassed
        failed = 0
        windowsCi = $false
        releaseGate = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 4),
        [Text.UTF8Encoding]::new($false))
    Write-Host "G5 声明式贡献目录专项门禁通过：$totalPassed 项。"
    # 最后一次成功的 rg“未找到”会留下退出码 1；脚本已经把该结果验证为预期，
    # 因此显式恢复成功码，避免调用方把已通过的专项门禁误判为失败。
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
