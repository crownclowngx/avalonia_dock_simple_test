[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$WorkflowStudioRoot,
    [string]$ClassicGameRoot
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($WorkflowStudioRoot)) {
    $WorkflowStudioRoot = Join-Path $repositoryRoot `
        '..\avalonia_management_plug\myavalonia-workflow-studio'
}
if ([string]::IsNullOrWhiteSpace($ClassicGameRoot)) {
    $ClassicGameRoot = Join-Path $repositoryRoot `
        '..\avalonia_management_plug\myavalonia-classic-game'
}
$WorkflowStudioRoot = [IO.Path]::GetFullPath($WorkflowStudioRoot)
$ClassicGameRoot = [IO.Path]::GetFullPath($ClassicGameRoot)
$allowedResultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\test-results'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $allowedResultRoot 'WorkbenchCommandG10'))
$driveRoot = [IO.Path]::GetPathRoot($repositoryRoot)
$allowedScratchRoot = [IO.Path]::GetFullPath((Join-Path $driveRoot 'MyAvalonia-G10-Work'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $allowedScratchRoot 'current'))
Import-Module (Join-Path $PSScriptRoot 'WorkbenchCommandG10Gate.Core.psm1') -Force

function Assert-True {
    param([Parameter(Mandatory)] [bool]$Condition, [Parameter(Mandatory)] [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-PwshChecked {
    param([Parameter(Mandatory)] [string]$Script, [string[]]$Arguments = @())
    & pwsh -NoProfile -File $Script @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Script 失败，退出码：$LASTEXITCODE。"
    }
}

function Invoke-DotnetChecked {
    param([Parameter(Mandatory)] [string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
    }
}

function Read-Json {
    param([Parameter(Mandatory)] [string]$Path, [Parameter(Mandatory)] [string]$Description)
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "$Description 缺少：$Path。"
    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}

function Get-TrxCounts {
    param([Parameter(Mandatory)] [string]$Path)
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "缺少 TRX：$Path。"
    [xml]$trx = Get-Content -Raw -LiteralPath $Path
    $counters = $trx.TestRun.ResultSummary.Counters
    return [ordered]@{
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        skipped = [int]$counters.notExecuted
    }
}

function Assert-LeafNonReleaseFlags {
    param([Parameter(Mandatory)] $Summary, [Parameter(Mandatory)] [string]$Description)
    foreach ($flag in @(
            'aiflow', 'windowsCi', 'windowsSmoke',
            'releaseAcceptance', 'releaseGate', 'publishable')) {
        $property = $Summary.PSObject.Properties[$flag]
        Assert-True ($null -ne $property -and $property.Value -is [bool] -and
            -not [bool]$property.Value) "$Description 的 $flag 必须存在且为 false。"
    }
}

function Get-SourceFact {
    param([Parameter(Mandatory)] [string]$Root)
    $revision = & git -C $Root rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw "无法读取仓库 revision：$Root。" }
    $branch = & git -C $Root branch --show-current
    if ($LASTEXITCODE -ne 0) { throw "无法读取仓库分支：$Root。" }
    $status = @(& git -C $Root status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw "无法读取仓库工作树状态：$Root。" }
    & git -C $Root diff --ignore-space-at-eol --quiet
    $semanticDiffExit = $LASTEXITCODE
    if ($semanticDiffExit -gt 1) { throw "无法检查仓库语义差异：$Root。" }
    $fingerprint = Get-WorkbenchCommandG10WorkspaceFingerprint -RepositoryRoot $Root
    return [ordered]@{
        revision = ([string]$revision).Trim()
        branch = ([string]$branch).Trim()
        changedEntries = $status.Count
        semanticTrackedDiff = $semanticDiffExit -eq 1
        files = [int]$fingerprint.files
        sha256 = [string]$fingerprint.sha256
    }
}

function Get-ApiFact {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [string]$RelativePath,
        [Parameter(Mandatory)] [int]$ExpectedEntries,
        [Parameter(Mandatory)] [string]$ExpectedHash
    )
    $path = Join-Path $Root $RelativePath
    $lines = @(Get-Content -LiteralPath $path)
    Assert-True ($lines.Count -gt 0 -and $lines[0] -ceq '#nullable enable') `
        "API 文件缺少 nullable 头：$RelativePath。"
    $entries = $lines.Count - 1
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    Assert-True ($entries -eq $ExpectedEntries) `
        "API 条目数漂移：$RelativePath，实际 $entries，预期 $ExpectedEntries。"
    Assert-True ($hash -ceq $ExpectedHash) "API SHA-256 漂移：$RelativePath。"
    return [ordered]@{ entries = $entries; sha256 = $hash }
}

function Invoke-RoundStage {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [scriptblock]$Action,
        [Parameter(Mandatory)] [AllowEmptyCollection()]
        [Collections.Generic.List[object]]$States,
        [Parameter(Mandatory)] [string]$StatePath
    )
    $started = [DateTime]::UtcNow
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Write-Host "`n[Workbench Command G10] 开始阶段：$Name"
    try {
        & $Action | Out-Host
        $watch.Stop()
        $States.Add([ordered]@{
            name = $Name; status = 'passed'; startedAtUtc = $started.ToString('O')
            durationMilliseconds = $watch.ElapsedMilliseconds; error = $null
        })
        Write-WorkbenchCommandG10Json -Path $StatePath -Value @($States)
        Write-Host "[Workbench Command G10] 阶段通过：$Name"
    }
    catch {
        $watch.Stop()
        $States.Add([ordered]@{
            name = $Name; status = 'failed'; startedAtUtc = $started.ToString('O')
            durationMilliseconds = $watch.ElapsedMilliseconds; error = $_.Exception.Message
        })
        Write-WorkbenchCommandG10Json -Path $StatePath -Value @($States)
        throw "G10 阶段 '$Name' 失败：$($_.Exception.Message)"
    }
}

