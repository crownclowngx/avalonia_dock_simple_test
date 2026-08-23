[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('G1', 'G2', 'G3', 'G4', 'G5', 'G6')]
    [string]$Stage,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\test-results\HostV4\$Stage"))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\test-results\HostV4'))

if (-not $resultRoot.StartsWith(
        $allowedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "V4 开发门禁结果目录越界：$resultRoot。"
}

function Invoke-Checked {
    param([string]$FilePath, [string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($Arguments -join ' ') 失败，退出码：$LASTEXITCODE。"
    }
}

function Assert-PatternAbsent {
    param([string]$Pattern, [string[]]$Paths, [string[]]$Globs, [string]$Message)
    $arguments = @('--quiet', $Pattern) + $Paths
    foreach ($glob in $Globs) { $arguments += @('-g', $glob) }
    & rg @arguments
    if ($LASTEXITCODE -eq 0) { throw $Message }
    if ($LASTEXITCODE -gt 1) { throw "结构扫描失败：$Pattern。" }
}

if (Test-Path -LiteralPath $resultRoot) {
    Remove-Item -LiteralPath $resultRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultRoot | Out-Null

$stageNumber = [int]$Stage.Substring(1)
$hostRoot = Join-Path $repositoryRoot 'Host\MyAvaloniaManagement'

Push-Location $repositoryRoot
try {
    # 该入口只执行开发期本地验证。它不会调用 AIFLOW、Windows CI/Smoke、
    # ReleaseAcceptance、Host Release Gate、标签、上传或发布命令。
    Invoke-Checked dotnet @('tool', 'restore')
    Invoke-Checked dotnet @('restore', 'MyAvaloniaManagement.sln', '--locked-mode')
    Invoke-Checked dotnet @(
        'build', 'MyAvaloniaManagement.sln',
        '-c', $Configuration,
        '--no-restore',
        '-warnaserror')

    & (Join-Path $PSScriptRoot 'Invoke-MyAvaloniaManagementTests.ps1') `
        -Configuration $Configuration -NoRestore
    if ($LASTEXITCODE -ne 0) { throw 'Host 三层测试或覆盖率门禁失败。' }

    Assert-PatternAbsent `
        'IDropTarget|DragDrop\.AllowDrop|Microsoft\.Extensions\.Hosting' `
        @($hostRoot, (Join-Path $repositoryRoot 'Directory.Packages.props')) `
        @('*.cs', '*.axaml', '*.csproj', '*.props') `
        'G1 已删除的拖放面或 Hosting 直接依赖重新出现。'
    Assert-PatternAbsent '<Separator\s*/>' `
        @((Join-Path $hostRoot 'Views\MenuView.axaml')) @('*.axaml') `
        '文件菜单重新出现悬空 Separator。'

    if ($stageNumber -ge 2) {
        Assert-PatternAbsent `
            'DockNameConstant|CreateDocumentAsync\(string|OpenDocumentByPath|public\s+async\s+Task\s+CreateDocument\(string' `
            @($hostRoot) @('*.cs') `
            'G2 已删除的字符串身份或 ViewModel 用例转发重新出现。'
    }

    if ($stageNumber -ge 3) {
        $layoutRoot = Join-Path $hostRoot 'Business\Layout'
        foreach ($file in @(
                'DockLayoutLifecycle.cs',
                'DockLayoutSnapshotMapper.cs',
                'DockLayoutRuntimeValidator.cs')) {
            if (-not (Test-Path -LiteralPath (Join-Path $layoutRoot $file) -PathType Leaf)) {
                throw "G3 Layout 职责文件缺失：$file。"
            }
        }
    }

    if ($stageNumber -ge 4) {
        Assert-PatternAbsent `
            'Application\.Current[^;\r\n]*Resources' `
            @((Join-Path $hostRoot 'Business')) @('*.cs') `
            'G4 业务或生命周期代码重新通过 Application.Current 查找回收器。'
    }

    if ($stageNumber -ge 5) {
        if (Test-Path -LiteralPath (Join-Path $hostRoot 'Business\Helpers')) {
            throw 'G5 完成后 Business/Helpers 必须不存在。'
        }
        if (Test-Path -LiteralPath (Join-Path $hostRoot 'Common\Utils\Misc')) {
            throw 'G5 完成后 Common/Utils/Misc 必须不存在。'
        }
        Assert-PatternAbsent `
            'MyAvaloniaManagement\.Business\.Helpers|MyAvaloniaManagement\.(ViewModels|Views)\.Hello' `
            @($hostRoot) @('*.cs', '*.axaml') `
            'G5 完成后旧 Helpers 或 Hello 命名空间不得存在。'
    }

    if ($stageNumber -ge 6) {
        Assert-PatternAbsent `
            'AssemblyLoadConstant|PLUGINS_SUBDIRECTORY|class\s+FileHelper' `
            @($hostRoot) @('*.cs') `
            'G6 完成后旧部署常量或 FileHelper 不得存在。'
    }

    & (Join-Path $PSScriptRoot 'Test-Documentation.ps1')
    if ($LASTEXITCODE -ne 0) { throw '文档门禁失败。' }

    $hostSummary = Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot 'artifacts\test-results\MyAvaloniaManagement\summary.json') |
        ConvertFrom-Json
    if ([double]$hostSummary.lineCoverage -lt 84.39 -or
        [double]$hostSummary.branchCoverage -lt 70.58) {
        throw 'Host 覆盖率低于 V4 G0 的 84.39% / 70.58%。'
    }

    $summary = [ordered]@{
        schemaVersion = 1
        stage = $Stage
        configuration = $Configuration
        hostPassed = [int]$hostSummary.passed
        hostLineCoverage = [double]$hostSummary.lineCoverage
        hostBranchCoverage = [double]$hostSummary.branchCoverage
        passed = $true
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
    Write-Host "$Stage Host V4 本地开发门禁通过。"
}
finally {
    Pop-Location
}
