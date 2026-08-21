[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modulePath = Join-Path $PSScriptRoot 'DocumentationGate.Core.psm1'
Import-Module $modulePath -Force

# 当前事实只覆盖宿主、SDK 和 Managed Plugin v2 的公共说明。Host V2 任务书及分阶段记录只参加
# 链接、命令和项目路径校验，不参加 v1 当前事实的措辞禁令，避免把目标设计误判为已经实现。
# 插件业务设计、理论文章和 .NET 升级历史仍不属于本门禁范围。
$currentDocumentPaths = @(
    'README.md',
    'docs/README.md',
    'docs/design/document-persistence-v2-design.md',
    'docs/design/host-plugin-architecture-review.md',
    'docs/design/host-v1-sealing-readiness-plan.md',
    'docs/plan-history/host-v1/g16-documentation-and-v1-baseline.md',
    'docs/reference/dock-layout-snapshot-v1.md',
    'docs/reference/myavalonia-management-tests.md',
    'docs/reference/plugin-sdk-api-compatibility.md',
    'Host/MyAvaloniaManagement/docs/README.md',
    'Host/MyAvaloniaManagement/docs/design/architecture.md',
    'Host/MyAvaloniaManagement/docs/design/design-methodology-and-tradeoffs.md',
    'Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md',
    'Host/MyAvaloniaManagement.PluginSdk/README.md',
    'Host/MyAvaloniaManagement.LegacyPluginContracts/README.md',
    'Host/MyAvaloniaManagement.PluginSdk.UI/README.md'
)
$currentDocumentPaths += @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'docs\quick-start') `
        -Filter '*.md' -File | ForEach-Object {
            [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName).Replace('\', '/')
        })
$hostHistoryDirectories = @('host-v1', 'host-v2')
$historyDocumentPaths = @($hostHistoryDirectories | ForEach-Object {
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "docs\plan-history\$_") `
            -Filter '*.md' -File
    } | ForEach-Object {
        [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName).Replace('\', '/')
    })
$candidateDocumentPaths = @('docs/design/host-v2-breaking-refactor-plan.md')
$linkDocumentPaths = @(
    $currentDocumentPaths + $historyDocumentPaths + $candidateDocumentPaths |
        Sort-Object -Unique)

# 规则只拦截仍被写成“当前状态”的旧结论。Legacy、旧类型和固定数量在历史审计段落中仍是
# 必要事实，因此不能用全仓库关键词禁令粗暴删除。
$forbiddenStatementRules = @(
    [pscustomobject]@{ Name = '宿主仍待封板'; Pattern = '状态：待整改，不满足封板条件' },
    [pscustomobject]@{ Name = 'G16 仍待完成'; Pattern = '仅\s*G16\s*待完成' },
    [pscustomobject]@{ Name = '导航仍声明未封板'; Pattern = '完成前不得认定宿主已封板' },
    [pscustomobject]@{ Name = '主项目仍把 G8 当作当前基线'; Pattern = '2026-08-18\s+G8\s+基线为' },
    [pscustomobject]@{ Name = '保存契约仍未统一'; Pattern = '保存契约尚未统一' },
    [pscustomobject]@{ Name = 'Legacy 仍是并列入口'; Pattern = 'Legacy\s*(?:为|作为).*并列.*(?:方式|入口)' },
    [pscustomobject]@{ Name = 'G16 证据尚未回填'; Pattern = '待(?:执行|最终复跑)' }
)

$requiredSymbols = @(
    [pscustomobject]@{ Symbol = 'IPluginModule'; Path = 'Host/MyAvaloniaManagement.PluginSdk.UI/PluginRegistrationContracts.cs' },
    [pscustomobject]@{ Symbol = 'IPluginRegistration'; Path = 'Host/MyAvaloniaManagement.PluginSdk.UI/PluginRegistrationContracts.cs' },
    [pscustomobject]@{ Symbol = 'DocumentContent'; Path = 'Host/MyAvaloniaManagement.PluginSdk/DocumentContracts.cs' },
    [pscustomobject]@{ Symbol = 'IHostEventBus'; Path = 'Host/MyAvaloniaManagement.PluginSdk/PluginContracts.cs' },
    [pscustomobject]@{ Symbol = 'HostDiagnosticRedactionPolicy'; Path = 'Host/MyAvaloniaManagement/Business/Diagnostics/HostDiagnostics.cs' },
    [pscustomobject]@{ Symbol = 'DocumentEnvelopeSerializer'; Path = 'Host/MyAvaloniaManagement/Business/Documents/DocumentEnvelopeSerializer.cs' }
)
$forbiddenSymbols = @('IDocumentSavePathPolicy', 'HandledEventsAwareBehavior')
$pluginProjects = @(
    'Plugins/BiliDownloader/BiliDownloader/BiliDownloader.csproj',
    'Plugins/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug.csproj',
    'Plugins/MyPlugTest/MyPlugTest/MyPlugTest.csproj',
    'Plugins/MySmallTools/MySmallTools/MySmallTools.csproj'
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
        if (-not $project.Path.StartsWith('Plugins/QuickStartPlugin/', [StringComparison]::Ordinal)) {
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
