[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repositoryRoot `
    'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
$resultRoot = Join-Path $repositoryRoot 'artifacts\test-results\PluginContainerIsolation'
$trxPath = Join-Path $resultRoot 'G4-PluginContainerIsolation.trx'

# 专项门禁只覆盖 G4 的容器所有权，不启动窗口、不打包、不发布，也不调用 Windows CI。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$arguments = @(
    'test',
    $project,
    '-c', $Configuration,
    '-p:SkipPluginDeploy=true',
    '--filter', 'FullyQualifiedName~PluginContainerIsolationTests',
    '--results-directory', $resultRoot,
    '--logger', 'trx;LogFileName=G4-PluginContainerIsolation.trx',
    '--logger', 'console;verbosity=minimal'
)
if ($NoRestore) {
    $arguments += '--no-restore'
}

Push-Location $repositoryRoot
try {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "G4 插件容器专项测试失败，退出码：$LASTEXITCODE。"
    }

    [xml]$trx = Get-Content -LiteralPath $trxPath
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.failed -ne 0 -or
        [int]$counters.notExecuted -ne 0 -or
        [int]$counters.executed -ne [int]$counters.passed) {
        throw (
            "G4 TRX 未全绿：passed=$($counters.passed)，" +
            "failed=$($counters.failed)，notExecuted=$($counters.notExecuted)。")
    }

    # 源码结构门禁证明旧的保护事务已经真正删除，而不是留在生产代码中等待误用。
    $productionRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement'
    $forbidden = @(
        'HostServiceDescriptorPolicy',
        'PluginServiceRegistrationTransaction',
        'PluginServiceRegistrationViolation',
        'CONTRIBUTION_REGISTRATION_BYPASS'
    )
    foreach ($symbol in $forbidden) {
        & rg --fixed-strings --glob '*.cs' --quiet $symbol $productionRoot
        if ($LASTEXITCODE -eq 0) {
            throw "G4 已删除的生产符号仍然存在：$symbol。"
        }
        if ($LASTEXITCODE -gt 1) {
            throw "无法扫描 G4 已删除符号：$symbol。"
        }
    }

    $requiredFiles = @(
        'Host\MyAvaloniaManagement\Business\Helpers\PluginProviderOwner.cs',
        'Host\MyAvaloniaManagement\Business\Helpers\DocumentScopeRegistry.cs'
    )
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Leaf)) {
            throw "G4 所有权实现文件缺失：$relativePath。"
        }
    }

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        windowsCi = $false
        releaseGate = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json),
        [Text.UTF8Encoding]::new($false))
    Write-Host "G4 插件独立容器专项门禁通过：$($counters.passed) 项。"
}
finally {
    Pop-Location
}
