[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modulePath = Join-Path $PSScriptRoot 'DocumentationGate.Core.psm1'
Import-Module $modulePath -Force

# 当前源码已经完成 V3 G7 Host Catalog / Plugin Registry 分离；其余生产语义仍由 V2 G14
# 签署，G8–G14 的协议设计不能写成已实现。V1/V2/V3 阶段记录继续参加链接、命令和项目路径检查。
$currentDocumentPaths = @(
    'README.md',
    'docs/README.md',
    'docs/design/document-persistence-v2-design.md',
    'docs/design/host-plugin-architecture-review.md',
    'docs/design/host-v2-breaking-refactor-plan.md',
    'docs/design/host-v3-breaking-refactor-plan.md',
    'docs/plan-history/host-v2/g14-v2-sealing.md',
    'docs/plan-history/host-v3/g0-green-baseline.md',
    'docs/plan-history/host-v3/g1-version-and-data-boundaries.md',
    'docs/plan-history/host-v3/g2-revisioned-document-save.md',
    'docs/plan-history/host-v3/g3-exclusive-document-activation.md',
    'docs/plan-history/host-v3/g4-plugin-registration-ownership.md',
    'docs/plan-history/host-v3/g5-plugin-private-messaging.md',
    'docs/plan-history/host-v3/g6-workspace-session-and-dock-factory.md',
    'docs/plan-history/host-v3/g7-host-catalog-and-plugin-registry.md',
    'docs/reference/dock-layout-snapshot-v2.md',
    'docs/reference/myavalonia-management-tests.md',
    'docs/reference/plugin-sdk-api-compatibility.md',
    'Host/MyAvaloniaManagement/docs/README.md',
    'Host/MyAvaloniaManagement/docs/design/architecture.md',
    'Host/MyAvaloniaManagement/docs/design/design-methodology-and-tradeoffs.md',
    'Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md',
    'Host/MyAvaloniaManagement.PluginSdk/README.md',
    'Host/MyAvaloniaManagement.PluginSdk.UI/README.md'
)
$currentDocumentPaths += @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'docs\quick-start') `
        -Filter '*.md' -File | ForEach-Object {
            [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName).Replace('\', '/')
        })
$hostHistoryDirectories = @('host-v1', 'host-v2', 'host-v3')
$historyDocumentPaths = @($hostHistoryDirectories | ForEach-Object {
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "docs\plan-history\$_") `
            -Filter '*.md' -File
    } | ForEach-Object {
        [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName).Replace('\', '/')
    })
$legacyReferencePaths = @(
    'docs/design/host-v1-sealing-readiness-plan.md',
    'docs/design/document-persistence-v1-design.md',
    'docs/reference/dock-layout-snapshot-v1.md'
)
$linkDocumentPaths = @(
    $currentDocumentPaths + $historyDocumentPaths + $legacyReferencePaths |
        Sort-Object -Unique)