function Copy-ExternalPackageInputs {
    param(
        [Parameter(Mandatory)] [string[]]$SourceRoots,
        [Parameter(Mandatory)] [string]$CombinedControls
    )
    New-Item -ItemType Directory -Path $CombinedControls -Force | Out-Null
    foreach ($sourceRoot in $SourceRoots) {
        Assert-True (Test-Path -LiteralPath $sourceRoot -PathType Container) `
            "外部插件 Host 输入目录不存在：$sourceRoot。"
        foreach ($pluginDirectory in Get-ChildItem -LiteralPath $sourceRoot -Directory) {
            $target = Join-Path $CombinedControls $pluginDirectory.Name
            Assert-True (-not (Test-Path -LiteralPath $target)) `
                "两个外部包出现重复目录：$($pluginDirectory.Name)。"
            Copy-Item -LiteralPath $pluginDirectory.FullName -Destination $target -Recurse
        }
    }
    Assert-True (@(Get-ChildItem -LiteralPath $CombinedControls -Directory).Count -eq 2) `
        'G10 组合输入必须恰好包含 WorkflowStudio 与 ClassicGame 两个目录。'
}

function Get-InternalPluginProjection {
    param([Parameter(Mandatory)] $Plugins)
    $projection = [ordered]@{}
    foreach ($property in @($Plugins.PSObject.Properties | Sort-Object Name)) {
        $plugin = $property.Value
        $projection[$property.Name] = [ordered]@{
            passed = [int]$plugin.passed
            failed = [int]$plugin.failed
            skipped = [int]$plugin.skipped
            suites = $plugin.suites
            hostCoverage = $plugin.hostCoverage
            pluginCoverage = $plugin.pluginCoverage
            manifest = $plugin.manifest
            archiveSha256 = [string]$plugin.archiveSha256
            packageFiles = [int]$plugin.packageFiles
            deterministicBuilds = [int]$plugin.deterministicBuilds
            workspaceDocuments = [int]$plugin.workspaceDocuments
            workspaceTools = [int]$plugin.workspaceTools
            harness = if ($null -ne $plugin.harness) {
                [ordered]@{
                    suite = [string]$plugin.harness.suite
                    cycles = [int]$plugin.harness.cycles
                    success = [bool]$plugin.harness.success
                    allFinalResourcesZero = [bool]$plugin.harness.allFinalResourcesZero
                    aliveClosedDocuments = [int]$plugin.harness.aliveClosedDocuments
                    aliveClosedViews = [int]$plugin.harness.aliveClosedViews
                    aliveDisposedEncryptedStreams =
                        [int]$plugin.harness.aliveDisposedEncryptedStreams
                }
            }
            else { $null }
        }
    }
    return $projection
}

function Invoke-G10Round {
    param(
        [Parameter(Mandatory)] [int]$Round,
        [Parameter(Mandatory)] $SourceFacts
    )
    $roundRoot = Join-Path $resultRoot "round-$Round"
    # 深层模板与插件路径在长仓库前缀下会触发 Windows 传统 MAX_PATH。工作副本放在
    # 同盘短暂存根，成功后只把 test-results 证据复制回主仓 artifacts，再安全清理。
    $workspaceRoot = Join-Path $scratchRoot "r$Round"
    $roundCache = Join-Path $workspaceRoot '.cache'
    $statePath = Join-Path $roundRoot 'stage-state.json'
    New-Item -ItemType Directory -Path $roundRoot, $workspaceRoot, $roundCache -Force | Out-Null

    # 叶子门禁会验证独立仓库目录名；短根已经消除了 MAX_PATH，无需再缩写仓库名。
    $hostRoot = Join-Path $workspaceRoot 'avalonia_dock_simple_test'
    $studioRoot = Join-Path $workspaceRoot 'myavalonia-workflow-studio'
    $gameRoot = Join-Path $workspaceRoot 'myavalonia-classic-game'
    $copyFacts = [ordered]@{}
    $copyFacts.host = Copy-WorkbenchCommandG10Workspace -SourceRoot $repositoryRoot `
        -DestinationRoot $hostRoot -AllowedDestinationParent $workspaceRoot
    $copyFacts.workflowStudio = Copy-WorkbenchCommandG10Workspace `
        -SourceRoot $WorkflowStudioRoot -DestinationRoot $studioRoot `
        -AllowedDestinationParent $workspaceRoot
    $copyFacts.classicGame = Copy-WorkbenchCommandG10Workspace `
        -SourceRoot $ClassicGameRoot -DestinationRoot $gameRoot `
        -AllowedDestinationParent $workspaceRoot
    foreach ($name in @('host', 'workflowStudio', 'classicGame')) {
        Assert-True (
            [int]$copyFacts[$name].files -eq [int]$SourceFacts[$name].files -and
            [string]$copyFacts[$name].sha256 -ceq [string]$SourceFacts[$name].sha256) `
            "G10 第 $Round 轮 $name 副本与源工作树指纹不一致。"
    }

    $states = [Collections.Generic.List[object]]::new()
    $previousPackages = $env:NUGET_PACKAGES
    $previousTemp = $env:TEMP
    $previousTmp = $env:TMP
    $previousCliHome = $env:DOTNET_CLI_HOME
    $previousPathMap = $env:PathMap
    $transcriptStarted = $false
    $roundCompleted = $false
    $stableTempLinked = $false
    $physicalTemp = Join-Path $workspaceRoot '.temp'
    # 点号模板名称会形成很深的 Standalone/obj/apphost 路径。稳定入口放在允许暂存根的
    # 单字符子目录为仍受传统 MAX_PATH 影响的 MSBuild Copy 任务留出余量；Junction
    # 只指向本轮自己的物理 TEMP，不会把临时文件写回三个源工作树。
    $stableTempLink = Join-Path $allowedScratchRoot 't'
    try {
        $env:NUGET_PACKAGES = Join-Path $roundCache 'packages'
        # TEMP 与 DOTNET_CLI_HOME 必须位于三个仓库副本之外。若放进主仓 artifacts，
        # 临时生成的消费项目会向上继承主仓 Directory.Packages.props，进而把原本正确的
        # PackageReference 误判为中央包管理违规；这属于门禁环境污染，不是 SDK 缺陷。
        New-Item -ItemType Directory -Path $physicalTemp -Force | Out-Null
        if (Test-Path -LiteralPath $stableTempLink) {
            $existingLink = Get-Item -LiteralPath $stableTempLink -Force
            if (-not ($existingLink.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "G10 稳定 TEMP 名称被普通目录或文件占用：$stableTempLink"
            }
            # 这里只删除 Junction 本身，绝不递归进入它上一轮指向的物理 TEMP。
            [IO.Directory]::Delete($stableTempLink)
        }
        New-Item -ItemType Junction -Path $stableTempLink -Target $physicalTemp | Out-Null
        $stableTempLinked = $true
        $env:TEMP = $stableTempLink
        $env:TMP = $env:TEMP
        $env:DOTNET_CLI_HOME = Join-Path $workspaceRoot '.dotnet-cli-home'
        # 固定 Junction 已为外部打包器提供跨轮稳定的编译路径。G10 主进程不注入全局
        # PathMap：它会把 Cobertura 文件名改成虚拟 `/` 路径，破坏叶子门禁对关键文件的
        # 既有定位规则。清空调用方偶然设置的同名属性，并在 finally 中原样恢复。
        $env:PathMap = $null
        New-Item -ItemType Directory -Path `
            $env:NUGET_PACKAGES, $env:DOTNET_CLI_HOME -Force | Out-Null
        Start-Transcript -Path (Join-Path $roundRoot 'pass.log') -Force | Out-Null
        $transcriptStarted = $true

        Invoke-RoundStage -Name 'core-unit' -States $states -StatePath $statePath -Action {
            Invoke-PwshChecked (Join-Path $hostRoot 'scripts\Test-WorkbenchCommandG10GateCore.ps1')
        }
        Invoke-RoundStage -Name 'workflow-studio' -States $states -StatePath $statePath -Action {
            Invoke-PwshChecked (Join-Path $studioRoot 'scripts\Test-WorkflowStudioG10.ps1') @(
                '-Configuration', $Configuration)
        }
        Invoke-RoundStage -Name 'classic-game' -States $states -StatePath $statePath -Action {
            Invoke-PwshChecked (Join-Path $gameRoot `
                'scripts\Test-ClassicGameWorkbenchCommandG10.ps1') @(
                '-Configuration', $Configuration)
        }
        Invoke-RoundStage -Name 'host-base' -States $states -StatePath $statePath -Action {
            Invoke-PwshChecked (Join-Path $hostRoot 'scripts\Test-HostV4DevelopmentGate.ps1') @(
                '-Stage', 'G7', '-Configuration', $Configuration)
        }
        Invoke-RoundStage -Name 'sdk-template' -States $states -StatePath $statePath -Action {
            Invoke-PwshChecked (Join-Path $hostRoot 'scripts\Test-WorkbenchCommandG6.ps1') @(
                '-Configuration', $Configuration, '-ReuseVerifiedBaseGate',
                '-UsePublishedSdkBaseline')
        }
        Invoke-RoundStage -Name 'host-workflow-studio' -States $states -StatePath $statePath -Action {
            Invoke-PwshChecked (Join-Path $hostRoot 'scripts\Test-WorkbenchCommandG7.ps1') @(
                '-Configuration', $Configuration,
                '-WorkflowStudioRoot', $studioRoot,
                '-ReuseVerifiedBaseGate', '-ReuseVerifiedStudioGate')
        }
        Invoke-RoundStage -Name 'host-classic-game' -States $states -StatePath $statePath -Action {
            Invoke-PwshChecked (Join-Path $hostRoot 'scripts\Test-WorkbenchCommandG8.ps1') @(
                '-Configuration', $Configuration,
                '-ClassicGameRoot', $gameRoot,
                '-ReuseVerifiedBaseGate', '-ReuseVerifiedClassicGameGate')
        }
        Invoke-RoundStage -Name 'command-palette' -States $states -StatePath $statePath -Action {
            Invoke-PwshChecked (Join-Path $hostRoot 'scripts\Test-WorkbenchCommandG9.ps1') @(
                '-Configuration', $Configuration, '-ReuseVerifiedBaseGate')
        }

        $studioG10Path = Join-Path $studioRoot `
            'artifacts\test-results\WorkflowStudioG10\summary.json'
        $gameG10Path = Join-Path $gameRoot `
            'artifacts\test-results\ClassicGameWorkbenchCommandG10\summary.json'
        $studioG10 = Read-Json $studioG10Path 'WorkflowStudio G10 摘要'
        $gameG10 = Read-Json $gameG10Path 'ClassicGame G10 摘要'
        $combinedResultRoot = Join-Path $hostRoot `
            'artifacts\test-results\WorkbenchCommandG10Combined'
        $combinedControls = Join-Path $combinedResultRoot 'Controls'
        Copy-ExternalPackageInputs -SourceRoots @(
            [string]$studioG10.hostInputRoot,
            [string]$gameG10.hostInputRoot) -CombinedControls $combinedControls
        # ScriptBlock 由阶段函数在子作用域调用；用可变持有者传回结果，避免普通变量赋值
        # 被 PowerShell 动态作用域吞掉，直到完整长门禁末尾才出现空摘要。
        $combinedTestsHolder = [ordered]@{ Value = $null }
        Invoke-RoundStage -Name 'combined-real-packages' -States $states `
            -StatePath $statePath -Action {
            $targeted = Join-Path $combinedResultRoot 'targeted'
            New-Item -ItemType Directory -Path $targeted -Force | Out-Null
            $previousCombined = $env:MYAVALONIA_WORKBENCH_COMMAND_G10_EXTERNAL_PLUGIN_ROOT
            try {
                $env:MYAVALONIA_WORKBENCH_COMMAND_G10_EXTERNAL_PLUGIN_ROOT = $combinedControls
                Push-Location $hostRoot
                try {
                    Invoke-DotnetChecked @(
                        'test', 'Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj',
                        '-c', $Configuration, '--no-build', '--no-restore', '-m:1',
                        '--filter', 'FullyQualifiedName~WorkbenchCommandG10CrossRepositoryPackageTests',
                        '--results-directory', $targeted,
                        '--logger', 'trx;LogFileName=WorkbenchCommandG10.Plugin.trx')
                    Invoke-DotnetChecked @(
                        'test', 'Host/MyAvaloniaManagement.UiTests/MyAvaloniaManagement.UiTests.csproj',
                        '-c', $Configuration, '--no-build', '--no-restore', '-m:1',
                        '--filter', 'FullyQualifiedName~WorkbenchCommandG10CrossRepositoryUiTests',
                        '--results-directory', $targeted,
                        '--logger', 'trx;LogFileName=WorkbenchCommandG10.Ui.trx')
                }
                finally { Pop-Location }
            }
            finally {
                $env:MYAVALONIA_WORKBENCH_COMMAND_G10_EXTERNAL_PLUGIN_ROOT = $previousCombined
            }
            $plugin = Get-TrxCounts (Join-Path $targeted 'WorkbenchCommandG10.Plugin.trx')
            $ui = Get-TrxCounts (Join-Path $targeted 'WorkbenchCommandG10.Ui.trx')
            Assert-True ($plugin.passed -eq 1 -and $plugin.failed -eq 0 -and
                $plugin.skipped -eq 0) 'G10 组合真实包 PluginTests 未达到 1/1。'
            Assert-True ($ui.passed -eq 1 -and $ui.failed -eq 0 -and
                $ui.skipped -eq 0) 'G10 组合真实包 Headless UI 未达到 1/1。'
            $combinedTestsHolder.Value = [ordered]@{
                plugin = $plugin; ui = $ui; plugins = 2; documents = 14
                commands = 25; menus = 25; keyBindings = 5
                targetSwitching = $true; subscriptionsReleased = $true
            }
        }
        $combinedTests = $combinedTestsHolder.Value
        Assert-True ($null -ne $combinedTests) 'G10 组合测试阶段没有返回稳定摘要。'
        Invoke-RoundStage -Name 'documentation' -States $states -StatePath $statePath -Action {
            Invoke-PwshChecked (Join-Path $hostRoot 'scripts\Test-Documentation.ps1')
        }

        # PowerShell 变量名不区分大小写，不能使用 $host：它会碰撞只读自动变量 $Host。
        # 摘要变量显式带 Summary 后缀，也让叶子证据与 Host 运行时对象的职责一眼可分。
        $hostSummary = Read-Json (Join-Path $hostRoot `
            'artifacts\test-results\HostV4\G7\summary.json') 'Host G7 摘要'
        $g6 = Read-Json (Join-Path $hostRoot `
            'artifacts\test-results\WorkbenchCommandG6\summary.json') 'Workbench Command G6 摘要'
        $g7 = Read-Json (Join-Path $hostRoot `
            'artifacts\test-results\WorkbenchCommandG7\summary.json') 'Workbench Command G7 摘要'
        $g8 = Read-Json (Join-Path $hostRoot `
            'artifacts\test-results\WorkbenchCommandG8\summary.json') 'Workbench Command G8 摘要'
        $g9 = Read-Json (Join-Path $hostRoot `
            'artifacts\test-results\WorkbenchCommandG9\summary.json') 'Workbench Command G9 摘要'
        $documentation = Read-Json (Join-Path $hostRoot `
            'artifacts\test-results\Documentation\summary.json') 'Host 文档摘要'
        foreach ($leaf in @(
                @{ Summary = $hostSummary; Name = 'Host G7' },
                @{ Summary = $g6; Name = 'Command G6' },
                @{ Summary = $g7; Name = 'Command G7' },
                @{ Summary = $g8; Name = 'Command G8' },
                @{ Summary = $g9; Name = 'Command G9' },
                @{ Summary = $studioG10; Name = 'WorkflowStudio G10' },
                @{ Summary = $gameG10; Name = 'ClassicGame G10' },
                @{ Summary = $documentation; Name = 'Documentation' })) {
            Assert-LeafNonReleaseFlags -Summary $leaf.Summary -Description $leaf.Name
        }
        Assert-True ([int]$hostSummary.hostPassed -ge 584) 'G10 Host 测试数低于 G9 的 584 项基线。'
        Assert-True ([double]$hostSummary.hostLineCoverage -ge 87.32) `
            'G10 Host 行覆盖率低于 G9 的 87.32%。'
        Assert-True ([double]$hostSummary.hostBranchCoverage -ge 72.58) `
            'G10 Host 分支覆盖率低于 G9 的 72.58%。'

        $api = [ordered]@{
            coreShipped = Get-ApiFact $hostRoot `
                'Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v3/PublicAPI.Shipped.txt' `
                127 '063BCB5852827612B0501C135D23FECD015069A6F7DDB409547157E4FA00F80F'
            coreUnshipped = Get-ApiFact $hostRoot `
                'Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v3/PublicAPI.Unshipped.txt' `
                91 '6805C1C131B7420CE1C7A601A06694B1910FA225D6063B38594D6FAF4D1E05EF'
            uiShipped = Get-ApiFact $hostRoot `
                'Host/MyAvaloniaManagement.PluginSdk.UI/ApiCompatibility/v3/PublicAPI.Shipped.txt' `
                45 'B11FBE768C3AD04CA65CBF5128BF6FCE8C00058EBB24052D51FE5464A65AD803'
            uiUnshipped = Get-ApiFact $hostRoot `
                'Host/MyAvaloniaManagement.PluginSdk.UI/ApiCompatibility/v3/PublicAPI.Unshipped.txt' `
                66 'AACE9EF4878E209FABDB1D49DF7657C7DD38A2D54753C1BD5E560CF0272E1FD8'
            workflowShipped = Get-ApiFact $hostRoot `
                'Host/MyAvaloniaManagement.PluginSdk.Workflow/ApiCompatibility/v1/PublicAPI.Shipped.txt' `
                68 '7A3F931E36AEE1F6E135DF8B2CFB16C06CBA947BD585527B1500FD2998F36585'
            workflowUnshipped = Get-ApiFact $hostRoot `
                'Host/MyAvaloniaManagement.PluginSdk.Workflow/ApiCompatibility/v1/PublicAPI.Unshipped.txt' `
                0 '0570CF88EF7BA0638A95F61E904C349C0C00BD34F76241B5EA968CE31482606A'
        }

        $roundSummary = [ordered]@{
            schemaVersion = 1
            stage = 'WorkbenchCommandG10'
            round = $Round
            configuration = $Configuration
            evidencePath = (Join-Path $roundRoot 'summary.json')
            generatedAtUtc = [DateTime]::UtcNow.ToString('O')
            source = $SourceFacts
            api = $api
            versions = [ordered]@{
                product = [string]$g6.productVersion
                sdk = [string]$g6.sdkVersion
                templates = [string]$g6.templateVersion
                workflowSdk = [string]$g6.workflowSdkVersion
                build = [string]$g6.buildVersion
                workflowStudio = [string]$studioG10.manifest.pluginVersion
                classicGame = [string]$gameG10.manifest.pluginVersion
                internalPlugins = Get-InternalPluginProjection $hostSummary.plugins
            }
            schemas = $g6.schema
            host = [ordered]@{
                tests = [int]$hostSummary.hostPassed
                lineCoverage = [double]$hostSummary.hostLineCoverage
                branchCoverage = [double]$hostSummary.hostBranchCoverage
                sdkCompatibility = [bool]$hostSummary.sdkCompatibility
                sdkPackageConsumption = [bool]$hostSummary.sdkPackageConsumption
                diagnosticRedaction = [bool]$hostSummary.diagnosticRedaction
            }
            sdkAndTemplate = [ordered]@{
                tests = $g6.tests
                packages = $g6.packages
                externalPackages = $g6.externalPackages
                standaloneStartupCheck = [bool]$g6.standaloneStartupCheck
                dottedNameSupported = [bool]$g6.dottedNameSupported
            }
            workflowStudio = [ordered]@{
                tests = $studioG10.tests
                lineCoverage = [double]$studioG10.lineCoverage
                branchCoverage = [double]$studioG10.branchCoverage
                mainDocumentLineCoverage = [double]$studioG10.mainDocumentLineCoverage
                archiveSha256 = [string]$studioG10.archiveSha256
                packageFiles = [int]$studioG10.packageFiles
                deterministicBuilds = [int]$studioG10.deterministicBuilds
                manifest = $studioG10.manifest
            }
            classicGame = [ordered]@{
                tests = $gameG10.tests
                lineCoverage = [double]$gameG10.lineCoverage
                branchCoverage = [double]$gameG10.branchCoverage
                gomokuDocumentLineCoverage = [double]$gameG10.gomokuDocumentLineCoverage
                adapterLineCoverage =
                    [double]$gameG10.workbenchDocumentCommandAdapterLineCoverage
                archiveSha256 = [string]$gameG10.archiveSha256
                packageFiles = [int]$gameG10.packageFiles
                deterministicBuilds = [int]$gameG10.deterministicBuilds
                manifest = $gameG10.manifest
            }
            externalHost = [ordered]@{
                g7 = [ordered]@{
                    plugin = $g7.externalPackageTests; ui = $g7.headlessUiTests
                    callerBoundActionInvoked = [bool]$g7.callerBoundActionInvoked
                    twoDocumentsVerified = [bool]$g7.twoStudioDocumentsVerified
                }
                g8 = [ordered]@{
                    plugin = $g8.externalPackageTests; ui = $g8.headlessUiTests
                    thirteenDocumentsVerified = [bool]$g8.thirteenDocumentsVerified
                    catalogCommandsVerified = [int]$g8.catalogCommandsVerified
                    twoDocumentsVerified = [bool]$g8.twoGomokuDocumentsVerified
                }
                g9 = [ordered]@{
                    unit = $g9.unitTargeted; ui = $g9.uiTargeted
                    paletteProjectionLineCoverage = [double]$g9.paletteProjectionLineCoverage
                }
            }
            combinedTests = $combinedTests
            documentation = [ordered]@{
                documents = [int]$documentation.documents
                currentDocuments = [int]$documentation.currentDocuments
                localLinks = [int]$documentation.localLinks
                commandPaths = [int]$documentation.commandPaths
                projectPaths = [int]$documentation.projectPaths
                apiBaseline = [string]$documentation.apiBaseline
                shippedApiEntries = [int]$documentation.shippedApiEntries
                unshippedApiEntries = [int]$documentation.unshippedApiEntries
            }
            passed = $true
            aiflow = $false
            windowsCi = $false
            windowsSmoke = $false
            releaseAcceptance = $false
            releaseGate = $false
            publishable = $false
            published = $false
            uploaded = $false
            signed = $false
            tagCreated = $false
        }
        Write-WorkbenchCommandG10Json -Path $roundSummary.evidencePath -Value $roundSummary
        $evidenceRoot = Join-Path $roundRoot 'evidence'
        New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
        foreach ($evidence in @(
                @{ Name = 'host'; Path = Join-Path $hostRoot 'artifacts\test-results' },
                @{ Name = 'workflow-studio'; Path = Join-Path $studioRoot 'artifacts\test-results' },
                @{ Name = 'classic-game'; Path = Join-Path $gameRoot 'artifacts\test-results' })) {
            Assert-True (Test-Path -LiteralPath $evidence.Path -PathType Container) `
                "G10 缺少待归档的 $($evidence.Name) test-results。"
            Copy-Item -LiteralPath $evidence.Path `
                -Destination (Join-Path $evidenceRoot $evidence.Name) -Recurse
        }
        $roundCompleted = $true
        return $roundSummary
    }
    finally {
        if ($transcriptStarted) { Stop-Transcript | Out-Null }
        $env:NUGET_PACKAGES = $previousPackages
        $env:TEMP = $previousTemp
        $env:TMP = $previousTmp
        $env:DOTNET_CLI_HOME = $previousCliHome
        $env:PathMap = $previousPathMap
        & dotnet build-server shutdown | Out-Null
        if ($stableTempLinked -and (Test-Path -LiteralPath $stableTempLink)) {
            $linkItem = Get-Item -LiteralPath $stableTempLink -Force
            if (-not ($linkItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "G10 拒绝清理已被替换为普通目录的稳定 TEMP：$stableTempLink"
            }
            # Directory.Delete 对 Junction 只删除链接节点，不会删除本轮物理 TEMP 内容。
            [IO.Directory]::Delete($stableTempLink)
        }
        if ($roundCompleted -and (Test-Path -LiteralPath $workspaceRoot)) {
            Remove-WorkbenchCommandG10OwnedTree -Path $workspaceRoot `
                -AllowedParent $scratchRoot -Purpose "G10 第 $Round 轮成功工作副本清理"
        }
    }
}

