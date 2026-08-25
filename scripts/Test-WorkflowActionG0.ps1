[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$CandidateOnly
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\test-results\WorkflowActionG0'))
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryParent ('WorkflowActionG0-' + [Guid]::NewGuid().ToString('N'))
$baselineCommit = '030a4fca408f72aed75500c105dc51af855d9af7'
$baselineTree = 'd961e506357fbb6cc7f160f18b65acec0e3b72f5'
$coreShippedHash = '063BCB5852827612B0501C135D23FECD015069A6F7DDB409547157E4FA00F80F'
$uiShippedHash = 'B11FBE768C3AD04CA65CBF5128BF6FCE8C00058EBB24052D51FE5464A65AD803'
$assetRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginTests\TestAssets\WorkflowActionG0'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-ChildPath {
    param([string]$ChildPath, [string]$ParentPath, [string]$Description)
    $child = [IO.Path]::GetFullPath($ChildPath)
    $parent = [IO.Path]::GetFullPath($ParentPath).TrimEnd(
        [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    Assert-True ($child.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase)) `
        "$Description 路径越界：$child；允许根：$parent"
}

function Invoke-DotNet {
    param([string[]]$Arguments, [string]$WorkingDirectory = $repositoryRoot)
    Push-Location $WorkingDirectory
    try {
        & dotnet @Arguments | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
        }
    }
    finally { Pop-Location }
}

function Invoke-Pwsh {
    param([string[]]$Arguments)
    & pwsh @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "pwsh $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
    }
}

function Invoke-ExpectedBuildFailure {
    param([string[]]$Arguments, [string]$WorkingDirectory, [string[]]$ExpectedFragments)
    Push-Location $WorkingDirectory
    try {
        $output = @(& dotnet @Arguments 2>&1)
        Assert-True ($LASTEXITCODE -ne 0) '未登记候选 API 的构建意外成功。'
        $text = $output -join [Environment]::NewLine
        foreach ($fragment in $ExpectedFragments) {
            Assert-True ($text.Contains($fragment, [StringComparison]::Ordinal)) `
                "候选 API 失败输出缺少 $fragment。"
        }
        return $text
    }
    finally { Pop-Location }
}

function Set-CandidateApiFromDiagnostics {
    param(
        [string]$DiagnosticText,
        [string]$TargetPath
    )

    $entries = [Collections.Generic.List[string]]::new()
    foreach ($line in $DiagnosticText -split '\r?\n') {
        if (-not $line.Contains('error RS0016:', [StringComparison]::Ordinal)) { continue }
        $startMarker = '符号“'
        $endMarker = '”不是已声明'
        $start = $line.IndexOf($startMarker, [StringComparison]::Ordinal)
        $end = $line.IndexOf($endMarker, [StringComparison]::Ordinal)
        Assert-True ($start -ge 0 -and $end -gt $start) `
            "无法从 RS0016 诊断提取候选 API：$line"
        $entry = $line.Substring($start + $startMarker.Length, $end - $start - $startMarker.Length)
        $entries.Add($entry)
    }

    [string[]]$uniqueEntries = @($entries | Select-Object -Unique)
    [Array]::Sort($uniqueEntries, [StringComparer]::Ordinal)
    Assert-True ($uniqueEntries.Count -gt 0) "候选 API 诊断没有为 $TargetPath 生成条目。"
    [IO.File]::WriteAllText(
        $TargetPath,
        "#nullable enable`r`n" + ($uniqueEntries -join "`r`n") + "`r`n",
        [Text.UTF8Encoding]::new($false))
}

function Get-TrxPassed {
    param([string]$Path)
    [xml]$trx = Get-Content -LiteralPath $Path
    $counters = $trx.TestRun.ResultSummary.Counters
    Assert-True (
        [int]$counters.failed -eq 0 -and
        [int]$counters.notExecuted -eq 0 -and
        [int]$counters.executed -eq [int]$counters.passed) `
        "TRX 未做到全部执行、零失败、零跳过：$Path"
    return [int]$counters.passed
}

function Get-ApiEntries {
    param([string]$Path)
    $lines = @(Get-Content -LiteralPath $Path)
    Assert-True ($lines.Count -ge 1 -and $lines[0] -ceq '#nullable enable') `
        "API 文件缺少 nullable 头：$Path"
    return @($lines | Select-Object -Skip 1)
}

function Set-ExactVersion {
    param([string]$Path)
    $text = [IO.File]::ReadAllText($Path)
    foreach ($replacement in @(
            @('<MyAvaloniaPluginSdkVersion>3.0.0</MyAvaloniaPluginSdkVersion>', '<MyAvaloniaPluginSdkVersion>3.1.0</MyAvaloniaPluginSdkVersion>'),
            @('<MyAvaloniaPluginSdkFileVersion>3.0.0.0</MyAvaloniaPluginSdkFileVersion>', '<MyAvaloniaPluginSdkFileVersion>3.1.0.0</MyAvaloniaPluginSdkFileVersion>'),
            @('<MyAvaloniaPluginSdkAssemblyVersion>3.0.0.0</MyAvaloniaPluginSdkAssemblyVersion>', '<MyAvaloniaPluginSdkAssemblyVersion>3.1.0.0</MyAvaloniaPluginSdkAssemblyVersion>'))) {
        $old = $replacement[0]
        $new = $replacement[1]
        Assert-True ($text.IndexOf($old, [StringComparison]::Ordinal) -ge 0) `
            "候选版本替换哨兵不存在：$old"
        $text = $text.Replace($old, $new, [StringComparison]::Ordinal)
    }
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
}

