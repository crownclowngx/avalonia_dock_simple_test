[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$modulePath = Join-Path $PSScriptRoot 'DocumentationGate.Core.psm1'
Import-Module $modulePath -Force

function Assert-True {
    param([Parameter(Mandatory)] [bool]$Condition, [Parameter(Mandatory)] [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-ThrowsLike {
    param(
        [Parameter(Mandatory)] [scriptblock]$Action,
        [Parameter(Mandatory)] [string]$ExpectedFragment
    )

    try { & $Action }
    catch {
        Assert-True (
            $_.Exception.Message.Contains($ExpectedFragment, [StringComparison]::Ordinal)) (
            "异常未包含 '$ExpectedFragment'：$($_.Exception.Message)")
        return
    }
    throw "操作本应失败并包含 '$ExpectedFragment'，但实际成功。"
}

function Write-FixtureText {
    param([Parameter(Mandatory)] [string]$Path, [Parameter(Mandatory)] [string]$Text)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Write-VersionFixture {
    param([Parameter(Mandatory)] [string]$Root, [string]$SdkVersion = '1.0.0')
    Write-FixtureText (Join-Path $Root 'Directory.Version.props') @"
<Project><PropertyGroup>
  <MyAvaloniaProductVersion>$SdkVersion</MyAvaloniaProductVersion>
  <MyAvaloniaHostApiAssemblyVersion>1.0.0.0</MyAvaloniaHostApiAssemblyVersion>
  <MyAvaloniaPluginSdkVersion>$SdkVersion</MyAvaloniaPluginSdkVersion>
  <MyAvaloniaPluginSdkAssemblyVersion>1.0.0.0</MyAvaloniaPluginSdkAssemblyVersion>
  <MyAvaloniaPluginSdkApiBaseline>v1</MyAvaloniaPluginSdkApiBaseline>
</PropertyGroup></Project>
"@
    Write-FixtureText (Join-Path $Root 'Host/MyAvaloniaManagementCommon/ApiCompatibility/v1/PublicAPI.Shipped.txt') "#nullable enable`nFixture.Type`n"
    Write-FixtureText (Join-Path $Root 'Host/MyAvaloniaManagementCommon/ApiCompatibility/v1/PublicAPI.Unshipped.txt') "#nullable enable`n"
}

function Write-PluginFixture {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [string]$RelativePath,
        [string]$Maximum = '2.0.0'
    )
    Write-FixtureText (Join-Path $Root $RelativePath) @"
<Project><PropertyGroup>
  <ManagedPlugin>true</ManagedPlugin><PluginVersion>1.0.0</PluginVersion>
  <ManagedPluginHostApiMinInclusive>1.0.0</ManagedPluginHostApiMinInclusive>
  <ManagedPluginHostApiMaxExclusive>$Maximum</ManagedPluginHostApiMaxExclusive>
  <ManagedPluginCommonContractMinInclusive>1.0.0</ManagedPluginCommonContractMinInclusive>
  <ManagedPluginCommonContractMaxExclusive>$Maximum</ManagedPluginCommonContractMaxExclusive>
</PropertyGroup></Project>
"@
}

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $temporaryParent ('DocumentationGateCoreTests-' + [Guid]::NewGuid().ToString('N'))
Assert-DocumentationChildPath -Candidate $testRoot -Parent $temporaryParent -Purpose 'G16 核心测试目录'
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $guide = Join-Path $testRoot 'docs/guide.md'
    $target = Join-Path $testRoot 'docs/target file.md'
    $script = Join-Path $testRoot 'scripts/Test-Sample.ps1'
    $project = Join-Path $testRoot 'Host/Sample/Sample.csproj'
    Write-FixtureText $guide '[本地](<target%20file.md#标题>) [外部](https://example.com) [页内](#标题)'
    Write-FixtureText $target '# 标题'
    Write-FixtureText $script '# fixture'
    Write-FixtureText $project '<Project />'
    $tracked = @('docs/guide.md', 'docs/target file.md', 'scripts/Test-Sample.ps1', 'Host/Sample/Sample.csproj')
    $links = @(Get-DocumentationMarkdownLinks -Text ([IO.File]::ReadAllText($guide)) -SourcePath 'docs/guide.md')
    Assert-True ($links.Count -eq 3) 'Markdown 链接提取数量不正确。'
    Assert-True ((Assert-DocumentationLinks -RepositoryRoot $testRoot -Links $links -TrackedPaths $tracked) -eq 1) (
        '本地、外部与页内链接分类不正确。')

    $brokenLinks = @(Get-DocumentationMarkdownLinks -Text '[损坏](missing.md)' -SourcePath 'docs/guide.md')
    Assert-ThrowsLike {
        Assert-DocumentationLinks -RepositoryRoot $testRoot -Links $brokenLinks -TrackedPaths $tracked
    } '本地链接不存在'
    Assert-ThrowsLike {
        Assert-DocumentationLinks -RepositoryRoot $testRoot -Links $links -TrackedPaths @('docs/guide.md')
    } '大小写或 Git 跟踪状态不正确'

    $commands = @(Get-DocumentationCommandPaths `
        -Text '.\scripts\Test-Sample.ps1' -SourcePath 'docs/guide.md')
    Assert-True ((Assert-DocumentationCommandPaths `
                -RepositoryRoot $testRoot -Commands $commands -TrackedPaths $tracked) -eq 1) (
        '脚本路径没有通过。')
    $missingCommand = @(Get-DocumentationCommandPaths `
        -Text 'scripts/Missing.ps1' -SourcePath 'docs/guide.md')
    Assert-ThrowsLike {
        Assert-DocumentationCommandPaths `
            -RepositoryRoot $testRoot -Commands $missingCommand -TrackedPaths $tracked
    } '引用的脚本不存在'

    $projects = @(Get-DocumentationProjectPaths `
        -Text 'dotnet build Host/Sample/Sample.csproj' -SourcePath 'docs/guide.md')
    Assert-True ((Assert-DocumentationProjectPaths `
                -RepositoryRoot $testRoot -Projects $projects -TrackedPaths $tracked) -eq 1) (
        '真实项目路径没有通过。')
    $missingProject = @(Get-DocumentationProjectPaths `
        -Text 'dotnet build Host/Missing/Missing.csproj' -SourcePath 'docs/guide.md')
    Assert-ThrowsLike {
        Assert-DocumentationProjectPaths `
            -RepositoryRoot $testRoot -Projects $missingProject -TrackedPaths $tracked
    } '引用的项目不存在'

    $documents = @([pscustomobject]@{ Path = 'README.md'; Text = '状态：待整改，不满足封板条件' })
    $rules = @([pscustomobject]@{ Name = '旧状态'; Pattern = '状态：待整改' })
    Assert-ThrowsLike {
        Assert-DocumentationForbiddenStatements -Documents $documents -Rules $rules
    } '旧状态'

    Write-FixtureText (Join-Path $testRoot 'src/CurrentType.cs') 'public interface ICurrentType { }'
    Write-FixtureText (Join-Path $testRoot 'src/Production.cs') 'internal sealed class Production { }'
    Assert-DocumentationSourceSymbols `
        -RepositoryRoot $testRoot `
        -RequiredSymbols @([pscustomobject]@{ Symbol = 'ICurrentType'; Path = 'src/CurrentType.cs' }) `
        -ForbiddenSymbols @('IRemovedType') `
        -ProductionFiles @('src/Production.cs')
    Write-FixtureText (Join-Path $testRoot 'src/Production.cs') 'internal sealed class IRemovedType { }'
    Assert-ThrowsLike {
        Assert-DocumentationSourceSymbols `
            -RepositoryRoot $testRoot `
            -RequiredSymbols @([pscustomobject]@{ Symbol = 'ICurrentType'; Path = 'src/CurrentType.cs' }) `
            -ForbiddenSymbols @('IRemovedType') `
            -ProductionFiles @('src/Production.cs')
    } '重新出现在生产源码'

    $pluginProject = 'Plugins/Fixture/Fixture.csproj'
    Write-VersionFixture -Root $testRoot
    Write-PluginFixture -Root $testRoot -RelativePath $pluginProject
    $facts = Get-ManagementV1BaselineFacts -RepositoryRoot $testRoot -PluginProjects @($pluginProject)
    Assert-True ($facts.SdkVersion -ceq '1.0.0' -and $facts.Plugins.Count -eq 1) (
        'v1 版本与插件事实没有正确读取。')
    Write-VersionFixture -Root $testRoot -SdkVersion '1.1.0'
    Assert-ThrowsLike {
        Get-ManagementV1BaselineFacts -RepositoryRoot $testRoot -PluginProjects @($pluginProject)
    } '插件版本'
    Write-VersionFixture -Root $testRoot
    Write-PluginFixture -Root $testRoot -RelativePath $pluginProject -Maximum '3.0.0'
    Assert-ThrowsLike {
        Get-ManagementV1BaselineFacts -RepositoryRoot $testRoot -PluginProjects @($pluginProject)
    } '兼容区间不是'

    Assert-ThrowsLike {
        Assert-DocumentationChildPath `
            -Candidate (Join-Path (Split-Path -Parent $testRoot) 'sibling') `
            -Parent $testRoot -Purpose '越界夹具'
    } '允许根之外'
    $readOnlyRoot = Join-Path $testRoot 'readonly'
    $readOnlyFile = Join-Path $readOnlyRoot 'fixture.txt'
    Write-FixtureText $readOnlyFile 'fixture'
    (Get-Item -LiteralPath $readOnlyFile).Attributes = [IO.FileAttributes]::ReadOnly
    Remove-DocumentationOwnedTree -Path $readOnlyRoot -AllowedParent $testRoot
    Assert-True (-not (Test-Path -LiteralPath $readOnlyRoot)) '只读临时树没有安全清理。'

    Write-Host '[G16] 文档门禁核心单元测试通过：链接、脚本/项目路径、过期表述、类型、版本、插件区间和路径安全均符合预期。'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-DocumentationOwnedTree -Path $testRoot -AllowedParent $temporaryParent
    }
}