# 规则只拦截仍被写成“当前状态”的旧结论。Legacy、旧类型和固定数量在历史审计段落中仍是
# 必要事实，因此不能用全仓库关键词禁令粗暴删除。
$forbiddenStatementRules = @(
    [pscustomobject]@{ Name = '宿主仍待封板'; Pattern = '状态：待整改，不满足封板条件' },
    [pscustomobject]@{ Name = 'G16 仍待完成'; Pattern = '仅\s*G16\s*待完成' },
    [pscustomobject]@{ Name = '导航仍声明未封板'; Pattern = '完成前不得认定宿主已封板' },
    [pscustomobject]@{ Name = '主项目仍把 G8 当作当前基线'; Pattern = '2026-08-18\s+G8\s+基线为' },
    [pscustomobject]@{ Name = 'V2 总任务仍声称只完成 G0-G8'; Pattern = 'V2\s+已完成\s+G0[–-]G8' },
    [pscustomobject]@{ Name = 'V2 当前事实仍声称只完成 G0-G9'; Pattern = '当前.{0,80}V2\s+已完成\s+G0[–-]G9' },
    [pscustomobject]@{ Name = 'V2 当前事实仍声称只完成 G0-G10'; Pattern = '当前.{0,80}V2\s+已完成\s+G0[–-]G10' },
    [pscustomobject]@{ Name = '当前事实仍称 MySmallTools 等待 G11'; Pattern = 'MySmallTools(?:/BiliDownloader|\s*(?:与|和)\s*BiliDownloader).{0,40}(?:等待|留到)\s*G11' },
    [pscustomobject]@{ Name = 'V2 仍停留在 G13'; Pattern = '(?:当前|状态).{0,80}G0[–-]G13' },
    [pscustomobject]@{ Name = 'G14 仍未完成'; Pattern = '仅\s*G14\s*(?:尚未实现|待完成)|G14\s+尚未实现' },
    [pscustomobject]@{ Name = 'V3 状态仍停留在 G0'; Pattern = '状态：实施中；G0 已完成，G1[–-]G14 尚未实施' },
    [pscustomobject]@{ Name = 'V3 当前状态仍停留在 G5'; Pattern = '状态：实施中；G0[–-]G5 已完成，G6[–-]G14 尚未实施' },
    [pscustomobject]@{ Name = 'V3 当前状态仍停留在 G6'; Pattern = '状态：实施中；G0[–-]G6 已完成，G7[–-]G14 尚未实施' },
    [pscustomobject]@{ Name = 'V2 SDK 仍标记未发布'; Pattern = 'V2.{0,80}(?:SDK|契约).{0,40}(?:仍是|尚未).{0,20}未发布' },
    [pscustomobject]@{ Name = 'V2 API 仍为空 Shipped'; Pattern = '(?:v2|V2).{0,50}Shipped\s*(?:均|为)?\s*为空' },
    [pscustomobject]@{ Name = 'BiliDownloader 仍等待 G12'; Pattern = 'BiliDownloader.{0,30}(?:等待|留待)\s*G12' },
    [pscustomobject]@{ Name = '快速开始仍等待 G9 迁移'; Pattern = '快速开始.{0,40}(?:等待|等)\s*G9' },
    [pscustomobject]@{ Name = '保存契约仍未统一'; Pattern = '保存契约尚未统一' },
    [pscustomobject]@{ Name = 'Legacy 仍是并列入口'; Pattern = 'Legacy\s*(?:为|作为).*并列.*(?:方式|入口)' },
    [pscustomobject]@{ Name = 'G16 证据尚未回填'; Pattern = '待(?:执行|最终复跑)' }
)

