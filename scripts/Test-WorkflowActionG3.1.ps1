[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\test-results\WorkflowActionG31'))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\test-results'))
if (-not $resultRoot.StartsWith(
        $allowedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "G3.1 结果目录越界：$resultRoot。"
}

function Invoke-Checked {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
    }
}

function Assert-True {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-TestSuite {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Project,
        [switch]$Coverage)
    $suiteRoot = Join-Path $resultRoot $Name
    $arguments = @(
        'test', (Join-Path $repositoryRoot $Project),
        '-c', $Configuration, '--no-build', '--no-restore',
        '--results-directory', $suiteRoot,
        '--logger', "trx;LogFileName=$Name.trx")
    if ($Coverage) { $arguments += '--collect:XPlat Code Coverage' }
    $testOutput = @(& dotnet @arguments 2>&1)
    $testExitCode = $LASTEXITCODE
    $testOutput | ForEach-Object { Write-Host $_ }
    if ($testExitCode -ne 0) {
        throw "dotnet $($arguments -join ' ') 失败，退出码：$testExitCode。"
    }
    [xml]$trx = Get-Content -Raw -LiteralPath (Join-Path $suiteRoot "$Name.trx")
    $counters = $trx.TestRun.ResultSummary.Counters
    Assert-True ([int]$counters.failed -eq 0) "$Name 存在失败测试。"
    Assert-True ([int]$counters.notExecuted -eq 0) "$Name 存在跳过或未执行测试。"
    Assert-True ([int]$counters.passed -gt 0) "$Name 没有实际执行测试。"
    return [int]$counters.passed
}

if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null

Push-Location $repositoryRoot
try {
    # 本入口只执行 G3.1 开发与 SDK 候选门禁，不调用 AIFLOW、Windows CI/Smoke、
    # Host Release Acceptance、Host 产品打包、标签、签名或上传。
    Invoke-Checked dotnet @(
        'restore', 'MyAvaloniaManagement.sln', '--locked-mode',
        '--source=https://api.nuget.org/v3/index.json')
    Invoke-Checked dotnet @(
        'build', 'MyAvaloniaManagement.sln', '-c', $Configuration,
        '--no-restore', '-warnaserror')

    $passed = [ordered]@{}
    $passed.sdk = Invoke-TestSuite 'PluginSdk' `
        'Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj' -Coverage
    $passed.unit = Invoke-TestSuite 'HostUnit' `
        'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
    $passed.headlessUi = Invoke-TestSuite 'HeadlessUi' `
        'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
    $passed.plugin = Invoke-TestSuite 'Plugin' `
        'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'

    $coveragePath = (Get-ChildItem -LiteralPath (Join-Path $resultRoot 'PluginSdk') `
        -Filter 'coverage.cobertura.xml' -File -Recurse | Select-Object -First 1).FullName
    Assert-True (-not [string]::IsNullOrWhiteSpace($coveragePath)) 'Workflow SDK 未生成覆盖率报告。'
    [xml]$coverage = Get-Content -Raw -LiteralPath $coveragePath
    $workflowPackage = @($coverage.coverage.packages.package | Where-Object {
            $_.name -ceq 'MyAvaloniaManagement.PluginSdk.Workflow'
        })
    Assert-True ($workflowPackage.Count -eq 1) '覆盖率报告缺少唯一 Workflow SDK package。'
    $lineCoverage = [Math]::Round(100 * [double]$workflowPackage[0].'line-rate', 2)
    $branchCoverage = [Math]::Round(100 * [double]$workflowPackage[0].'branch-rate', 2)
    Assert-True ($lineCoverage -ge 85) "Workflow SDK 行覆盖率 $lineCoverage% 低于 85%。"
    Assert-True ($branchCoverage -ge 75) "Workflow SDK 分支覆盖率 $branchCoverage% 低于 75%。"
    foreach ($criticalFile in @(
            'WorkflowSchemaValidator.cs',
            'WorkflowReferenceTypeSystem.cs',
            'WorkflowReferencePath.cs',
            'WorkflowCatalogRevisionCalculator.cs')) {
        $classes = @($workflowPackage[0].classes.class | Where-Object {
                $_.filename -like "*$criticalFile"
            })
        Assert-True ($classes.Count -gt 0) "覆盖率报告缺少协议关键文件：$criticalFile。"
        $lines = @($classes | ForEach-Object { $_.lines.line } | Group-Object number |
            ForEach-Object {
                [pscustomobject]@{ Hits = [int](($_.Group | Measure-Object -Property hits -Maximum).Maximum) }
            })
        $covered = @($lines | Where-Object { $_.Hits -gt 0 }).Count
        $actual = [Math]::Round(100 * $covered / $lines.Count, 2)
        Assert-True ($actual -ge 90) "协议关键文件 $criticalFile 行覆盖率 $actual% 低于 90%。"
    }

    $packageRoot = Join-Path $resultRoot 'candidate-feed'
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    foreach ($project in @(
            'Host\MyAvaloniaManagement.PluginSdk\MyAvaloniaManagement.PluginSdk.csproj',
            'Host\MyAvaloniaManagement.PluginSdk.UI\MyAvaloniaManagement.PluginSdk.UI.csproj',
            'Host\MyAvaloniaManagement.PluginSdk.Workflow\MyAvaloniaManagement.PluginSdk.Workflow.csproj')) {
        Invoke-Checked dotnet @(
            'pack', (Join-Path $repositoryRoot $project), '-c', $Configuration,
            '--no-build', '--no-restore', '-o', $packageRoot)
    }
    $expected = @(
        'MyAvaloniaManagement.PluginSdk.3.2.0.nupkg',
        'MyAvaloniaManagement.PluginSdk.3.2.0.snupkg',
        'MyAvaloniaManagement.PluginSdk.UI.3.2.0.nupkg',
        'MyAvaloniaManagement.PluginSdk.UI.3.2.0.snupkg',
        'MyAvaloniaManagement.PluginSdk.Workflow.1.0.0.nupkg',
        'MyAvaloniaManagement.PluginSdk.Workflow.1.0.0.snupkg')
    $actualPackages = @(Get-ChildItem -LiteralPath $packageRoot -File |
        Select-Object -ExpandProperty Name | Sort-Object)
    Assert-True (($actualPackages -join '|') -ceq (($expected | Sort-Object) -join '|')) `
        "SDK 候选文件集合不正确：$($actualPackages -join ', ')。"

    $hashes = [ordered]@{}
    Get-ChildItem -LiteralPath $packageRoot -File | Sort-Object Name | ForEach-Object {
        $hashes[$_.Name] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    $summary = [ordered]@{
        schemaVersion = 1
        stage = 'G3.1'
        configuration = $Configuration
        passed = $passed
        workflowSdkLineCoverage = $lineCoverage
        workflowSdkBranchCoverage = $branchCoverage
        packages = $hashes
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        hostReleaseGate = $false
        hostProductPublished = $false
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))
    Write-Host "Workflow Action G3.1 平台门禁通过；候选 feed：$packageRoot"
}
finally {
    Pop-Location
    & dotnet build-server shutdown | Out-Null
}