function Copy-DirectoryContents {
    param([string]$Source, [string]$Destination)
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

Assert-ChildPath $resultRoot $repositoryRoot 'G0 结果'
Assert-ChildPath $temporaryRoot $temporaryParent 'G0 临时树'
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null
New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

$suiteCounts = [ordered]@{}
$candidatePackageHashes = [ordered]@{}
$baselineArchiveHash = $null
$candidateUnshipped = [ordered]@{}
$hostCoverage = $null
$worktreeAdded = $false

try {
    Push-Location $repositoryRoot
    try {
        $actualCommit = (& git rev-parse $baselineCommit).Trim()
        $actualTree = (& git show -s --format='%T' $baselineCommit).Trim()
        Assert-True ($LASTEXITCODE -eq 0 -and $actualCommit -ceq $baselineCommit) `
            'G0 输入提交不存在或身份漂移。'
        Assert-True ($actualTree -ceq $baselineTree) 'G0 输入 Git tree 与冻结事实不一致。'
    }
    finally { Pop-Location }

    [xml]$versionDocument = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props')
    Assert-True (
        [string]$versionDocument.Project.PropertyGroup.MyAvaloniaPluginSdkVersion -ceq '3.1.0') `
        'G0 重新签署后生产 Plugin SDK 候选必须为 3.1.0。'
    $apiPaths = [ordered]@{
        CoreShipped = 'Host\MyAvaloniaManagement.PluginSdk\ApiCompatibility\v3\PublicAPI.Shipped.txt'
        CoreUnshipped = 'Host\MyAvaloniaManagement.PluginSdk\ApiCompatibility\v3\PublicAPI.Unshipped.txt'
        UiShipped = 'Host\MyAvaloniaManagement.PluginSdk.UI\ApiCompatibility\v3\PublicAPI.Shipped.txt'
        UiUnshipped = 'Host\MyAvaloniaManagement.PluginSdk.UI\ApiCompatibility\v3\PublicAPI.Unshipped.txt'
    }
    Assert-True ((Get-FileHash (Join-Path $repositoryRoot $apiPaths.CoreShipped)).Hash -ceq $coreShippedHash) `
        '生产 Core v3 Shipped 哈希漂移。'
    Assert-True ((Get-FileHash (Join-Path $repositoryRoot $apiPaths.UiShipped)).Hash -ceq $uiShippedHash) `
        '生产 UI v3 Shipped 哈希漂移。'
    Assert-True ((Get-ApiEntries (Join-Path $repositoryRoot $apiPaths.CoreShipped)).Count -eq 127) `
        '生产 Core v3 Shipped 必须为 127 条。'
    Assert-True ((Get-ApiEntries (Join-Path $repositoryRoot $apiPaths.UiShipped)).Count -eq 45) `
        '生产 UI v3 Shipped 必须为 45 条。'
    $productionCoreUnshipped = Get-ApiEntries (Join-Path $repositoryRoot $apiPaths.CoreUnshipped)
    $productionUiUnshipped = Get-ApiEntries (Join-Path $repositoryRoot $apiPaths.UiUnshipped)
    Assert-True ($productionCoreUnshipped.Count -eq 72) `
        'G0 重新签署后的生产 Core v3 Unshipped 必须为 72 条。'
    Assert-True ($productionUiUnshipped.Count -eq 6) `
        'G0 重新签署后的生产 UI v3 Unshipped 必须为 6 条。'
    Assert-True (@($productionCoreUnshipped | Where-Object {
                $_.Contains('IWorkflowActionGateway.CreateRun()', [StringComparison]::Ordinal)
            }).Count -eq 1) '生产契约缺少 caller-bound CreateRun。'
    Assert-True (@($productionCoreUnshipped | Where-Object {
                $_.Contains('IWorkflowActionRun.InvokeAsync', [StringComparison]::Ordinal)
            }).Count -eq 1) '生产契约缺少精确 IWorkflowActionRun 调用边界。'

    # 使用固定输入提交建立隔离副本。测试资产来自当前工作树，但生产源码只来自冻结 Git tree。
    $stagedRoot = Join-Path $temporaryRoot 'repo'
    Push-Location $repositoryRoot
    try {
        & git worktree add --detach $stagedRoot $baselineCommit
        if ($LASTEXITCODE -ne 0) { throw '无法建立 G0 固定输入 worktree。' }
        $worktreeAdded = $true
    }
    finally { Pop-Location }

    # 真实 3.0 ZIP 必须从固定 baseline tree 构建，不能用已经提升到 SDK 3.1 的当前工作树
    # 冒充旧插件。输出仍只进入 Git 忽略的 G0 证据目录。
    $baselinePackageRoot = Join-Path $resultRoot 'baseline-package'
    & (Join-Path $stagedRoot 'scripts\Build-ManagedPluginPackage.ps1') `
        -Project 'Plugins\MyPlugTest\MyPlugTest\MyPlugTest.csproj' `
        -Configuration $Configuration `
        -OutputDirectory $baselinePackageRoot
    if ($LASTEXITCODE -ne 0) { throw '固定 baseline tree 的真实 3.0 MyPlugTest 包构建失败。' }
    $baselineArchive = Get-ChildItem -LiteralPath $baselinePackageRoot -Filter '*.zip' -File |
        Select-Object -First 1
    Assert-True ($null -ne $baselineArchive) 'G0 未找到固定 baseline tree 的真实 3.0 MyPlugTest ZIP。'
    $baselineArchiveHash = (Get-FileHash -LiteralPath $baselineArchive.FullName -Algorithm SHA256).Hash
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj') `
        -Destination (
        Join-Path $stagedRoot 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj') -Force

    # 在未提升版本的隔离副本中证明新插件先被版本政策拒绝，伪 DLL 不得进入加载阶段。
    Copy-Item -LiteralPath (Join-Path $assetRoot 'Baseline\WorkflowActionG0OldHostTests.cs') `
        -Destination (Join-Path $stagedRoot 'Host\MyAvaloniaManagement.PluginTests\WorkflowActionG0OldHostTests.cs')
    Invoke-DotNet @(
        'restore', 'Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj',
        '--locked-mode', '--nologo') $stagedRoot
    $baselineTrxRoot = Join-Path $resultRoot 'baseline-host'
    New-Item -ItemType Directory -Path $baselineTrxRoot | Out-Null
    Invoke-DotNet @(
        'test', 'Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj',
        '-c', $Configuration, '-p:SkipPluginDeploy=true', '--no-restore', '--nologo',
        '--filter', 'FullyQualifiedName~WorkflowActionG0OldHostTests',
        '--results-directory', $baselineTrxRoot,
        '--logger', 'trx;LogFileName=BaselineOldHost.trx') $stagedRoot
    $suiteCounts.BaselineOldHost = Get-TrxPassed (Join-Path $baselineTrxRoot 'BaselineOldHost.trx')

    # 在固定 3.0 临时副本叠加重新签署后的 3.1 契约，再与当前生产 API 数量交叉验证。
    Set-ExactVersion (Join-Path $stagedRoot 'Directory.Version.props')
    Copy-Item -LiteralPath (Join-Path $assetRoot 'Candidate\Core\WorkflowActionContracts.cs') `
        -Destination (Join-Path $stagedRoot 'Host\MyAvaloniaManagement.PluginSdk\WorkflowActionContracts.cs')
    Copy-Item -LiteralPath (Join-Path $assetRoot 'Candidate\UI\WorkflowActionRegistrationContracts.cs') `
        -Destination (Join-Path $stagedRoot 'Host\MyAvaloniaManagement.PluginSdk.UI\WorkflowActionRegistrationContracts.cs')
    Copy-Item -LiteralPath (Join-Path $assetRoot 'Candidate\Tests\WorkflowActionG0CandidateTests.cs') `
        -Destination (Join-Path $stagedRoot 'Host\MyAvaloniaManagement.PluginTests\WorkflowActionG0CandidateTests.cs')
    Copy-Item -LiteralPath (Join-Path $assetRoot 'Candidate\Tests\WorkflowActionG0SchemaProfile.cs') `
        -Destination (Join-Path $stagedRoot 'Host\MyAvaloniaManagement.PluginTests\WorkflowActionG0SchemaProfile.cs')
    Copy-DirectoryContents `
        (Join-Path $assetRoot 'Candidate\Provider') `
        (Join-Path $stagedRoot 'Host\MyAvaloniaManagement.PluginTests\TestAssets\WorkflowActionG0\Candidate\Provider')
    Copy-DirectoryContents `
        (Join-Path $assetRoot 'Candidate\Consumer') `
        (Join-Path $stagedRoot 'Host\MyAvaloniaManagement.PluginTests\TestAssets\WorkflowActionG0\Candidate\Consumer')

    $coreProject = 'Host/MyAvaloniaManagement.PluginSdk/MyAvaloniaManagement.PluginSdk.csproj'
    $uiProject = 'Host/MyAvaloniaManagement.PluginSdk.UI/MyAvaloniaManagement.PluginSdk.UI.csproj'
    Invoke-DotNet @('restore', $uiProject, '--locked-mode', '--nologo') $stagedRoot
    $coreDiagnostics = Invoke-ExpectedBuildFailure @(
        'build', $coreProject, '-c', $Configuration, '--no-restore', '--nologo', '-t:Rebuild') `
        $stagedRoot @('RS0016', 'WorkflowAction')
    # 本地化诊断中的“符号”正文就是 PublicApiAnalyzer 要求的规范 API 文本。只把这些精确条目
    # 写入临时 Unshipped，再由同一 Analyzer 重建复核；不关闭诊断，也不猜测签名格式。
    Set-CandidateApiFromDiagnostics $coreDiagnostics (
        Join-Path $stagedRoot $apiPaths.CoreUnshipped)
    Invoke-DotNet @(
        'build', $coreProject, '-c', $Configuration, '--no-restore', '--nologo',
        '-warnaserror', '-t:Rebuild') $stagedRoot
    $uiDiagnostics = Invoke-ExpectedBuildFailure @(
        'build', $uiProject, '-c', $Configuration, '--no-restore', '--nologo', '-t:Rebuild') `
        $stagedRoot @('RS0016', 'WorkflowAction')
    Set-CandidateApiFromDiagnostics $uiDiagnostics (
        Join-Path $stagedRoot $apiPaths.UiUnshipped)
    Invoke-DotNet @(
        'build', $uiProject, '-c', $Configuration, '--no-restore', '--nologo',
        '-warnaserror', '-t:Rebuild') $stagedRoot
    Assert-True ((Get-FileHash (Join-Path $stagedRoot $apiPaths.CoreShipped)).Hash -ceq $coreShippedHash) `
        '候选构建改写了 Core v3 Shipped。'
    Assert-True ((Get-FileHash (Join-Path $stagedRoot $apiPaths.UiShipped)).Hash -ceq $uiShippedHash) `
        '候选构建改写了 UI v3 Shipped。'
    $candidateUnshipped.Core = (Get-ApiEntries (Join-Path $stagedRoot $apiPaths.CoreUnshipped)).Count
    $candidateUnshipped.UI = (Get-ApiEntries (Join-Path $stagedRoot $apiPaths.UiUnshipped)).Count
    Assert-True ($candidateUnshipped.Core -gt 0 -and $candidateUnshipped.UI -gt 0) `
        '候选 Core/UI public API 未进入临时 v3 Unshipped。'
    Assert-True ($candidateUnshipped.Core -eq 72 -and $candidateUnshipped.UI -eq 6) `
        '重新签署候选 API 数量与生产 Core/UI Unshipped 不一致。'

    # 生成候选包，证明 Core/UI 真实 nupkg 可以被外部项目消费；包只保存在临时树。
    $candidatePackageRoot = Join-Path $temporaryRoot 'candidate-packages'
    New-Item -ItemType Directory -Path $candidatePackageRoot | Out-Null
    foreach ($project in @(
            'Host/MyAvaloniaManagement.PluginSdk/MyAvaloniaManagement.PluginSdk.csproj',
            $uiProject)) {
        Invoke-DotNet @(
            'pack', $project, '-c', $Configuration, '--no-restore', '--nologo',
            '-o', $candidatePackageRoot) $stagedRoot
    }
    foreach ($package in Get-ChildItem -LiteralPath $candidatePackageRoot -Filter '*.3.1.0.nupkg') {
        $candidatePackageHashes[$package.Name] = (
            Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
    }
    Assert-True ($candidatePackageHashes.Count -eq 2) '候选 Core/UI 3.1.0 nupkg 数量不正确。'

    # 构建独立 Provider/Consumer，并准备真实 manifest/deps 目录；共享 SDK 不复制进插件目录。
    $providerProject = 'Host/MyAvaloniaManagement.PluginTests/TestAssets/WorkflowActionG0/Candidate/Provider/WorkflowActionG0.Provider.csproj'
    $consumerProject = 'Host/MyAvaloniaManagement.PluginTests/TestAssets/WorkflowActionG0/Candidate/Consumer/WorkflowActionG0.Consumer.csproj'
    foreach ($fixtureProject in @($providerProject, $consumerProject)) {
        Invoke-DotNet @('restore', $fixtureProject, '--nologo') $stagedRoot
        Invoke-DotNet @(
            'build', $fixtureProject, '-c', $Configuration,
            '--no-restore', '--nologo', '-warnaserror') $stagedRoot
    }
    $providerBuild = Join-Path $stagedRoot (
        "Host\MyAvaloniaManagement.PluginTests\TestAssets\WorkflowActionG0\Candidate\Provider\bin\$Configuration\net10.0")
    $consumerBuild = Join-Path $stagedRoot (
        "Host\MyAvaloniaManagement.PluginTests\TestAssets\WorkflowActionG0\Candidate\Consumer\bin\$Configuration\net10.0")
    $pluginRoot = Join-Path $temporaryRoot 'plugin-root'
    $providerDirectory = Join-Path $pluginRoot 'Provider'
    $consumerDirectory = Join-Path $pluginRoot 'Consumer'
    Copy-DirectoryContents $providerBuild $providerDirectory
    Copy-DirectoryContents $consumerBuild $consumerDirectory
    foreach ($pluginDirectory in @($providerDirectory, $consumerDirectory)) {
        foreach ($sharedName in @(
                'MyAvaloniaManagement.PluginSdk.dll',
                'MyAvaloniaManagement.PluginSdk.UI.dll')) {
            $sharedCopy = Join-Path $pluginDirectory $sharedName
            if (Test-Path -LiteralPath $sharedCopy) {
                Remove-Item -LiteralPath $sharedCopy -Force
            }
        }
    }
    [IO.File]::WriteAllText(
        (Join-Path $providerDirectory 'plugin.manifest.json'),
        @'
{
  "schemaVersion": 2,
  "pluginId": "myavalonia.plugin.workflow-g0-provider",
  "pluginVersion": "1.0.0",
  "entryPoint": {
    "assembly": "WorkflowActionG0.Provider.dll",
    "type": "WorkflowActionG0.Provider.ProviderModule"
  },
  "sdk": { "minInclusive": "3.1.0", "maxExclusive": "4.0.0" }
}
'@,
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $consumerDirectory 'plugin.manifest.json'),
        @'
{
  "schemaVersion": 2,
  "pluginId": "myavalonia.plugin.workflow-g0-consumer",
  "pluginVersion": "1.0.0",
  "entryPoint": {
    "assembly": "WorkflowActionG0.Consumer.dll",
    "type": "WorkflowActionG0.Consumer.ConsumerModule"
  },
  "sdk": { "minInclusive": "3.1.0", "maxExclusive": "4.0.0" }
}
'@,
        [Text.UTF8Encoding]::new($false))

    # 解压真实 3.0 ZIP，并把唯一 manifest 所在目录归一为候选 Host 的一个插件目录。
    $oldExtract = Join-Path $temporaryRoot 'old-extract'
    Expand-Archive -LiteralPath $baselineArchive.FullName -DestinationPath $oldExtract
    $oldManifest = @(Get-ChildItem -LiteralPath $oldExtract -Recurse -Filter 'plugin.manifest.json' -File)
    Assert-True ($oldManifest.Count -eq 1) '真实 3.0 ZIP 必须包含唯一 manifest。'
    $oldPluginRoot = Join-Path $temporaryRoot 'old-plugin-root'
    Copy-DirectoryContents $oldManifest[0].Directory.FullName (Join-Path $oldPluginRoot 'MyPlugTest')

    $candidateTrxRoot = Join-Path $resultRoot 'candidate-tests'
    New-Item -ItemType Directory -Path $candidateTrxRoot | Out-Null
    $env:MYAVALONIA_WORKFLOW_G0_PLUGIN_ROOT = $pluginRoot
    $env:MYAVALONIA_WORKFLOW_G0_OLD_PLUGIN_ROOT = $oldPluginRoot
    try {
        Invoke-DotNet @(
            'restore', 'Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj',
            '--locked-mode', '--nologo') $stagedRoot
        Invoke-DotNet @(
            'test', 'Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj',
            '-c', $Configuration, '-p:SkipPluginDeploy=true', '--no-restore', '--nologo',
            '--filter', 'FullyQualifiedName~WorkflowActionG0CandidateTests',
            '--results-directory', $candidateTrxRoot,
            '--logger', 'trx;LogFileName=Candidate31.trx') $stagedRoot
    }
    finally {
        Remove-Item Env:MYAVALONIA_WORKFLOW_G0_PLUGIN_ROOT -ErrorAction SilentlyContinue
        Remove-Item Env:MYAVALONIA_WORKFLOW_G0_OLD_PLUGIN_ROOT -ErrorAction SilentlyContinue
    }
    $suiteCounts.Candidate31 = Get-TrxPassed (Join-Path $candidateTrxRoot 'Candidate31.trx')

    if (-not $CandidateOnly) {
        Invoke-DotNet @('tool', 'restore')
        Invoke-DotNet @('restore', 'MyAvaloniaManagement.sln', '--locked-mode', '--nologo')
        Invoke-DotNet @(
            'build', 'MyAvaloniaManagement.sln', '-c', $Configuration,
            '--no-restore', '--nologo', '-warnaserror', '-p:SkipPluginDeploy=true')

        & (Join-Path $PSScriptRoot 'Invoke-MyAvaloniaManagementTests.ps1') `
            -Configuration $Configuration -NoRestore
        if ($LASTEXITCODE -ne 0) { throw 'Host 三层测试或覆盖率门禁失败。' }
        $hostSummary = Get-Content -Raw -LiteralPath (
            Join-Path $repositoryRoot 'artifacts\test-results\MyAvaloniaManagement\summary.json') |
            ConvertFrom-Json
        $suiteCounts.Host = [int]$hostSummary.passed
        $hostCoverage = [ordered]@{
            line = [double]$hostSummary.lineCoverage
            branch = [double]$hostSummary.branchCoverage
        }

        $regressionProjects = [ordered]@{
            PluginSdk = 'Host/MyAvaloniaManagement.PluginSdk.Tests/MyAvaloniaManagement.PluginSdk.Tests.csproj'
            MyPlugTest = 'Plugins/MyPlugTest/MyPlugTest.Tests/MyPlugTest.Tests.csproj'
            DaTangAccountingHelp = 'Plugins/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug.Tests/DaTangAccountingHelpPlug.Tests.csproj'
            MySmallTools = 'Plugins/MySmallTools/MySmallTools.Tests/MySmallTools.Tests.csproj'
            BiliDownloader = 'Plugins/BiliDownloader/BiliDownloader.Tests/BiliDownloader.Tests.csproj'
        }
        foreach ($suite in $regressionProjects.GetEnumerator()) {
            $suiteRoot = Join-Path $resultRoot "regression\$($suite.Key)"
            New-Item -ItemType Directory -Path $suiteRoot -Force | Out-Null
            Invoke-DotNet @(
                'test', $suite.Value, '-c', $Configuration,
                '-p:SkipPluginDeploy=true', '--no-restore', '--nologo', '-warnaserror',
                '--results-directory', $suiteRoot,
                '--logger', "trx;LogFileName=$($suite.Key).trx")
            $suiteCounts[$suite.Key] = Get-TrxPassed (Join-Path $suiteRoot "$($suite.Key).trx")
        }

        Invoke-Pwsh @(
            '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-PluginSdkCompatibility.ps1'),
            '-Baseline', 'v3', '-Configuration', $Configuration)
        Invoke-Pwsh @(
            '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-PluginSdkPackage.ps1'),
            '-Configuration', $Configuration)
        & (Join-Path $PSScriptRoot 'Test-Documentation.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'G0 文档门禁失败。' }
    }

    $summary = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        inputCommit = $baselineCommit
        inputTree = $baselineTree
        productVersion = '3.0.0'
        productionSdkVersion = '3.1.0'
        candidateSdkVersion = '3.1.0'
        sdkRoute = '3.1-compatible-addition'
        api = [ordered]@{
            coreShippedEntries = 127
            uiShippedEntries = 45
            productionCoreUnshippedEntries = 72
            productionUiUnshippedEntries = 6
            coreShippedSha256 = $coreShippedHash
            uiShippedSha256 = $uiShippedHash
            candidateCoreUnshippedEntries = $candidateUnshipped.Core
            candidateUiUnshippedEntries = $candidateUnshipped.UI
        }
        budgets = [ordered]@{
            schemaBytes = 65536
            inputBytes = 262144
            outputBytes = 1048576
            depth = 16
            properties = 128
            arrayItems = 1024
            stringBytes = 65536
        }
        tests = $suiteCounts
        hostCoverage = $hostCoverage
        oldPluginArchiveSha256 = $baselineArchiveHash
        candidatePackageSha256 = $candidatePackageHashes
        aiflow = $false
        windowsCi = $false
        windowsSmoke = $false
        releaseAcceptance = $false
        releaseGate = $false
        publishable = $false
        published = $false
        uploaded = $false
        tagCreated = $false
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))
    Write-Host "[Workflow Action G0] 通过：SDK 路线为 3.1-compatible-addition。摘要：$resultRoot\summary.json"
}
finally {
    Remove-Item Env:MYAVALONIA_WORKFLOW_G0_PLUGIN_ROOT -ErrorAction SilentlyContinue
    Remove-Item Env:MYAVALONIA_WORKFLOW_G0_OLD_PLUGIN_ROOT -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $temporaryRoot) {
        Assert-ChildPath $temporaryRoot $temporaryParent 'G0 临时树'
        & dotnet build-server shutdown | Out-Host
        if ($worktreeAdded) {
            Push-Location $repositoryRoot
            try {
                for ($attempt = 1; $attempt -le 20 -and
                    (Test-Path -LiteralPath $stagedRoot); $attempt++) {
                    & git worktree remove --force $stagedRoot 2>$null
                    if (Test-Path -LiteralPath $stagedRoot) {
                        Start-Sleep -Milliseconds 500
                    }
                }
                & git worktree prune
            }
            finally { Pop-Location }
        }
        if (Test-Path -LiteralPath $temporaryRoot) {
            # ALC/编译服务器在 Windows 上可能短暂保留文件句柄；只对已经验证位于系统临时根
            # 下的本轮目录做有限重试，绝不把清理目标扩大到仓库或用户目录。
            for ($attempt = 1; $attempt -le 20; $attempt++) {
                try {
                    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
                    break
                }
                catch {
                    if ($attempt -ge 20) { throw }
                    Start-Sleep -Milliseconds 500
                }
            }
        }
    }
}
