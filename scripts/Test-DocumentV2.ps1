[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\DocumentV2'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G7 结果目录不在仓库内：$resultRoot。"
}

# G7 是开发阶段的非发布门禁。三个测试进程串行执行，避免共享编译输出、
# Avalonia Headless 资源和诊断文件相互干扰。本脚本永不调用 Windows CI、
# Windows Smoke、ReleaseAcceptance、发布包验收、上传或标签操作。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$suites = @(
    [pscustomobject]@{
        Name = 'G7-Unit'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
        Filter = 'FullyQualifiedName~DocumentEnvelopeV2Tests|FullyQualifiedName~DocumentPersistenceV2Tests|FullyQualifiedName~DocumentCloseV2Tests|FullyQualifiedName~HostDockAdapterTests'
    },
    [pscustomobject]@{
        Name = 'G7-Plugin'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        Filter = 'FullyQualifiedName~DocumentScopeManagerTests|FullyQualifiedName~VersionPolicyTests'
    },
    [pscustomobject]@{
        Name = 'G7-UI'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        Filter = 'FullyQualifiedName~HostDockAdapterUiTests|FullyQualifiedName~ApplicationAndWindowTests'
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

    # Document 生产路径只能保留 V2 Core SDK 契约。这里扫描符号而非命名空间全集，
    # 允许 layout-v1 与尚待迁移的业务插件继续存在于 G7 明确排除的阶段边界。
    $productionRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement'
    $documentPaths = @(
        (Join-Path $productionRoot 'Business\Documents'),
        (Join-Path $productionRoot 'Business\Docking'),
        (Join-Path $productionRoot 'Business\Helpers\DocumentScopeManager.cs'),
        (Join-Path $productionRoot 'Business\Workspace\WorkspaceSession.cs'),
        (Join-Path $productionRoot 'ViewModels\MainWindowViewModel.cs')
    )
    $forbiddenPattern = 'DocumentEnvelopeV1|DocumentContentSnapshot|ISavableDocument|IDocumentSaveState|DocumentLoadException|IDocumentScopeFactory|CreateLegacyDocument|Newtonsoft\.Json'
    & rg --quiet $forbiddenPattern @documentPaths -g '*.cs'
    if ($LASTEXITCODE -eq 0) {
        throw 'G7 Host Document 生产路径重新出现 V1 保存或 Legacy Scope 符号。'
    }
    if ($LASTEXITCODE -gt 1) {
        throw '无法执行 G7 生产结构扫描。'
    }

    $serializerPath = Join-Path $productionRoot `
        'Business\Documents\DocumentEnvelopeSerializer.cs'
    & rg --quiet 'CurrentSchemaVersion\s*=\s*2' $serializerPath
    if ($LASTEXITCODE -ne 0) {
        throw 'G7 Serializer 没有固定为唯一 schemaVersion 2。'
    }
    & rg --quiet 'Payload\s*\{[^}]*string|string\s+payload|JsonSerializer\.Serialize\([^)]*Payload' `
        $serializerPath
    if ($LASTEXITCODE -eq 0) {
        throw 'G7 Serializer 重新把 payload 表达为字符串或二次编码 JSON。'
    }
    if ($LASTEXITCODE -gt 1) {
        throw '无法执行 G7 payload 结构扫描。'
    }

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        suites = $suiteSummary
        passed = $totalPassed
        failed = 0
        windowsCi = $false
        windowsSmoke = $false
        releaseGate = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 4),
        [Text.UTF8Encoding]::new($false))
    Write-Host "G7 Document V2 专项门禁通过：$totalPassed 项。"
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
