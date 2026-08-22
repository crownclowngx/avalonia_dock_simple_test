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
    'artifacts\test-results\PluginPrivateMessaging'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + `
    [IO.Path]::DirectorySeparatorChar
if (-not $resultRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "G5 结果目录不在仓库内：$resultRoot。"
}

# G5 是本地开发阶段门禁。本脚本只运行源码、单元测试和临时编译负例；不会读取或初始化
# AIFLOW，也不会调用 Windows CI/Smoke、ReleaseAcceptance、发布门禁、签名、上传或标签操作。
if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$suites = @(
    [pscustomobject]@{
        Name = 'G5-MyPlugTestBus'
        Project = 'Plugins\MyPlugTest\MyPlugTest.Tests\MyPlugTest.Tests.csproj'
        Filter = 'FullyQualifiedName~MyPlugTestEventBusTests'
        CoverageFile = 'Messaging/MyPlugTestEventBus.cs'
    },
    [pscustomobject]@{
        Name = 'G5-BiliDownloaderBus'
        Project = 'Plugins\BiliDownloader\BiliDownloader.Tests\BiliDownloader.Tests.csproj'
        Filter = 'FullyQualifiedName~BiliDownloaderEventBusTests'
        CoverageFile = 'Messaging/BiliDownloaderEventBus.cs'
    },
    [pscustomobject]@{
        Name = 'G5-HostContracts'
        Project = 'Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj'
        Filter = 'FullyQualifiedName~PublicApiContractTests|FullyQualifiedName~InternalRefactorTests|FullyQualifiedName~PluginRegistrationOwnershipTests'
        CoverageFile = $null
    },
    [pscustomobject]@{
        Name = 'G5-PluginIsolation'
        Project = 'Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj'
        Filter = 'FullyQualifiedName~PluginContainerIsolationTests|FullyQualifiedName~MyPlugTestV2MigrationTests'
        CoverageFile = $null
    },
    [pscustomobject]@{
        Name = 'G5-HeadlessUi'
        Project = 'Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj'
        Filter = 'FullyQualifiedName~MyPlugTestV2UiTests|FullyQualifiedName~BiliDownloaderDocumentVisualTests'
        CoverageFile = $null
    },
    [pscustomobject]@{
        Name = 'G5-BiliMessages'
        Project = 'Plugins\BiliDownloader\BiliDownloader.Tests\BiliDownloader.Tests.csproj'
        Filter = 'FullyQualifiedName~BiliDownloaderModuleTests|FullyQualifiedName~BiliLoginStateServiceTests|FullyQualifiedName~ExtrasAndProgressTests|FullyQualifiedName~DocumentV3G4Tests|FullyQualifiedName~BiliDownloadCoordinatorTests'
        CoverageFile = $null
    }
)

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$suiteSummary = [ordered]@{}
$coverageSummary = [ordered]@{}
$totalPassed = 0
$negativeCompileRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'G5-NoHostEventBus-' + [Guid]::NewGuid().ToString('N'))

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
        if ($suite.CoverageFile) {
            $arguments += '--collect:XPlat Code Coverage'
        }
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

        if ($suite.CoverageFile) {
            # TRX logger 会把 collector 附件再复制到 In 目录；两份内容相同，优先读取 collector
            # 原始输出，避免把同一覆盖率文件误判为两次采集。
            $coveragePaths = @(Get-ChildItem -LiteralPath $suiteDirectory -Recurse `
                -Filter 'coverage.cobertura.xml' -File | Where-Object {
                    $_.FullName -notmatch '[\\/]In[\\/]'
                })
            Assert-True ($coveragePaths.Count -eq 1) `
                "$($suite.Name) 没有生成唯一 collector coverage.cobertura.xml。"
            [xml]$coverage = Get-Content -LiteralPath $coveragePaths[0].FullName
            $class = @($coverage.coverage.packages.package.classes.class | Where-Object {
                    $_.filename.Replace('\', '/').EndsWith(
                        $suite.CoverageFile,
                        [StringComparison]::OrdinalIgnoreCase)
                })
            # Cobertura 会把嵌套 Subscription 作为第二个 class 记录，但二者仍指向同一源码文件。
            # 取该文件所有 class 的最低行覆盖率，确保外层总线和令牌实现都达到门槛。
            Assert-True ($class.Count -ge 1) `
                "$($suite.Name) 无法定位重点消息器覆盖率：$($suite.CoverageFile)。"
            $linePercent = [Math]::Round([double](@($class | ForEach-Object {
                        [double]$_.'line-rate' * 100
                    }) | Measure-Object -Minimum).Minimum, 2)
            Assert-True ($linePercent -ge 90.0) `
                "$($suite.CoverageFile) 行覆盖率 $linePercent% 低于 90%。"
            $coverageSummary[$suite.CoverageFile] = $linePercent
        }
    }

    # 当前生产面必须完全删除 Host 总线；反射负例字符串只存在于测试，不参与生产源码扫描。
    $productionRoots = @(
        'Host\MyAvaloniaManagement.PluginSdk',
        'Host\MyAvaloniaManagement',
        'Plugins\MyPlugTest\MyPlugTest',
        'Plugins\BiliDownloader\BiliDownloader'
    )
    & rg --quiet 'IHostEventBus|HostEventBus' @productionRoots -g '*.cs'
    Assert-True ($LASTEXITCODE -eq 1) 'G5 生产源码重新出现 Host 通用事件总线。'
    Assert-True (-not (Test-Path -LiteralPath `
        'Host\MyAvaloniaManagement\Business\Events\HostEventBus.cs')) `
        'HostEventBus.cs 仍存在。'

    $v3Api = Get-Content -Raw -LiteralPath `
        'Host\MyAvaloniaManagement.PluginSdk\ApiCompatibility\v3\PublicAPI.Unshipped.txt'
    $v2Api = Get-Content -Raw -LiteralPath `
        'Host\MyAvaloniaManagement.PluginSdk\ApiCompatibility\v2\PublicAPI.Shipped.txt'
    Assert-True (-not $v3Api.Contains('IHostEventBus', [StringComparison]::Ordinal)) `
        'v3 Unshipped 仍包含 IHostEventBus。'
    Assert-True ($v2Api.Contains('IHostEventBus', [StringComparison]::Ordinal)) `
        'v2 历史 API 基线被意外改写。'

    $myPlugModule = Get-Content -Raw -LiteralPath `
        'Plugins\MyPlugTest\MyPlugTest\Plugin\MyPlugTestPluginModule.cs'
    $biliModule = Get-Content -Raw -LiteralPath `
        'Plugins\BiliDownloader\BiliDownloader\Plugin\BiliDownloaderPluginModule.cs'
    Assert-True ($myPlugModule.Contains(
        'AddSingleton<IMyPlugTestEventBus, MyPlugTestEventBus>',
        [StringComparison]::Ordinal)) 'MyPlugTest 没有注册独占消息器。'
    Assert-True ($biliModule.Contains(
        'AddSingleton<IBiliDownloaderEventBus, BiliDownloaderEventBus>',
        [StringComparison]::Ordinal)) 'BiliDownloader 没有注册独占消息器。'

    # 独立消费项目只引用当前 v3 Core SDK；旧 Host 总线必须在编译阶段不可见。
    New-Item -ItemType Directory -Path $negativeCompileRoot | Out-Null
    $sdkProject = [Security.SecurityElement]::Escape((Join-Path $repositoryRoot `
        'Host\MyAvaloniaManagement.PluginSdk\MyAvaloniaManagement.PluginSdk.csproj'))
    [IO.File]::WriteAllText(
        (Join-Path $negativeCompileRoot 'RemovedApi.csproj'),
        "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=`"$sdkProject`" /></ItemGroup></Project>",
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $negativeCompileRoot 'RemovedApi.cs'),
        'using MyAvaloniaManagement.PluginSdk; public sealed class RemovedApi(IHostEventBus bus) { }',
        [Text.UTF8Encoding]::new($false))
    $negativeOutput = & dotnet build (Join-Path $negativeCompileRoot 'RemovedApi.csproj') `
        -c $Configuration --nologo 2>&1 | Out-String
    Assert-True ($LASTEXITCODE -ne 0 -and $negativeOutput.Contains(
        'IHostEventBus', [StringComparison]::Ordinal)) `
        '使用 v3 SDK 的 IHostEventBus 编译负例没有按预期失败。'

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        suites = $suiteSummary
        coverage = $coverageSummary
        passed = $totalPassed
        failed = 0
        removedApiNegativeCompile = $true
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
    Write-Host "G5 插件私有消息专项门禁通过：$totalPassed 项。"
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
        [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedNegativeRoot = [IO.Path]::GetFullPath($negativeCompileRoot)
    if ($resolvedNegativeRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedNegativeRoot)) {
        Remove-Item -LiteralPath $resolvedNegativeRoot -Recurse -Force
    }
}
