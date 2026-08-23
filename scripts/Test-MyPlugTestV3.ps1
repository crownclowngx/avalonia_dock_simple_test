[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot `
    'artifacts\test-results\MyPlugTestV3'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G9 结果目录不在仓库内：$resultRoot。"
}

# G9 是开发期本地非发布门禁。测试进程保持串行，避免共享 Host 输出、插件部署目录和
# Avalonia Headless 全局资源互相污染。本脚本不读取、初始化或修改 AIFLOW，也不调用 Windows CI、
# Windows Smoke、ReleaseAcceptance、Accept/Approve/Release、发布门禁、签名、上传或标签流程。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$suites = @(
    [pscustomobject]@{
        Name = 'G9-PluginSdk'
        Project = 'Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj'
        HostCoverage = $false
        MyPlugTestCoverage = $false
    },
    [pscustomobject]@{
        Name = 'G9-HostUnit'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
        HostCoverage = $true
        MyPlugTestCoverage = $false
    },
    [pscustomobject]@{
        Name = 'G9-HeadlessUi'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        HostCoverage = $true
        MyPlugTestCoverage = $true
    },
    [pscustomobject]@{
        Name = 'G9-PluginDock'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        HostCoverage = $true
        MyPlugTestCoverage = $true
    },
    [pscustomobject]@{
        Name = 'G9-MyPlugTest'
        Project = 'Plugins\MyPlugTest\MyPlugTest.Tests\MyPlugTest.Tests.csproj'
        HostCoverage = $false
        MyPlugTestCoverage = $true
    }
)

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
    }
}

function Invoke-TestSuite {
    param(
        [Parameter(Mandatory)] $Suite,
        [string] $Filter,
        [bool] $CollectCoverage = $true
    )

    $suiteDirectory = Join-Path $resultRoot $Suite.Name
    New-Item -ItemType Directory -Path $suiteDirectory | Out-Null
    $arguments = @(
        'test', $Suite.Project,
        '-c', $Configuration,
        '-p:SkipPluginDeploy=true',
        '-p:TreatWarningsAsErrors=true',
        '--results-directory', $suiteDirectory,
        '--logger', "trx;LogFileName=$($Suite.Name).trx",
        '--logger', 'console;verbosity=minimal'
    )
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $arguments += @('--filter', $Filter)
    }
    if ($CollectCoverage) { $arguments += '--collect:XPlat Code Coverage' }
    if ($NoRestore) { $arguments += '--no-restore' }
    Invoke-DotNet $arguments

    $trxPath = Get-ChildItem -LiteralPath $suiteDirectory -Recurse `
        -Filter "$($Suite.Name).trx" -File |
        Select-Object -First 1 -ExpandProperty FullName
    Assert-True (-not [string]::IsNullOrWhiteSpace($trxPath)) `
        "$($Suite.Name) 缺少 TRX。"
    [xml]$trx = Get-Content -LiteralPath $trxPath
    $counters = $trx.TestRun.ResultSummary.Counters
    Assert-True (
        [int]$counters.failed -eq 0 -and
        [int]$counters.notExecuted -eq 0 -and
        [int]$counters.executed -eq [int]$counters.passed) `
        "$($Suite.Name) TRX 未做到全部执行、零失败、零跳过。"
    $passed = [int]$counters.passed
    Assert-True ($passed -gt 0) "$($Suite.Name) 没有实际执行测试。"

    $coveragePath = $null
    if ($CollectCoverage) {
        # TRX logger 可能把 collector 附件复制到 In 子目录。只读取 collector 原始文件，
        # 防止同一覆盖率证据被重复合并并虚增权重。
        $coverageFiles = @(Get-ChildItem -LiteralPath $suiteDirectory -Recurse `
            -Filter 'coverage.cobertura.xml' -File | Where-Object {
                $_.FullName -notmatch '[\\/]In[\\/]'
            })
        Assert-True ($coverageFiles.Count -eq 1) `
            "$($Suite.Name) 没有生成唯一 coverage.cobertura.xml。"
        $coveragePath = $coverageFiles[0].FullName
    }
    return [pscustomobject]@{ Passed = $passed; CoveragePath = $coveragePath }
}

function Assert-RgAbsent {
    param(
        [Parameter(Mandatory)] [string]$Pattern,
        [Parameter(Mandatory)] [string[]]$Paths,
        [string[]]$Globs = @('*.cs', '*.csproj'),
        [Parameter(Mandatory)] [string]$Message
    )
    $arguments = @('--quiet', $Pattern) + $Paths
    foreach ($glob in $Globs) { $arguments += @('-g', $glob) }
    & rg @arguments
    if ($LASTEXITCODE -eq 0) { throw $Message }
    if ($LASTEXITCODE -gt 1) { throw "无法执行 G9 结构扫描：$Pattern。" }
}

function Get-FileLineCoverage {
    param([object[]]$Classes, [string]$RelativePath)
    $matching = @($Classes | Where-Object {
        $_.filename.Replace('\', '/').EndsWith(
            $RelativePath, [StringComparison]::OrdinalIgnoreCase)
    })
    Assert-True ($matching.Count -gt 0) "覆盖率报告缺少关键文件：$RelativePath。"
    $lines = @($matching | ForEach-Object { $_.lines.line } |
        Group-Object number | ForEach-Object {
            [pscustomobject]@{
                Covered = @($_.Group | Where-Object { [int]$_.hits -gt 0 }).Count -gt 0
            }
        })
    Assert-True ($lines.Count -gt 0) "覆盖率报告中的关键文件没有可执行行：$RelativePath。"
    [Math]::Round(100 * @($lines | Where-Object Covered).Count / $lines.Count, 2)
}

$suiteSummary = [ordered]@{}
$coveragePaths = @{}
$totalPassed = 0
Push-Location $repositoryRoot
try {
    Invoke-DotNet @('tool', 'restore')
    foreach ($suite in $suites) {
        $result = Invoke-TestSuite `
            -Suite $suite `
            -CollectCoverage ($suite.HostCoverage -or $suite.MyPlugTestCoverage)
        $suiteSummary[$suite.Name] = $result.Passed
        $coveragePaths[$suite.Name] = $result.CoveragePath
        $totalPassed += $result.Passed
    }

    $hostReports = @($suites | Where-Object HostCoverage | ForEach-Object {
        $coveragePaths[$_.Name]
    })
    Assert-True ($hostReports.Count -eq 3) `
        "G9 预期三份 Host 覆盖率报告，实际为 $($hostReports.Count) 份。"
    $hostCoverageRoot = Join-Path $resultRoot 'coverage-host'
    Invoke-DotNet @(
        'reportgenerator', "-reports:$($hostReports -join ';')",
        "-targetdir:$hostCoverageRoot", '-reporttypes:Cobertura;JsonSummary',
        '-assemblyfilters:+MyAvaloniaManagement;-*.Tests',
        '-filefilters:-*/obj/*;-*.g.cs;-*.g.i.cs')
    [xml]$hostCoverage = Get-Content -LiteralPath (Join-Path $hostCoverageRoot 'Cobertura.xml')
    $hostLineCoverage = [Math]::Round(100 * [double]$hostCoverage.coverage.'line-rate', 2)
    $hostBranchCoverage = [Math]::Round(100 * [double]$hostCoverage.coverage.'branch-rate', 2)
    Assert-True ($hostLineCoverage -ge 83.24) `
        "Host 总行覆盖率 $hostLineCoverage% 低于 G0 基线 83.24%。"
    Assert-True ($hostBranchCoverage -ge 68.98) `
        "Host 总分支覆盖率 $hostBranchCoverage% 低于 G0 基线 68.98%。"

    $myPlugReports = @($suites | Where-Object MyPlugTestCoverage | ForEach-Object {
        $coveragePaths[$_.Name]
    })
    Assert-True ($myPlugReports.Count -eq 3) `
        "G9 预期三份 MyPlugTest 覆盖率报告，实际为 $($myPlugReports.Count) 份。"
    $myPlugCoverageRoot = Join-Path $resultRoot 'coverage-my-plug-test'
    Invoke-DotNet @(
        'reportgenerator', "-reports:$($myPlugReports -join ';')",
        "-targetdir:$myPlugCoverageRoot", '-reporttypes:Cobertura;JsonSummary',
        '-assemblyfilters:+MyPlugTest;-*.Tests',
        '-filefilters:-*/obj/*;-*.g.cs;-*.g.i.cs')
    [xml]$myPlugCoverage = Get-Content -LiteralPath `
        (Join-Path $myPlugCoverageRoot 'Cobertura.xml')
    $myPlugClasses = @($myPlugCoverage.coverage.packages.package.classes.class)
    $eventBusCoverage = Get-FileLineCoverage $myPlugClasses 'Messaging/MyPlugTestEventBus.cs'
    $codecCoverage = Get-FileLineCoverage $myPlugClasses `
        'Persistence/TestWelcomeDocumentContentCodec.cs'
    Assert-True ($eventBusCoverage -ge 90.0) `
        "MyPlugTestEventBus.cs 行覆盖率 $eventBusCoverage% 低于 90%。"
    Assert-True ($codecCoverage -ge 90.0) `
        "TestWelcomeDocumentContentCodec.cs 行覆盖率 $codecCoverage% 低于 90%。"

    $pluginRoot = Join-Path $repositoryRoot 'Plugins\MyPlugTest\MyPlugTest'
    Assert-RgAbsent `
        'MyAvaloniaManagementCommon|LegacyPluginContracts|Dock\.Model|IDocumentCreationStrategy|IToolCreationStrategy|IDocumentScopeFactory|DocumentContentSnapshot|ISavableDocument|IDocumentSaveState|Newtonsoft\.Json|LegacyIds|IHostEventBus|HostEventBus|WeakReferenceMessenger|StrongReferenceMessenger|\bIMessenger\b|IServiceProvider' `
        @($pluginRoot) @('*.cs', '*.csproj') `
        'G9 MyPlugTest 生产代码重新出现 Legacy、Dock、旧保存、Host 总线、静态消息器或服务定位器。'

    $activeTestRoots = @(
        (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginTests'),
        (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.UiTests'),
        (Join-Path $repositoryRoot 'scripts\Test-MyPlugTestV3.ps1'))
    # 文档门禁需要保存一个精确的 V2 历史删除例外，因此不能把整个 scripts 目录当作“活动 G9
    # 入口”扫描。这里只扫描两类活动测试和当前专项脚本；其他脚本是否错误引用旧路径，由
    # Test-Documentation.ps1 的当前文档负例与脚本路径校验分别负责。
    # 分段组成旧名字，避免专项脚本为了表达负例而被自己的源码扫描命中。
    $legacyV2Pattern =
        'MyPlugTest' + 'V2(Migration|Ui)Tests|Test-MyPlugTest' +
        'V2|MYAVALONIA_G9_' + 'PACKAGE_ROOT'
    Assert-RgAbsent `
        $legacyV2Pattern `
        $activeTestRoots @('*.cs', '*.ps1') `
        'G9 活动测试或脚本仍保留 MyPlugTest V2 阶段入口。'
    Assert-RgAbsent `
        'DockableLocator[^\r\n]*("Plug"|\["Plug"\])|GetDockable[^\r\n]*"Files"|DockableLocator[^\r\n]*"Files"|\["Files"\]' `
        @(
            (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement'),
            (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.Tests'),
            (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.PluginTests'),
            (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement.UiTests')) `
        @('*.cs') `
        'G9 生产或活动测试重新出现 Files/Plug Dock Locator。'

    $projectPath = Join-Path $pluginRoot 'MyPlugTest.csproj'
    foreach ($requiredReference in
        'MyAvaloniaManagement.PluginSdk.csproj',
        'MyAvaloniaManagement.PluginSdk.UI.csproj') {
        & rg --quiet ([Regex]::Escape($requiredReference)) $projectPath
        Assert-True ($LASTEXITCODE -eq 0) `
            "G9 MyPlugTest 缺少最终 SDK 引用：$requiredReference。"
    }
    Assert-RgAbsent `
        'ManagedPluginUseV2EntryContract|ManagedPluginHostApi|ManagedPluginCommonContract' `
        @($projectPath) @('*.csproj') `
        'G9 MyPlugTest 项目重新出现过渡入口开关或 Host/Common 双区间。'

    $firstPackageRoot = Join-Path $resultRoot 'package-first'
    $secondPackageRoot = Join-Path $resultRoot 'package-second'
    & (Join-Path $PSScriptRoot 'Build-ManagedPluginPackage.ps1') `
        -Project $projectPath -Configuration $Configuration -OutputDirectory $firstPackageRoot
    if ($LASTEXITCODE -ne 0) { throw 'G9 第一次隔离测试 ZIP 构建失败。' }
    & (Join-Path $PSScriptRoot 'Build-ManagedPluginPackage.ps1') `
        -Project $projectPath -Configuration $Configuration -OutputDirectory $secondPackageRoot
    if ($LASTEXITCODE -ne 0) { throw 'G9 第二次隔离测试 ZIP 构建失败。' }

    $firstSidecars = @(Get-ChildItem -LiteralPath $firstPackageRoot `
        -Filter 'MyPlugTest-*-win-x64.manifest.json')
    Assert-True ($firstSidecars.Count -eq 1) `
        "G9 预期唯一机器清单，实际为 $($firstSidecars.Count) 份。"
    $firstSidecar = Get-Content -Raw -LiteralPath $firstSidecars[0].FullName |
        ConvertFrom-Json
    $packageBaseName = $firstSidecars[0].Name -replace '\.manifest\.json$', ''
    $secondSidecarPath = Join-Path $secondPackageRoot "$packageBaseName.manifest.json"
    Assert-True (Test-Path -LiteralPath $secondSidecarPath -PathType Leaf) `
        'G9 第二次隔离构建缺少对应机器清单。'
    $secondSidecar = Get-Content -Raw -LiteralPath $secondSidecarPath | ConvertFrom-Json
    Assert-True ($firstSidecar.archive.sha256 -eq $secondSidecar.archive.sha256) `
        'G9 两次隔离测试 ZIP 的归档摘要不一致。'
    $firstFiles = @($firstSidecar.files | ForEach-Object {
        "$($_.path)|$($_.length)|$($_.sha256)"
    })
    $secondFiles = @($secondSidecar.files | ForEach-Object {
        "$($_.path)|$($_.length)|$($_.sha256)"
    })
    Assert-True (-not (Compare-Object $firstFiles $secondFiles)) `
        'G9 两次隔离测试 ZIP 的文件事实不一致。'
    $forbiddenPackageFiles = @($firstSidecar.files.path | Where-Object {
        $_ -match '(^|/)(?:MyAvaloniaManagement(?:Common|\.PluginSdk(?:\.UI)?)?|Avalonia(?:\.|$)|Dock\.|Newtonsoft\.Json|Microsoft\.Extensions\.).*\.dll$'
    })
    Assert-True ($forbiddenPackageFiles.Count -eq 0) `
        "G9 测试 ZIP 混入宿主共享程序集：$($forbiddenPackageFiles -join ', ')"

    $packageLoadRoot = Join-Path $resultRoot 'package-load'
    Expand-Archive `
        -LiteralPath (Join-Path $firstPackageRoot "$packageBaseName.zip") `
        -DestinationPath $packageLoadRoot
    $manifestPath = Join-Path $packageLoadRoot `
        'Controls\MyPlugTest\plugin.manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    Assert-True (
        [int]$manifest.schemaVersion -eq 2 -and
        $manifest.pluginId -ceq 'myavalonia.plugin.my-plug-test' -and
        $manifest.pluginVersion -ceq '3.0.0' -and
        $manifest.sdk.minInclusive -ceq '3.0.0' -and
        $manifest.sdk.maxExclusive -ceq '4.0.0') `
        'G9 测试 ZIP 的 manifest schema、身份、版本或 V3 SDK 区间不正确。'

    $variableName = 'MYAVALONIA_G9_V3_PACKAGE_ROOT'
    $previousPackageRoot = [Environment]::GetEnvironmentVariable($variableName)
    try {
        [Environment]::SetEnvironmentVariable(
            $variableName, (Join-Path $packageLoadRoot 'Controls'))
        $zipSuite = [pscustomobject]@{
            Name = 'G9-FinalZip'
            Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        }
        $zipResult = Invoke-TestSuite `
            -Suite $zipSuite `
            -Filter 'FullyQualifiedName~G9最终测试Zip通过真实V3发现组合并进入Workspace目录' `
            -CollectCoverage $false
        $suiteSummary[$zipSuite.Name] = $zipResult.Passed
        $totalPassed += $zipResult.Passed
    }
    finally {
        [Environment]::SetEnvironmentVariable($variableName, $previousPackageRoot)
    }

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        suites = $suiteSummary
        passed = $totalPassed
        failed = 0
        skipped = 0
        hostCoverage = [ordered]@{ line = $hostLineCoverage; branch = $hostBranchCoverage }
        myPlugTestCoverage = [ordered]@{
            eventBusLine = $eventBusCoverage
            contentCodecLine = $codecCoverage
        }
        manifest = [ordered]@{
            schemaVersion = [int]$manifest.schemaVersion
            pluginId = $manifest.pluginId
            pluginVersion = $manifest.pluginVersion
            sdkMinInclusive = $manifest.sdk.minInclusive
            sdkMaxExclusive = $manifest.sdk.maxExclusive
        }
        archiveSha256 = $firstSidecar.archive.sha256
        packageFiles = $firstSidecar.files.Count
        deterministicBuilds = 2
        workspaceDocuments = 4
        workspaceTools = 1
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
        ($summary | ConvertTo-Json -Depth 7),
        [Text.UTF8Encoding]::new($false))
    Write-Host (
        "G9 MyPlugTest V3 专项门禁通过：$totalPassed 项；" +
        "Host 行覆盖率 $hostLineCoverage%，分支覆盖率 $hostBranchCoverage%；" +
        "测试 ZIP $($firstSidecar.files.Count) 个文件。")
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