$requiredSymbols = @(
    [pscustomobject]@{ Symbol = 'IPluginModule'; Path = 'Host/MyAvaloniaManagement.PluginSdk.UI/PluginRegistrationContracts.cs' },
    [pscustomobject]@{ Symbol = 'IPluginRegistration'; Path = 'Host/MyAvaloniaManagement.PluginSdk.UI/PluginRegistrationContracts.cs' },
    [pscustomobject]@{ Symbol = 'IPluginWindowInteraction'; Path = 'Host/MyAvaloniaManagement.PluginSdk.UI/IPluginWindowInteraction.cs' },
    [pscustomobject]@{ Symbol = 'IWindowContentFullscreenHost'; Path = 'Host/MyAvaloniaManagement.PluginSdk.UI/IWindowContentFullscreenHost.cs' },
    [pscustomobject]@{ Symbol = 'DocumentContent'; Path = 'Host/MyAvaloniaManagement.PluginSdk/DocumentContracts.cs' },
    [pscustomobject]@{ Symbol = 'DocumentRevision'; Path = 'Host/MyAvaloniaManagement.PluginSdk/DocumentContracts.cs' },
    [pscustomobject]@{ Symbol = 'DocumentSaveSnapshot'; Path = 'Host/MyAvaloniaManagement.PluginSdk/DocumentContracts.cs' },
    [pscustomobject]@{ Symbol = 'CaptureSaveSnapshotAsync'; Path = 'Host/MyAvaloniaManagement.PluginSdk/DocumentContracts.cs' },
    [pscustomobject]@{ Symbol = 'DocumentActivation'; Path = 'Host/MyAvaloniaManagement.PluginSdk/DocumentContracts.cs' },
    [pscustomobject]@{ Symbol = 'NewDocumentActivation'; Path = 'Host/MyAvaloniaManagement.PluginSdk/DocumentContracts.cs' },
    [pscustomobject]@{ Symbol = 'RestoreDocumentActivation'; Path = 'Host/MyAvaloniaManagement.PluginSdk/DocumentContracts.cs' },
    [pscustomobject]@{ Symbol = 'IMyPlugTestEventBus'; Path = 'Plugins/MyPlugTest/MyPlugTest/Messaging/IMyPlugTestEventBus.cs' },
    [pscustomobject]@{ Symbol = 'IBiliDownloaderEventBus'; Path = 'Plugins/BiliDownloader/BiliDownloader/Messaging/IBiliDownloaderEventBus.cs' },
    [pscustomobject]@{ Symbol = 'PluginServiceCommitGuard'; Path = 'Host/MyAvaloniaManagement/Business/Helpers/PluginServiceCommitGuard.cs' },
    [pscustomobject]@{ Symbol = 'HostDiagnosticRedactionPolicy'; Path = 'Host/MyAvaloniaManagement/Business/Diagnostics/HostDiagnostics.cs' },
    [pscustomobject]@{ Symbol = 'DocumentEnvelopeSerializer'; Path = 'Host/MyAvaloniaManagement/Business/Documents/DocumentEnvelopeSerializer.cs' }
    [pscustomobject]@{ Symbol = 'HostDockFactory'; Path = 'Host/MyAvaloniaManagement/Business/Docking/HostDockFactory.cs' }
    [pscustomobject]@{ Symbol = 'WorkspaceSession'; Path = 'Host/MyAvaloniaManagement/Business/Workspace/WorkspaceSession.cs' }
    [pscustomobject]@{ Symbol = 'ToolWorkspaceReadModel'; Path = 'Host/MyAvaloniaManagement/Business/Workspace/ToolWorkspaceReadModel.cs' }
    [pscustomobject]@{ Symbol = 'HostWorkspaceCatalog'; Path = 'Host/MyAvaloniaManagement/Business/Workspace/HostWorkspaceCatalog.cs' }
    [pscustomobject]@{ Symbol = 'WorkspaceCatalog'; Path = 'Host/MyAvaloniaManagement/Business/Workspace/WorkspaceCatalog.cs' }
    [pscustomobject]@{ Symbol = 'HostWorkspaceActivator'; Path = 'Host/MyAvaloniaManagement/Business/Workspace/HostWorkspaceActivator.cs' }
)
$forbiddenSymbols = @(
    'IDocumentSavePathPolicy',
    'HandledEventsAwareBehavior',
    'IPluginRegistrationContext',
    'MyAvaloniaManagementCommon',
    'LegacyPluginContracts',
    'CaptureContentAsync',
    'IHostEventBus',
    'HostEventBus',
    'DocumentActivationContext',
    'V2Owner',
    'AppendHostContributions'
)
$pluginProjects = @(
    'Plugins/BiliDownloader/BiliDownloader/BiliDownloader.csproj',
    'Plugins/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug.csproj',
    'Plugins/MyPlugTest/MyPlugTest/MyPlugTest.csproj',
    'Plugins/MySmallTools/MySmallTools/MySmallTools.csproj'
)
# G13 按设计整体删除了 Legacy 项目，但 V1 封板任务书必须作为当时已签署的历史证据原样保留。
# 例外同时绑定“历史文档 + 已删除项目”两个精确值，不能掩盖其他文档中的失效项目路径。
$historicallyDeletedProjectReferences = @(
    [pscustomobject]@{
        SourcePath = 'docs/design/host-v1-sealing-readiness-plan.md'
        ProjectPath = 'Host/MyAvaloniaManagement.LegacyPluginContracts/MyAvaloniaManagement.LegacyPluginContracts.csproj'
    }
)

