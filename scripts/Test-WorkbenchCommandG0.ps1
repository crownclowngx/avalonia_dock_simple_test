[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$WorkflowStudioRoot
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$Configuration = 'Release'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workflowRoot = [IO.Path]::GetFullPath($WorkflowStudioRoot)
$resultRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\test-results\WorkbenchCommandG0'))
$allowedResultRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\test-results'))
$expectedHostCommit = 'b8def254b1ca76e481014b4075b0a60d155ec132'
$expectedHostTree = 'cc653631805ed0d09aa477d15c0fc5eeaaaae877'
$expectedWorkflowCommit = '0b3a3f55f43e66a914099f011dd344e7f556b56e'
$expectedWorkflowTree = 'ad082df6a0445c84216ab9b76e785bdae0e644f3'

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )

    if (-not $Condition) { throw $Message }
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [string]$WorkingDirectory = $repositoryRoot
    )

    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$FilePath $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
        }
    }
    finally { Pop-Location }
}

function Get-GitValue {
    param(
        [Parameter(Mandatory)] [string]$WorkingDirectory,
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    $value = @(& git -C $WorkingDirectory @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git -C $WorkingDirectory $($Arguments -join ' ') 失败：$($value -join ' ')"
    }
    return ($value -join "`n").Trim()
}

function Get-TrxCounts {
    param([Parameter(Mandatory)] [string]$Path)

    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "缺少 TRX：$Path。"
    [xml]$trx = Get-Content -Raw -LiteralPath $Path
    $counters = $trx.TestRun.ResultSummary.Counters
    Assert-True ([int]$counters.failed -eq 0) "TRX 存在失败测试：$Path。"
    Assert-True ([int]$counters.notExecuted -eq 0) "TRX 存在跳过或未执行测试：$Path。"
    Assert-True ([int]$counters.passed -gt 0) "TRX 没有实际通过测试：$Path。"
    return [ordered]@{
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        skipped = [int]$counters.notExecuted
    }
}

function Get-ApiFact {
    param([Parameter(Mandatory)] [string]$RelativePath)

    $path = Join-Path $repositoryRoot $RelativePath
    $lines = @(Get-Content -LiteralPath $path)
    Assert-True ($lines.Count -gt 0 -and $lines[0] -ceq '#nullable enable') `
        "API 文件缺少 nullable 头：$RelativePath。"
    return [ordered]@{
        entries = @($lines | Select-Object -Skip 1).Count
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
}

function Get-ZipText {
    param(
        [Parameter(Mandatory)] [string]$ZipPath,
        [Parameter(Mandatory)] [string]$EntryName
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entry = $archive.GetEntry($EntryName)
        Assert-True ($null -ne $entry) "ZIP 缺少条目：$EntryName。"
        $reader = [IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
}

function Get-ZipEntries {
    param([Parameter(Mandatory)] [string]$ZipPath)

    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try { return @($archive.Entries | ForEach-Object FullName) }
    finally { $archive.Dispose() }
}

Assert-True ($resultRoot.StartsWith(
        $allowedResultRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) `
    'G0 结果目录越过 artifacts/test-results 边界。'
Assert-True (Test-Path -LiteralPath (Join-Path $workflowRoot '.git')) `
    "WorkflowStudioRoot 不是独立 Git 仓库：$workflowRoot。"

$hostCommit = Get-GitValue $repositoryRoot @('rev-parse', 'HEAD')
$hostTree = Get-GitValue $repositoryRoot @('rev-parse', 'HEAD^{tree}')
$workflowCommit = Get-GitValue $workflowRoot @('rev-parse', 'HEAD')
$workflowTree = Get-GitValue $workflowRoot @('rev-parse', 'HEAD^{tree}')
Assert-True ($hostCommit -ceq $expectedHostCommit -and $hostTree -ceq $expectedHostTree) `
    'Host 输入提交或 tree 已漂移，必须重新冻结 G0。'
Assert-True ($workflowCommit -ceq $expectedWorkflowCommit -and $workflowTree -ceq $expectedWorkflowTree) `
    'WorkflowStudio 输入提交或 tree 已漂移，必须重新冻结 G0。'
$workflowStatusBefore = Get-GitValue $workflowRoot @(
    'status', '--porcelain=v1', '--untracked-files=all')
Assert-True ([string]::IsNullOrWhiteSpace($workflowStatusBefore)) `
    'WorkflowStudio 工作树必须保持干净。'

# G0 可以提交文档与验证基础设施，但不能夹带生产代码、版本、锁文件或其他仓库变化。
$allowedHostChanges = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($path in @(
        'docs/design/workbench-command-introduction-plan.md',
        'docs/plan-history/workbench-command/g0-facts-semantics-public-api.md',
        'Host/MyAvaloniaManagement.PluginTests/WorkbenchCommandG0ExternalPackageTests.cs',
        'scripts/Test-Documentation.ps1',
        'scripts/Test-MyPlugTestV3.ps1',
        'scripts/Test-WorkbenchCommandG0.ps1')) {
    [void]$allowedHostChanges.Add($path)
}
$statusLines = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
Assert-True ($LASTEXITCODE -eq 0) '无法读取 Host Git 状态。'
$statusLines = @($statusLines | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_)
    })
