[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\HostDockAdapter'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G6 结果目录不在仓库内：$resultRoot。"
}

# G6 是开发阶段的非发布专项门禁。三组测试必须串行运行，避免共享构建输出、
# Avalonia Headless 资源和诊断文件相互干扰。本脚本明确不调用 Windows CI、
# Windows Smoke、ReleaseAcceptance、发布包、上传或打标签流程。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$suites = @(
    [pscustomobject]@{
        Name = 'G6-Unit'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
        Filter = 'FullyQualifiedName~HostDockAdapterTests|FullyQualifiedName~ExplicitContributionAndPluginRegistryTests'
    },
    [pscustomobject]@{
        Name = 'G6-Plugin'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        Filter = 'FullyQualifiedName~PluginContainerIsolationTests|FullyQualifiedName~DocumentScopeManagerTests|FullyQualifiedName~DockFourWayLayoutTests|FullyQualifiedName~DockFloatingDisabledTests'
    },
    [pscustomobject]@{
        Name = 'G6-UI'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        Filter = 'FullyQualifiedName~HostDockAdapterUiTests|FullyQualifiedName~ApplicationAndWindowTests|FullyQualifiedName~HostToolVisualTests|FullyQualifiedName~DocumentControlRecyclingTests'
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

    # 普通模型不能重新继承 Dock 类型。ToolManagementViewModel 仍需读取布局协调器提供的
    # Dock 状态以呈现管理列表，但它本身不再是 Dock Tool，也不拥有 Dock 生命周期。
    $ordinaryModels = @(
        'Host\MyAvaloniaManagement\ViewModels\Welcome\WelcomeViewModel.cs',
        'Host\MyAvaloniaManagement\ViewModels\Tools\FileSystemTreeViewModel.cs',
        'Host\MyAvaloniaManagement\ViewModels\Tools\PlugGroupMenuViewModel.cs',
        'Host\MyAvaloniaManagement\ViewModels\Tools\PluginStatusViewModel.cs',
        'Host\MyAvaloniaManagement\Models\Tools\ToolWorkspaceState.cs'
    ) | ForEach-Object { Join-Path $repositoryRoot $_ }
    & rg --quiet 'class\s+\w+[^\r\n{]*:\s*(Document|Tool)\b' @ordinaryModels
    if ($LASTEXITCODE -eq 0) {
        throw 'G6 普通 Host 模型重新继承了 Dock 类型。'
    }
    if ($LASTEXITCODE -gt 1) {
        throw '无法扫描 G6 普通 Host 模型。'
    }

    # Host 生产目录只有两个内部 sealed Adapter 可以继承 Dock Document/Tool。
    $productionRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement'
    $inheritanceMatches = @(& rg -l `
        'class\s+\w+[^\r\n{]*:\s*(?:[\w<>,?]+\s*,\s*)*(Document|Tool)\b' `
        $productionRoot -g '*.cs')
    if ($LASTEXITCODE -gt 1) {
        throw '无法执行 G6 Dock 继承面扫描。'
    }
    $allowedInheritanceFiles = @(
        [IO.Path]::GetFullPath((Join-Path $productionRoot `
            'Business\Docking\ManagedDocumentDockable.cs')),
        [IO.Path]::GetFullPath((Join-Path $productionRoot `
            'Business\Docking\ManagedToolDockable.cs'))
    )
    $unexpectedInheritance = $inheritanceMatches |
        ForEach-Object { [IO.Path]::GetFullPath($_) } |
        Where-Object { $_ -notin $allowedInheritanceFiles }
    if ($unexpectedInheritance) {
        throw "G6 发现 Adapter 之外的 Dock 继承：$($unexpectedInheritance -join ', ')。"
    }
    foreach ($adapterPath in $allowedInheritanceFiles) {
        & rg --quiet 'internal sealed class Managed(Document|Tool)Dockable' $adapterPath
        if ($LASTEXITCODE -ne 0) {
            throw "G6 Adapter 必须保持 internal sealed：$adapterPath。"
        }
    }

    # 激活器只激活普通模型；ViewLocator 只能消费精确注册的预构建 Adapter View。
    $activatorPath = Join-Path $productionRoot `
        'Business\Plugins\Registration\PluginContributionActivator.cs'
    & rg --quiet 'Dock\.Model|IsAssignableFrom\(|\bis\s+(Document|Tool)\b' $activatorPath
    if ($LASTEXITCODE -eq 0) {
        throw 'G6 PluginContributionActivator 重新承担了 Dock 类型转换或验证。'
    }
    if ($LASTEXITCODE -gt 1) {
        throw '无法扫描 G6 PluginContributionActivator。'
    }

    $viewLocatorPath = Join-Path $productionRoot 'ViewLocator.cs'
    # Assembly.GetName 只用于白名单诊断；禁止的是枚举类型、按名称解析和反射构造。
    & rg --quiet 'Assembly\.(GetTypes|Load)|Type\.GetType|Activator\.CreateInstance' $viewLocatorPath
    if ($LASTEXITCODE -eq 0) {
        throw 'G6 ViewLocator 出现程序集扫描、类型名猜测或反射回退。'
    }
    if ($LASTEXITCODE -gt 1) {
        throw '无法扫描 G6 ViewLocator。'
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
    Write-Host "G6 Host Dock Adapter 专项门禁通过：$totalPassed 项。"
    # rg 的预期“未找到”会留下退出码 1；此处显式恢复成功码。
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
