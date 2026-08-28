[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modulePath = Join-Path $PSScriptRoot 'DocumentationGate.Core.psm1'
Import-Module $modulePath -Force

# 当前源码在 V3 G14 与 Host V4 G8 封板后完成 Workflow Action G1–G4。G3.1 新增 Workflow SDK、
# Studio v2 和静态引用安全，G4 增加首个真实非破坏性业务 Action；历史发布事实继续独立保留。
$currentDocumentPaths = @(
    'README.md',
    'docs/README.md',
    'docs/design/document-persistence-v2-design.md',
    'docs/design/ai-workflow-plugin-exploration.md',
    'docs/design/external-managed-plugin-development-and-installation-plan.md',
    'docs/design/host-plugin-architecture-review.md',
    'docs/design/host-v2-breaking-refactor-plan.md',
    'docs/design/host-v3-breaking-refactor-plan.md',
    'docs/design/host-v4-breaking-refactor-plan.md',
    'docs/design/workbench-command-introduction-plan.md',
    'docs/plan-history/host-v4/g8-v4-sealing.md',
    'docs/plan-history/host-v2/g14-v2-sealing.md',
    'docs/plan-history/host-v3/g0-green-baseline.md',
    'docs/plan-history/host-v3/g1-version-and-data-boundaries.md',
    'docs/plan-history/host-v3/g2-revisioned-document-save.md',
    'docs/plan-history/host-v3/g3-exclusive-document-activation.md',
    'docs/plan-history/host-v3/g4-plugin-registration-ownership.md',
    'docs/plan-history/host-v3/g5-plugin-private-messaging.md',
    'docs/plan-history/host-v3/g6-workspace-session-and-dock-factory.md',
    'docs/plan-history/host-v3/g7-host-catalog-and-plugin-registry.md',
    'docs/plan-history/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md',
    'docs/plan-history/host-v3/g9-my-plug-test-v3-acceptance.md',
    'docs/plan-history/host-v3/g10-datang-accounting-help-v3-acceptance.md',
    'docs/plan-history/host-v3/g11-my-small-tools-v3-acceptance.md',
    'docs/plan-history/host-v3/g12-bili-downloader-v3-acceptance.md',
    'docs/plan-history/host-v3/g13-remove-v2-production-surface.md',
    'docs/plan-history/host-v3/g14-v3-sealing.md',
    'docs/reference/dock-layout-snapshot-v2.md',
    'docs/reference/myavalonia-management-tests.md',
    'docs/reference/plugin-sdk-api-compatibility.md',
    'docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md',
    'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md',
    'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md',
    'docs/plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md',
    'docs/plan-history/workflow-action/g3.1-workflow-protocol-consistency.md',
    'Host/MyAvaloniaManagement/docs/README.md',
    'Host/MyAvaloniaManagement/docs/design/architecture.md',
    'Host/MyAvaloniaManagement/docs/design/design-methodology-and-tradeoffs.md',
    'Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md',
    'Host/MyAvaloniaManagement.PluginSdk/README.md',
    'Host/MyAvaloniaManagement.PluginSdk.UI/README.md',
    'Plugins/BiliDownloader/BiliDownloader.Tests/TESTING.md',
    'Plugins/BiliDownloader/BiliDownloader/doc/reference/PRODUCT.md'
)
$currentDocumentPaths += @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'docs\quick-start') `
        -Filter '*.md' -File | ForEach-Object {
            [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName).Replace('\', '/')
        })
$hostHistoryDirectories = @(
    'host-v1',
    'host-v2',
    'host-v3',
    'host-v4',
    'workflow-action',
    'workbench-command')
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
    [pscustomobject]@{ Name = 'V2 仍停留在 G13'; Pattern = '(?:当前|状态).{0,80}V2.{0,80}G0[–-]G13|V2.{0,80}(?:当前|状态).{0,80}G0[–-]G13' },
    [pscustomobject]@{ Name = 'G14 仍未完成'; Pattern = '仅\s*G14\s*(?:尚未实现|待完成)|G14\s+尚未实现' },
    [pscustomobject]@{ Name = 'V3 状态仍停留在 G0'; Pattern = '状态：实施中；G0 已完成，G1[–-]G14 尚未实施' },
    [pscustomobject]@{ Name = 'V3 当前状态仍停留在 G5'; Pattern = '状态：实施中；G0[–-]G5 已完成，G6[–-]G14 尚未实施' },
    [pscustomobject]@{ Name = 'V3 当前状态仍停留在 G6'; Pattern = '状态：实施中；G0[–-]G6 已完成，G7[–-]G14 尚未实施' },
    [pscustomobject]@{ Name = 'V3 当前状态仍停留在 G7'; Pattern = '状态：实施中；G0[–-]G7 已完成，G8[–-]G14 尚未实施' },
    [pscustomobject]@{ Name = 'V3 当前状态仍停留在 G8'; Pattern = '状态：实施中；G0[–-]G8 已完成，G9[–-]G14 尚未实施' },
    [pscustomobject]@{ Name = 'V3 当前状态仍停留在 G9'; Pattern = '状态：实施中；G0[–-]G9 已完成，G10[–-]G14 尚未实施' },
    [pscustomobject]@{ Name = 'V3 当前状态仍停留在 G12'; Pattern = '状态：实施中；G0[–-]G12 已完成，G13[–-]G14 尚未实施' },
    [pscustomobject]@{ Name = 'G13 仍被声明为未实施'; Pattern = 'G13[–-]G14 尚未实施|G13 删除面.{0,30}仍由后续阶段负责' },
    [pscustomobject]@{ Name = '当前文档仍引用已删除的 MyPlugTest V2 门禁'; Pattern = 'Test-MyPlugTestV2\.ps1' },
    [pscustomobject]@{ Name = '当前文档仍引用已删除的 DaTang V2 门禁'; Pattern = 'Test-DaTangAccountingHelpPlugV2\.ps1' },
    [pscustomobject]@{ Name = '当前文档仍引用已删除的 MySmallTools V2 门禁'; Pattern = 'Test-MySmallToolsV2\.ps1' },
    [pscustomobject]@{ Name = '当前文档仍引用已删除的 BiliDownloader V2 门禁'; Pattern = 'Test-BiliDownloaderV2\.ps1' },
    [pscustomobject]@{ Name = 'V2 SDK 仍标记未发布'; Pattern = 'V2.{0,80}(?:SDK|契约).{0,40}(?:仍是|尚未).{0,20}未发布' },
    [pscustomobject]@{ Name = 'V2 API 仍为空 Shipped'; Pattern = '(?:v2|V2).{0,50}Shipped\s*(?:均|为)?\s*为空' },
    [pscustomobject]@{ Name = 'BiliDownloader 仍等待 G12'; Pattern = 'BiliDownloader.{0,30}(?:等待|留待)\s*G12' },
    [pscustomobject]@{ Name = '快速开始仍等待 G9 迁移'; Pattern = '快速开始.{0,40}(?:等待|等)\s*G9' },
    [pscustomobject]@{ Name = '保存契约仍未统一'; Pattern = '保存契约尚未统一' },
    [pscustomobject]@{ Name = 'Legacy 仍是并列入口'; Pattern = 'Legacy\s*(?:为|作为).*并列.*(?:方式|入口)' },
    [pscustomobject]@{ Name = 'G16 证据尚未回填'; Pattern = 'G16.{0,80}待(?:执行|最终复跑)|待最终复跑' },
    [pscustomobject]@{ Name = 'V4 当前状态仍停留在 G6'; Pattern = 'Host V4 当前状态：G0[–-]G6 已完成，G7[–-]G8 待实施' },
    [pscustomobject]@{ Name = 'V4 当前状态仍停留在 G7'; Pattern = '(?:状态：实施中；G0[–-]G7 已完成，G8 待实施|Host V4 当前状态：G0[–-]G7 已完成，G8 待实施)' },
    [pscustomobject]@{ Name = 'V4 当前文档仍声明 G8 未实施'; Pattern = 'V4 G8 尚未实施|G8 尚未实施，因此 V4' },
    [pscustomobject]@{ Name = 'Workflow Action 当前状态仍停留在 G2'; Pattern = '文档状态：G0 已重新签署、G1[–-]G2 已完成；G3[–-]G10 尚未实施' }
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
    [pscustomobject]@{ Symbol = 'PluginServiceCommitGuard'; Path = 'Host/MyAvaloniaManagement/Business/Plugins/Registration/PluginServiceCommitGuard.cs' },
    [pscustomobject]@{ Symbol = 'HostDiagnosticRedactionPolicy'; Path = 'Host/MyAvaloniaManagement/Business/Diagnostics/HostDiagnostics.cs' },
    [pscustomobject]@{ Symbol = 'DocumentEnvelopeSerializer'; Path = 'Host/MyAvaloniaManagement/Business/Documents/DocumentEnvelopeSerializer.cs' }
    [pscustomobject]@{ Symbol = 'IWorkflowActionRun'; Path = 'Host/MyAvaloniaManagement.PluginSdk/WorkflowActionContracts.cs' }
    [pscustomobject]@{ Symbol = 'IWorkflowActionRegistration'; Path = 'Host/MyAvaloniaManagement.PluginSdk.UI/WorkflowActionRegistrationContracts.cs' }
    [pscustomobject]@{ Symbol = 'WorkflowActionCatalogStore'; Path = 'Host/MyAvaloniaManagement/Business/WorkflowActions/WorkflowActionCatalogStore.cs' }
    [pscustomobject]@{ Symbol = 'WorkflowActionShutdownGate'; Path = 'Host/MyAvaloniaManagement/Business/WorkflowActions/WorkflowActionShutdownGate.cs' }
    [pscustomobject]@{ Symbol = 'HostDockFactory'; Path = 'Host/MyAvaloniaManagement/Business/Docking/HostDockFactory.cs' }
    [pscustomobject]@{ Symbol = 'WorkspaceSession'; Path = 'Host/MyAvaloniaManagement/Business/Workspace/WorkspaceSession.cs' }
    [pscustomobject]@{ Symbol = 'ToolWorkspaceReadModel'; Path = 'Host/MyAvaloniaManagement/Business/Workspace/ToolWorkspaceReadModel.cs' }
    [pscustomobject]@{ Symbol = 'HostWorkspaceCatalog'; Path = 'Host/MyAvaloniaManagement/Business/Workspace/HostWorkspaceCatalog.cs' }
    [pscustomobject]@{ Symbol = 'WorkspaceCatalog'; Path = 'Host/MyAvaloniaManagement/Business/Workspace/WorkspaceCatalog.cs' }
    [pscustomobject]@{ Symbol = 'HostWorkspaceActivator'; Path = 'Host/MyAvaloniaManagement/Business/Workspace/HostWorkspaceActivator.cs' }
    [pscustomobject]@{ Symbol = 'WindowContentFullscreenSession'; Path = 'Host/MyAvaloniaManagement/Business/Presentation/WindowContentFullscreenSession.cs' }
    [pscustomobject]@{ Symbol = 'FileSystemPath'; Path = 'Host/MyAvaloniaManagement/Models/FileSystem/FileSystemPath.cs' }
    [pscustomobject]@{ Symbol = 'DirectoryExists'; Path = 'Host/MyAvaloniaManagement/Business/Storage/IHostStorageService.cs' }
    [pscustomobject]@{ Symbol = 'PluginDeploymentConstants'; Path = 'Host/MyAvaloniaManagement/Business/Constants/PluginDeploymentConstants.cs' }
    [pscustomobject]@{ Symbol = 'CategoryNode'; Path = 'Host/MyAvaloniaManagement/Models/Tools/CategoryNode.cs' }
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
    'AppendHostContributions',
    'TryRestore'
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
# V3 G9–G12 删除了四插件活动 V2 专项入口，但对应 V2 记录中的命令是当时真实执行证据。
# 每条例外同时绑定一份历史文档与一条已删除脚本，不能掩盖其他文档中的失效命令。
$historicallyDeletedCommandReferences = @(
    [pscustomobject]@{
        SourcePath = 'docs/plan-history/host-v2/g9-my-plug-test-v2.md'
        CommandPath = 'scripts/Test-MyPlugTestV2.ps1'
    },
    [pscustomobject]@{
        SourcePath = 'docs/plan-history/host-v2/g10-datang-accounting-help-v2.md'
        CommandPath = 'scripts/Test-DaTangAccountingHelpPlugV2.ps1'
    },
    [pscustomobject]@{
        SourcePath = 'docs/plan-history/host-v2/g11-my-small-tools-v2.md'
        CommandPath = 'scripts/Test-MySmallToolsV2.ps1'
    },
    [pscustomobject]@{
        SourcePath = 'docs/plan-history/host-v2/g12-bili-downloader-v2.md'
        CommandPath = 'scripts/Test-BiliDownloaderV2.ps1'
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

# 最终签署和阶段进度不能只靠“没有旧句子”间接成立。以下正向哨兵把 V2/V3 G14、
# V4 G7 历史非发布事实、V4 G8 本地发布资格、正式 API 状态和无外部发布边界绑定到权威文档。
$requiredCurrentStatements = @(
    [pscustomobject]@{ Path = 'README.md'; Fragment = 'Managed Plugin V2 已完成 G0–G14 并正式封板' },
    [pscustomobject]@{ Path = 'docs/design/host-v2-breaking-refactor-plan.md'; Fragment = '状态：已完成；G0–G14 已全部封板' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v2/g14-v2-sealing.md'; Fragment = 'scripts/Invoke-HostV2ReleaseGate.ps1' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v2/g14-v2-sealing.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/design/host-v3-breaking-refactor-plan.md'; Fragment = '状态：已完成；G0–G14 已全部封板' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g14-v3-sealing.md'; Fragment = 'scripts/Invoke-HostV3ReleaseGate.ps1' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g14-v3-sealing.md'; Fragment = 'Core 127、UI 45' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g14-v3-sealing.md'; Fragment = 'repeatabilityVerified=true' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g14-v3-sealing.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g14-v3-sealing.md'; Fragment = 'published=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g9-my-plug-test-v3-acceptance.md'; Fragment = 'Test-MyPlugTestV3.ps1' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g9-my-plug-test-v3-acceptance.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g9-my-plug-test-v3-acceptance.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g9-my-plug-test-v3-acceptance.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g9-my-plug-test-v3-acceptance.md'; Fragment = 'releaseAcceptance=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g9-my-plug-test-v3-acceptance.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g9-my-plug-test-v3-acceptance.md'; Fragment = 'publishable=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g10-datang-accounting-help-v3-acceptance.md'; Fragment = 'Test-DaTangAccountingHelpPlugV3.ps1' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g10-datang-accounting-help-v3-acceptance.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g10-datang-accounting-help-v3-acceptance.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g10-datang-accounting-help-v3-acceptance.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g10-datang-accounting-help-v3-acceptance.md'; Fragment = 'releaseAcceptance=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g10-datang-accounting-help-v3-acceptance.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g10-datang-accounting-help-v3-acceptance.md'; Fragment = 'publishable=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g11-my-small-tools-v3-acceptance.md'; Fragment = 'Test-MySmallToolsV3.ps1' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g11-my-small-tools-v3-acceptance.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g11-my-small-tools-v3-acceptance.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g11-my-small-tools-v3-acceptance.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g11-my-small-tools-v3-acceptance.md'; Fragment = 'releaseAcceptance=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g11-my-small-tools-v3-acceptance.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g11-my-small-tools-v3-acceptance.md'; Fragment = 'publishable=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g12-bili-downloader-v3-acceptance.md'; Fragment = 'Test-BiliDownloaderV3.ps1' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g12-bili-downloader-v3-acceptance.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g12-bili-downloader-v3-acceptance.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g12-bili-downloader-v3-acceptance.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g12-bili-downloader-v3-acceptance.md'; Fragment = 'releaseAcceptance=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g12-bili-downloader-v3-acceptance.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g12-bili-downloader-v3-acceptance.md'; Fragment = 'publishable=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g13-remove-v2-production-surface.md'; Fragment = 'Test-HostV3ProductionSurface.ps1' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g13-remove-v2-production-surface.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g13-remove-v2-production-surface.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g13-remove-v2-production-surface.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g13-remove-v2-production-surface.md'; Fragment = 'releaseAcceptance=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g13-remove-v2-production-surface.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g13-remove-v2-production-surface.md'; Fragment = 'publishable=false' },
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
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md'; Fragment = 'windowsCi=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md'; Fragment = 'releaseAcceptance=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md'; Fragment = 'publishable=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md'; Fragment = 'IDisposable? TryPresent(Control content)' },
    [pscustomobject]@{ Path = 'docs/design/host-v4-breaking-refactor-plan.md'; Fragment = '状态：已完成；G0–G8 已全部封板' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g7-four-plugins-harness-documentation-regression.md'; Fragment = 'Host 合计 | **478/478**' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g7-four-plugins-harness-documentation-regression.md'; Fragment = 'MySmallTools | **709/709**' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g7-four-plugins-harness-documentation-regression.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g7-four-plugins-harness-documentation-regression.md'; Fragment = 'windowsSmoke=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g7-four-plugins-harness-documentation-regression.md'; Fragment = 'releaseGate=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g7-four-plugins-harness-documentation-regression.md'; Fragment = 'V4 尚未封板、尚不可发布' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g8-v4-sealing.md'; Fragment = 'scripts/Invoke-HostV4ReleaseGate.ps1' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g8-v4-sealing.md'; Fragment = 'Core 127 / UI 45，Unshipped 0 / 0' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g8-v4-sealing.md'; Fragment = 'Host 合计 | **478/478**' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g8-v4-sealing.md'; Fragment = 'MySmallTools 专项 | **713/713** | 73.20% | 49.28%' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g8-v4-sealing.md'; Fragment = 'BiliDownloader 专项 | **1246/1246** | 83.87% | 67.86%' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g8-v4-sealing.md'; Fragment = 'releaseEligible=true' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g8-v4-sealing.md'; Fragment = 'publishable=true' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g8-v4-sealing.md'; Fragment = 'published=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g8-v4-sealing.md'; Fragment = 'uploaded=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g8-v4-sealing.md'; Fragment = 'tagCreated=false' },
    [pscustomobject]@{ Path = 'docs/plan-history/host-v4/g8-v4-sealing.md'; Fragment = 'aiflow=false' },
    [pscustomobject]@{ Path = 'docs/reference/plugin-sdk-api-compatibility.md'; Fragment = 'v3 Shipped 为 Core 127 条、UI 45 条' }
    [pscustomobject]@{ Path = 'docs/design/ai-workflow-plugin-exploration.md'; Fragment = '文档状态：G0 已重新签署、G1–G4 已完成实现；G5–G10 尚未实施' }
    [pscustomobject]@{ Path = 'docs/design/ai-workflow-plugin-exploration.md'; Fragment = 'sdkRoute=3.1-compatible-addition' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md'; Fragment = '输入提交：`030a4fca408f72aed75500c105dc51af855d9af7`' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md'; Fragment = 'sdkRoute=3.1-compatible-addition' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md'; Fragment = '`myavalonia-workflow-studio`' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md'; Fragment = 'aiflow=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md'; Fragment = 'windowsCi=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md'; Fragment = 'windowsSmoke=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md'; Fragment = 'releaseAcceptance=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md'; Fragment = 'releaseGate=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md'; Fragment = 'publishable=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md'; Fragment = 'published=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md'; Fragment = 'uploaded=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md'; Fragment = 'tagCreated=false' }
    [pscustomobject]@{ Path = 'docs/reference/plugin-sdk-api-compatibility.md'; Fragment = 'v3 Unshipped 为 Core 72、UI 6' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = 'Test-WorkflowActionG1.ps1' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = 'IWorkflowActionRun' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = '状态：已完成（2026-08-25；完整非发布门禁通过）' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = '85.7%' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = '78417BF2B32DE8810175E8439F26264D6FA7C8FF6639664BBD81C7BCF02ACE3F' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = 'aiflow=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = 'windowsCi=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = 'windowsSmoke=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = 'releaseAcceptance=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = 'releaseGate=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = 'publishable=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = 'published=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = 'uploaded=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md'; Fragment = 'tagCreated=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'Test-WorkflowActionG2.ps1' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'Templates 已发布：`1.1.0`；Build 已发布基线：`1.1.2`' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'Core 127/UI 45，Unshipped 仍为 72/6' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = '状态：已完成并发布（2026-08-25；完整非发布门禁与发布阶段 Windows Smoke 通过）' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'Build 协议负例 / 既有真实插件包 | **25 / 4**' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = '7B698D5E3E9A1877C2DF7F90701149C4FE347C6EAF072D9098CE2C36E5C4C834' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'uploaded=true' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'buildReuploaded=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'aiflow=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'windowsCi=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'windowsSmoke=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'releaseAcceptance=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'releaseGate=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'publishable=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'published=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'uploaded=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md'; Fragment = 'tagCreated=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md'; Fragment = '外部提交：`e651665fb75f241b8b26f5680e2fcac7ff921024`' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md'; Fragment = '单元测试 | **43/43**；失败 0，跳过 0' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md'; Fragment = '57B0627D8B30887C6D7BF032E9C549F97FC2672D0EAC021F23D48D1190CE663B' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md'; Fragment = 'windowsCi=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md'; Fragment = 'releaseAcceptance=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md'; Fragment = 'releaseGate=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md'; Fragment = 'publishable=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md'; Fragment = 'published=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md'; Fragment = 'uploaded=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md'; Fragment = 'tagCreated=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3.1-workflow-protocol-consistency.md'; Fragment = '64 / 227 / 65 / 210' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3.1-workflow-protocol-consistency.md'; Fragment = '89.14% / 77.77%' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3.1-workflow-protocol-consistency.md'; Fragment = '49/49' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3.1-workflow-protocol-consistency.md'; Fragment = '34F516F0005B579E37398C3C71173C29961C37CA35D53C69207B70F4AB61F08A' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3.1-workflow-protocol-consistency.md'; Fragment = 'published=true' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3.1-workflow-protocol-consistency.md'; Fragment = 'uploaded=true' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3.1-workflow-protocol-consistency.md'; Fragment = 'publicOnlyVerification=true' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3.1-template-1.2-publication.md'; Fragment = 'templateVersion=1.2.0' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3.1-template-1.2-publication.md'; Fragment = '4D9357D5F482E1F69BDF0767BD57F827027A1173A815F24653307A13BAA79101' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g3.1-template-1.2-publication.md'; Fragment = 'publicOnlyVerification=true' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g4-my-small-tools-nondestructive-encryption-action.md'; Fragment = 'Test-WorkflowActionG4.ps1' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g4-my-small-tools-nondestructive-encryption-action.md'; Fragment = 'myavalonia.plugin.my-small-tools.workflow.encrypt-video' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g4-my-small-tools-nondestructive-encryption-action.md'; Fragment = 'windowsCi=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g4-my-small-tools-nondestructive-encryption-action.md'; Fragment = 'releaseGate=false' }
    [pscustomobject]@{ Path = 'docs/plan-history/workflow-action/g4-my-small-tools-nondestructive-encryption-action.md'; Fragment = 'publishable=false' }
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
        $normalizedCommand = $command.Path -replace '^\.[\\/]', '' -replace '\\', '/'
        $isHistoricalDeletion = @($historicallyDeletedCommandReferences | Where-Object {
                $_.SourcePath -eq $command.SourcePath -and
                $_.CommandPath -eq $normalizedCommand
            }).Count -gt 0
        if (-not $isHistoricalDeletion) {
            $commands.Add($command)
        }
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
    -RepositoryRoot $repositoryRoot `
    -PluginProjects $pluginProjects `
    -PluginVersionOverrides @{
        'Plugins/MySmallTools/MySmallTools/MySmallTools.csproj' = '3.1.0'
    }

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