Assert-WorkbenchCommandG10ChildPath -Candidate $resultRoot -Parent $allowedResultRoot `
    -Purpose 'G10 结果根'
Assert-WorkbenchCommandG10ChildPath -Candidate $scratchRoot -Parent $allowedScratchRoot `
    -Purpose 'G10 短工作根'
Assert-True (Test-Path -LiteralPath $WorkflowStudioRoot -PathType Container) `
    "WorkflowStudio 仓库不存在：$WorkflowStudioRoot。"
Assert-True (Test-Path -LiteralPath $ClassicGameRoot -PathType Container) `
    "ClassicGame 仓库不存在：$ClassicGameRoot。"

# 正式入口只组合本地开发门禁。以下字符串扫描与 Core 单元测试共同防止后续维护把
# AIFLOW、Windows Smoke、Release Gate、上传或 tag 悄悄接入 G10。
$entryText = Get-Content -Raw -LiteralPath $PSCommandPath
foreach ($forbidden in @(
        '(?im)\bgit\s+(?:push|tag)\b',
        '(?im)\b(?:dotnet\s+nuget|nuget)\s+push\b',
        '(?im)\b(?:Invoke|Initialize|Get)-AIFLOW\b',
        '(?im)Invoke-MyAvaloniaManagementWindowsSmoke\.ps1',
        '(?im)Invoke-HostV[0-9]+ReleaseGate\.ps1',
        '(?im)ReleaseAcceptance\.ps1')) {
    Assert-True ($entryText -notmatch $forbidden) `
        "G10 正式入口包含禁止调用：$forbidden。"
}

if (Test-Path -LiteralPath $resultRoot) {
    Remove-WorkbenchCommandG10OwnedTree -Path $resultRoot -AllowedParent $allowedResultRoot
}
if (Test-Path -LiteralPath $scratchRoot) {
    Remove-WorkbenchCommandG10OwnedTree -Path $scratchRoot -AllowedParent $allowedScratchRoot `
        -Purpose 'G10 旧短工作根清理'
}
New-Item -ItemType Directory -Path $resultRoot, $scratchRoot -Force | Out-Null

$sourceFacts = [ordered]@{
    host = Get-SourceFact $repositoryRoot
    workflowStudio = Get-SourceFact $WorkflowStudioRoot
    classicGame = Get-SourceFact $ClassicGameRoot
}
Write-WorkbenchCommandG10Json -Path (Join-Path $resultRoot 'source.json') -Value $sourceFacts

$first = Invoke-G10Round -Round 1 -SourceFacts $sourceFacts
$summary = Complete-WorkbenchCommandG10SingleRoundSealing -Evidence $first `
    -OutputPath (Join-Path $resultRoot 'summary.json')
Write-Host (
    "Workbench Command G10 单轮完整本地封板通过：Host $($summary.evidence.host.tests) 项，" +
    "WorkflowStudio $($summary.evidence.workflowStudio.tests.passed) 项，" +
    "ClassicGame $($summary.evidence.classicGame.tests.passed) 项；publishable=false。")