# Git 的路径清单用作大小写事实源。Windows 文件系统本身不区分大小写，单靠 Test-Path 会让
# 在 Linux 克隆中必坏的链接误过门禁；git -c core.quotepath=false 同时保留中文文件名。
$trackedPaths = @(& git -C $repositoryRoot -c core.quotepath=false ls-files `
        --cached --others --exclude-standard)
Assert-DocumentationCondition ($LASTEXITCODE -eq 0) '无法读取 Git 候选路径，文档门禁不能继续。'
$trackedPaths = @($trackedPaths | ForEach-Object { $_.Replace('\', '/') })

$documentsByPath = @{}
foreach ($relativePath in $linkDocumentPaths) {
    $fullPath = Join-Path $repositoryRoot $relativePath
    Assert-DocumentationCondition (Test-Path -LiteralPath $fullPath -PathType Leaf) (
        "G16 文档清单中的文件不存在：$relativePath")
    $documentsByPath[$relativePath] = [pscustomobject]@{
        Path = $relativePath
        Text = [IO.File]::ReadAllText($fullPath)
    }
}

# 最终签署和阶段进度不能只靠“没有旧句子”间接成立。以下正向哨兵把 V2 G14、
# V3 G7 的活动版本/API 状态和非发布边界绑定到权威文档。
$requiredCurrentStatements = @(
    [pscustomobject]@{ Path = 'README.md'; Fragment = 'Managed Plugin V2 已完成 G0–G14 并正式封板' },
    [pscustomobject]@{ Path = 'docs/design/host-v2-breaking-refactor-plan.md'; Fragment = '状态：已完成；G0–G14 已全部封板' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v2/g14-v2-sealing.md'; Fragment = 'scripts/Invoke-HostV2ReleaseGate.ps1' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v2/g14-v2-sealing.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/design/host-v3-breaking-refactor-plan.md'; Fragment = '状态：实施中；G0–G7 已完成，G8–G14 尚未实施' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g0-green-baseline.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g0-green-baseline.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g0-green-baseline.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g0-green-baseline.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g0-green-baseline.md'; Fragment = 'publishable=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g1-version-and-data-boundaries.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g1-version-and-data-boundaries.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g1-version-and-data-boundaries.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g1-version-and-data-boundaries.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g1-version-and-data-boundaries.md'; Fragment = 'publishable=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g2-revisioned-document-save.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g2-revisioned-document-save.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g2-revisioned-document-save.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g2-revisioned-document-save.md'; Fragment = 'releaseAcceptance=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g2-revisioned-document-save.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g2-revisioned-document-save.md'; Fragment = 'publishable=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g3-exclusive-document-activation.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g3-exclusive-document-activation.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g3-exclusive-document-activation.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g3-exclusive-document-activation.md'; Fragment = 'releaseAcceptance=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g3-exclusive-document-activation.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g3-exclusive-document-activation.md'; Fragment = 'publishable=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g4-plugin-registration-ownership.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g4-plugin-registration-ownership.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g4-plugin-registration-ownership.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g4-plugin-registration-ownership.md'; Fragment = 'releaseAcceptance=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g4-plugin-registration-ownership.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g4-plugin-registration-ownership.md'; Fragment = 'publishable=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g5-plugin-private-messaging.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g5-plugin-private-messaging.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g5-plugin-private-messaging.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g5-plugin-private-messaging.md'; Fragment = 'releaseAcceptance=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g5-plugin-private-messaging.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g5-plugin-private-messaging.md'; Fragment = 'publishable=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g6-workspace-session-and-dock-factory.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g6-workspace-session-and-dock-factory.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g6-workspace-session-and-dock-factory.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g6-workspace-session-and-dock-factory.md'; Fragment = 'releaseAcceptance=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g6-workspace-session-and-dock-factory.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g6-workspace-session-and-dock-factory.md'; Fragment = 'publishable=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g7-host-catalog-and-plugin-registry.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g7-host-catalog-and-plugin-registry.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g7-host-catalog-and-plugin-registry.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g7-host-catalog-and-plugin-registry.md'; Fragment = 'releaseAcceptance=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g7-host-catalog-and-plugin-registry.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g7-host-catalog-and-plugin-registry.md'; Fragment = 'publishable=false' },
    [pscustomobject]@{ Path = 'docs/reference/plugin-sdk-api-compatibility.md'; Fragment = 'Core 127 条、UI 46 条' }
)
foreach ($requirement in $requiredCurrentStatements) {
    Assert-DocumentationCondition (
        $documentsByPath[$requirement.Path].Text.Contains(
            $requirement.Fragment,
            [StringComparison]::Ordinal)) (
        "$($requirement.Path) 缺少当前阶段正向事实：$($requirement.Fragment)")
}

# V1 正文必须保持原始证据，所以门禁只要求页首有清晰的取代说明，不对历史数量或命令做替换。
$historicalV1BannerPaths = $legacyReferencePaths +
    @($historyDocumentPaths | Where-Object { $_ -like 'docs/plan-history/host-v1/*' })
foreach ($relativePath in $historicalV1BannerPaths) {
    $text = [IO.File]::ReadAllText((Join-Path $repositoryRoot $relativePath))
    $prefix = if ($text.Length -gt 500) { $text.Substring(0, 500) } else { $text }
    Assert-DocumentationCondition ($prefix -match '历史说明：.*已由.*V2.*取代') (
        "V1 历史文档缺少页首取代说明：$relativePath")
}

$links = [Collections.Generic.List[object]]::new()
$commands = [Collections.Generic.List[object]]::new()
$projects = [Collections.Generic.List[object]]::new()
foreach ($relativePath in $linkDocumentPaths) {
    $document = $documentsByPath[$relativePath]
    foreach ($link in @(Get-DocumentationMarkdownLinks `
            -Text $document.Text -SourcePath $relativePath)) {
        $links.Add($link)
    }
    foreach ($command in @(Get-DocumentationCommandPaths `
            -Text $document.Text -SourcePath $relativePath)) {
        $commands.Add($command)
    }
    foreach ($project in @(Get-DocumentationProjectPaths `
            -Text $document.Text -SourcePath $relativePath)) {
        # QuickStartPlugin 是教程要求读者新建的示例项目，门禁不能把“尚未执行教程”误判为仓库损坏。
        $isHistoricalDeletion = @($historicallyDeletedProjectReferences | Where-Object {
                $_.SourcePath -eq $project.SourcePath -and $_.ProjectPath -eq $project.Path
            }).Count -gt 0
        if (-not $isHistoricalDeletion -and
            -not $project.Path.StartsWith('Plugins/QuickStartPlugin/', [StringComparison]::Ordinal)) {
            $projects.Add($project)
        }
    }
}