foreach ($line in $statusLines) {
    Assert-True ($line.Length -gt 3) "无法解析 Git 状态行：$line。"
    $relativePath = $line.Substring(3).Replace('\', '/')
    Assert-True ($allowedHostChanges.Contains($relativePath)) `
        "G0 出现计划外工作树变化：$relativePath。"
}

if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

[xml]$versions = Get-Content -Raw -LiteralPath (
    Join-Path $repositoryRoot 'Directory.Version.props')
$versionProperties = $versions.Project.PropertyGroup
Assert-True ([string]$versionProperties.MyAvaloniaProductVersion -ceq '3.0.0') `
    'G0 不得提升 Host 产品版本。'
Assert-True ([string]$versionProperties.MyAvaloniaPluginSdkVersion -ceq '3.2.0') `
    'G0 不得提前提升 Core/UI SDK。'
Assert-True ([string]$versionProperties.MyAvaloniaPluginSdkWorkflowVersion -ceq '1.0.0') `
    'G0 不得改变 Workflow SDK。'
Assert-True (
    [string]$versionProperties.MyAvaloniaV2ManifestSchemaVersion -ceq '2' -and
    [string]$versionProperties.MyAvaloniaV2DocumentEnvelopeSchemaVersion -ceq '2' -and
    [string]$versionProperties.MyAvaloniaV2LayoutSchemaVersion -ceq '2' -and
    [string]$versionProperties.MyAvaloniaV2LayoutFileName -ceq 'layout-v2.json' -and
    [string]$versionProperties.MyAvaloniaHostDataRootGeneration -ceq 'v2') `
    'G0 不得改变 manifest、Document、layout 或数据根协议。'

$api = [ordered]@{
    coreShipped = Get-ApiFact 'Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v3/PublicAPI.Shipped.txt'
    coreUnshipped = Get-ApiFact 'Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v3/PublicAPI.Unshipped.txt'
    uiShipped = Get-ApiFact 'Host/MyAvaloniaManagement.PluginSdk.UI/ApiCompatibility/v3/PublicAPI.Shipped.txt'
    uiUnshipped = Get-ApiFact 'Host/MyAvaloniaManagement.PluginSdk.UI/ApiCompatibility/v3/PublicAPI.Unshipped.txt'
}
$expectedApi = [ordered]@{
    coreShipped = [ordered]@{ entries = 127; sha256 = '063BCB5852827612B0501C135D23FECD015069A6F7DDB409547157E4FA00F80F' }
    coreUnshipped = [ordered]@{ entries = 72; sha256 = '3CAA366630A123B60C10E7E014FD39F711CF22BAC54B7554526CD73714B295C7' }
    uiShipped = [ordered]@{ entries = 45; sha256 = 'B11FBE768C3AD04CA65CBF5128BF6FCE8C00058EBB24052D51FE5464A65AD803' }
    uiUnshipped = [ordered]@{ entries = 6; sha256 = 'D1BAC6F52B49E18E9814B98198372FE71362E3C5C9D2220B1933E3B0EF99E65F' }
}
foreach ($name in $expectedApi.Keys) {
    Assert-True (
        [int]$api[$name].entries -eq [int]$expectedApi[$name].entries -and
        [string]$api[$name].sha256 -ceq [string]$expectedApi[$name].sha256) `
        "G0 API 基线漂移：$name。"
}

$designPath = Join-Path $repositoryRoot 'docs\design\workbench-command-introduction-plan.md'
$designText = Get-Content -Raw -LiteralPath $designPath
foreach ($requiredDecision in @(
        '每次只携带一个非空 `CommandId`',
        'myavalonia.host.menu.file.shared',
        'myavalonia.host.menu.view.shared',
        'myavalonia.host.menu.tools.shared',
        'myavalonia.host.menu.help.shared',
        '`Avalonia.Input.Key`',
        '`Avalonia.Input.KeyModifiers`',
        '插件命令执行失败；插件异常正文未写入诊断。',
        '10 秒协作退出宽限',
        'ClassicGame 明确记为未签署')) {
    Assert-True ($designText.Contains($requiredDecision, [StringComparison]::Ordinal)) `
        "总任务书缺少 G0 冻结决策：$requiredDecision。"
}

# 生产目录在 G0 必须仍然没有 Command 契约或运行实现；专项测试中的名称只用于证明缺席边界。
& rg --quiet 'interface\s+IWorkbenchDocumentCommandTarget|record\s+CommandId|class\s+WorkbenchCommandExecutor' `
    (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement') `
    (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginSdk') `
    (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginSdk.UI') `
    -g '*.cs'
Assert-True ($LASTEXITCODE -eq 1) 'G0 不得新增 Workbench Command 生产类型。'

# 复用已经签署的开发期总入口。该入口明确不调用 Windows CI/Smoke、发布验收或发布门禁。
Invoke-Checked pwsh @(
    '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-HostV4DevelopmentGate.ps1'),
    '-Stage', 'G7', '-Configuration', $Configuration)
$hostSummaryPath = Join-Path $repositoryRoot 'artifacts\test-results\HostV4\G7\summary.json'
Assert-True (Test-Path -LiteralPath $hostSummaryPath -PathType Leaf) `
    'Host V4 G7 开发门禁未生成摘要。'
$hostSummary = Get-Content -Raw -LiteralPath $hostSummaryPath | ConvertFrom-Json
Assert-True (
    [bool]$hostSummary.passed -and
    [double]$hostSummary.hostLineCoverage -ge 84.39 -and
    [double]$hostSummary.hostBranchCoverage -ge 70.58) `
    'Host 开发门禁或覆盖率未达到 G0 基线。'

$workflowSolution = Join-Path $workflowRoot 'WorkflowStudio.slnx'
$workflowTestProject = Join-Path $workflowRoot 'tests\WorkflowStudio.Tests\WorkflowStudio.Tests.csproj'
$workflowPluginProject = Join-Path $workflowRoot 'src\WorkflowStudio.Plugin\WorkflowStudio.Plugin.csproj'
$workflowPackagesText = Get-Content -Raw -LiteralPath (
    Join-Path $workflowRoot 'Directory.Packages.props')
foreach ($exactPackage in @(
        '<PackageVersion Include="MyAvaloniaManagement.Plugin.Build" Version="[1.1.2]" />',
        '<PackageVersion Include="MyAvaloniaManagement.PluginSdk" Version="[3.2.0]" />',
        '<PackageVersion Include="MyAvaloniaManagement.PluginSdk.UI" Version="[3.2.0]" />',
        '<PackageVersion Include="MyAvaloniaManagement.PluginSdk.Workflow" Version="[1.0.0]" />')) {
    Assert-True ($workflowPackagesText.Contains($exactPackage, [StringComparison]::Ordinal)) `
        "WorkflowStudio 精确包引用漂移：$exactPackage。"
}

Invoke-Checked dotnet @('restore', $workflowSolution, '--locked-mode') $workflowRoot
Invoke-Checked dotnet @(
    'build', $workflowSolution, '-c', $Configuration,
    '--no-restore', '-warnaserror') $workflowRoot
$workflowTestRoot = Join-Path $resultRoot 'workflow-tests'
New-Item -ItemType Directory -Path $workflowTestRoot -Force | Out-Null
Invoke-Checked dotnet @(
    'test', $workflowTestProject,
    '-c', $Configuration, '--no-build', '--no-restore',
    '--results-directory', $workflowTestRoot,
    '--logger', 'trx;LogFileName=WorkflowStudio.trx') $workflowRoot
$workflowTests = Get-TrxCounts (Join-Path $workflowTestRoot 'WorkflowStudio.trx')

$packageRoots = @(
    (Join-Path $resultRoot 'workflow-package-1'),
    (Join-Path $resultRoot 'workflow-package-2'))
foreach ($packageRoot in $packageRoots) {
    Invoke-Checked dotnet @(
        'msbuild', $workflowPluginProject,
        '-t:BuildManagedPluginPackage',
        "-p:Configuration=$Configuration",
        "-p:ManagedPluginPackageOutput=$packageRoot") $workflowRoot
}
$zips = @($packageRoots | ForEach-Object {
        @(Get-ChildItem -LiteralPath $_ -Filter '*.zip' -File)
    })
Assert-True ($zips.Count -eq 2) 'WorkflowStudio 两次包构建必须各生成一个 ZIP。'
$zipHash1 = (Get-FileHash -LiteralPath $zips[0].FullName -Algorithm SHA256).Hash
$zipHash2 = (Get-FileHash -LiteralPath $zips[1].FullName -Algorithm SHA256).Hash
Assert-True ($zipHash1 -ceq $zipHash2) 'WorkflowStudio 两次本地 ZIP 不确定。'
$zipEntries = Get-ZipEntries $zips[0].FullName
foreach ($requiredEntry in @(
        'Controls/WorkflowStudio/plugin.manifest.json',
        'Controls/WorkflowStudio/WorkflowStudio.Plugin.dll',
        'Controls/WorkflowStudio/WorkflowStudio.Plugin.deps.json')) {
    Assert-True ($zipEntries -ccontains $requiredEntry) "WorkflowStudio ZIP 缺少 $requiredEntry。"
}
Assert-True (-not ($zipEntries -match 'Standalone|Tests|MyAvaloniaManagement\.PluginSdk.*\.dll')) `
    'WorkflowStudio ZIP 混入 Standalone、Tests 或 Host 共享 SDK。'
$workflowManifest = Get-ZipText $zips[0].FullName `
    'Controls/WorkflowStudio/plugin.manifest.json' | ConvertFrom-Json
Assert-True (
    [int]$workflowManifest.schemaVersion -eq 2 -and
    $workflowManifest.pluginId -ceq 'myavalonia.plugin.workflow-studio' -and
    $workflowManifest.pluginVersion -ceq '1.1.0' -and
    $workflowManifest.sdk.minInclusive -ceq '3.2.0' -and
    $workflowManifest.sdk.maxExclusive -ceq '4.0.0') `
    'WorkflowStudio ZIP manifest 身份、版本、schema 或 SDK 区间不正确。'

# 真实加载验收使用 Host PluginTests 的发现/组合对象图，不启动窗口，也不调用 Windows Smoke 脚本。
$extractRoot = Join-Path $resultRoot 'workflow-package-extracted'
Expand-Archive -LiteralPath $zips[0].FullName -DestinationPath $extractRoot
$previousPluginRoot = $env:MYAVALONIA_WORKBENCH_COMMAND_G0_WORKFLOW_PLUGIN_ROOT
try {
    $env:MYAVALONIA_WORKBENCH_COMMAND_G0_WORKFLOW_PLUGIN_ROOT =
        Join-Path $extractRoot 'Controls'
    $loaderTestRoot = Join-Path $resultRoot 'workflow-host-loader'
    New-Item -ItemType Directory -Path $loaderTestRoot -Force | Out-Null
    Invoke-Checked dotnet @(
        'test', 'Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj',
        '-c', $Configuration, '-p:SkipPluginDeploy=true',
        '--no-build', '--no-restore',
        '--filter', 'FullyQualifiedName~WorkbenchCommandG0ExternalPackageTests',
        '--results-directory', $loaderTestRoot,
        '--logger', 'trx;LogFileName=WorkflowHostLoader.trx')
    $loaderTests = Get-TrxCounts (Join-Path $loaderTestRoot 'WorkflowHostLoader.trx')
}
finally {
    $env:MYAVALONIA_WORKBENCH_COMMAND_G0_WORKFLOW_PLUGIN_ROOT = $previousPluginRoot
}

Invoke-Checked pwsh @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-Documentation.ps1'))
$workflowStatusAfter = Get-GitValue $workflowRoot @(
    'status', '--porcelain=v1', '--untracked-files=all')
Assert-True ([string]::IsNullOrWhiteSpace($workflowStatusAfter)) `
    'WorkflowStudio 门禁结束后出现跟踪或未忽略变化。'

$summary = [ordered]@{
    schemaVersion = 1
    stage = 'G0'
    configuration = $Configuration
    input = [ordered]@{
        hostCommit = $hostCommit
        hostTree = $hostTree
        workflowStudioCommit = $workflowCommit
        workflowStudioTree = $workflowTree
    }
    versions = [ordered]@{
        product = '3.0.0'
        coreUiSdk = '3.2.0'
        workflowSdk = '1.0.0'
        pluginBuild = '1.1.2'
        templates = '1.2.0'
        commandCandidateCoreUi = '3.3.0'
        commandCandidateTemplates = '1.3.0'
    }
    api = $api
    host = [ordered]@{
        passed = [int]$hostSummary.hostPassed
        failed = 0
        skipped = 0
        lineCoverage = [double]$hostSummary.hostLineCoverage
        branchCoverage = [double]$hostSummary.hostBranchCoverage
        developmentGate = 'HostV4/G7'
        plugins = $hostSummary.plugins
    }
    workflowStudio = [ordered]@{
        tests = $workflowTests
        hostLoaderTests = $loaderTests
        deterministicBuilds = 2
        archiveSha256 = $zipHash1
        packageFiles = $zipEntries.Count
        manifest = $workflowManifest
    }
    decisions = [ordered]@{
        targetStateEvent = 'single-command-id'
        sharedMenuLocations = @(
            'myavalonia.host.menu.file.shared',
            'myavalonia.host.menu.view.shared',
            'myavalonia.host.menu.tools.shared',
            'myavalonia.host.menu.help.shared')
        gesture = 'Avalonia.Input.Key+KeyModifiers'
        pluginFailureUserMessage = '插件命令执行失败；插件异常正文未写入诊断。'
        shutdownGraceSeconds = 10
    }
    classicGameVerified = $false
    aiflow = $false
    windowsCi = $false
    windowsSmoke = $false
    releaseAcceptance = $false
    releaseGate = $false
    publishable = $false
    published = $false
    uploaded = $false
    tagCreated = $false
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
}
[IO.File]::WriteAllText(
    (Join-Path $resultRoot 'summary.json'),
    ($summary | ConvertTo-Json -Depth 12),
    [Text.UTF8Encoding]::new($false))
Write-Host (
    "Workbench Command G0 通过：Host $($summary.host.passed) 项，" +
    "WorkflowStudio $($summary.workflowStudio.tests.passed) 项，" +
    "覆盖率 $($summary.host.lineCoverage)% / $($summary.host.branchCoverage)%。")