$checkedLinks = Assert-DocumentationLinks `
    -RepositoryRoot $repositoryRoot -Links $links.ToArray() -TrackedPaths $trackedPaths
$checkedCommands = Assert-DocumentationCommandPaths `
    -RepositoryRoot $repositoryRoot -Commands $commands.ToArray() -TrackedPaths $trackedPaths
$checkedProjects = Assert-DocumentationProjectPaths `
    -RepositoryRoot $repositoryRoot -Projects $projects.ToArray() -TrackedPaths $trackedPaths
Assert-DocumentationForbiddenStatements `
    -Documents @($currentDocumentPaths | ForEach-Object { $documentsByPath[$_] }) `
    -Rules $forbiddenStatementRules

$productionFiles = @(& git -C $repositoryRoot -c core.quotepath=false ls-files --cached --others --exclude-standard -- `
        'Host/MyAvaloniaManagement.PluginSdk/*.cs' `
        'Host/MyAvaloniaManagement.PluginSdk/**/*.cs' `
        'Host/MyAvaloniaManagement.PluginSdk.UI/*.cs' `
        'Host/MyAvaloniaManagement.PluginSdk.UI/**/*.cs')
Assert-DocumentationCondition ($LASTEXITCODE -eq 0) '无法枚举 Plugin SDK 生产源码。'
Assert-DocumentationSourceSymbols `
    -RepositoryRoot $repositoryRoot `
    -RequiredSymbols $requiredSymbols `
    -ForbiddenSymbols $forbiddenSymbols `
    -ProductionFiles $productionFiles

$baseline = Get-ManagementBaselineFacts `
    -RepositoryRoot $repositoryRoot -PluginProjects $pluginProjects

$summaryRoot = Join-Path $repositoryRoot 'artifacts\test-results\Documentation'
New-Item -ItemType Directory -Path $summaryRoot -Force | Out-Null
$summaryPath = Join-Path $summaryRoot 'summary.json'
$summary = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    documents = $linkDocumentPaths.Count
    currentDocuments = $currentDocumentPaths.Count
    localLinks = $checkedLinks
    commandPaths = $checkedCommands
    projectPaths = $checkedProjects
    productVersion = $baseline.ProductVersion
    sdkVersion = $baseline.SdkVersion
    hostAssemblyVersion = $baseline.HostAssemblyVersion
    sdkAssemblyVersion = $baseline.SdkAssemblyVersion
    apiBaseline = $baseline.ApiBaseline
    shippedApiEntries = $baseline.ShippedEntries
    unshippedApiEntries = $baseline.UnshippedEntries
    apiProjects = $baseline.ApiProjects
    plugins = $baseline.Plugins
    aiflow = $false
    windowsCi = $false
    windowsSmoke = $false
    releaseAcceptance = $false
    releaseGate = $false
    publishable = $false
}
[IO.File]::WriteAllText(
    $summaryPath,
    ($summary | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

Write-Host (
    "[Documentation] 文档门禁通过：文档 $($linkDocumentPaths.Count) 份，" +
    "本地链接 $checkedLinks 个，脚本路径 $checkedCommands 个，项目路径 $checkedProjects 个，" +
    "SDK $($baseline.SdkVersion) / API $($baseline.ApiBaseline)，" +
    "Managed Plugin $($baseline.Plugins.Count) 个。摘要：$summaryPath")
