param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ("MyAvaloniaPluginSdkG5-" + [Guid]::NewGuid().ToString("N"))))
$packageOutput = Join-Path $temporaryRoot "packages"
$isolatedPackageCache = Join-Path $temporaryRoot "global-packages"
$sdkVersion = ([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot "Directory.Version.props"))).Project.PropertyGroup.MyAvaloniaPluginSdkVersion

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-DotNet {
    param([string[]]$Arguments, [string]$WorkingDirectory = $repositoryRoot)
    Push-Location $WorkingDirectory
    try {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') 失败，退出码 $LASTEXITCODE。"
        }
    }
    finally {
        Pop-Location
    }
}

function Assert-DotNetBuildFails {
    param([string]$ProjectPath, [string]$WorkingDirectory, [string[]]$ExpectedFragments)
    Push-Location $WorkingDirectory
    try {
        $output = @(& dotnet build $ProjectPath -c Release --no-restore --nologo 2>&1)
        $exitCode = $LASTEXITCODE
        Assert-True ($exitCode -ne 0) "旧候选 SDK 夹具意外编译成功，破坏式 G5 基线没有生效。"
        $text = $output -join [Environment]::NewLine
        foreach ($fragment in $ExpectedFragments) {
            Assert-True ($text.IndexOf($fragment, [StringComparison]::Ordinal) -ge 0) "旧候选夹具失败信息缺少 $fragment。"
        }
    }
    finally {
        Pop-Location
    }
}

function Read-Nuspec {
    param([string]$PackagePath)
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entry = $archive.Entries | Where-Object FullName -Like "*.nuspec" | Select-Object -First 1
        Assert-True ($null -ne $entry) "包 $PackagePath 缺少 nuspec。"
        $reader = [IO.StreamReader]::new($entry.Open())
        try {
            return [xml]$reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-PackageEntries {
    param([string]$PackagePath)
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        return @($archive.Entries | ForEach-Object FullName)
    }
    finally {
        $archive.Dispose()
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null

try {
    Invoke-DotNet @(
        "pack", "Host/MyAvaloniaManagementCommon/MyAvaloniaManagementCommon.csproj",
        "-c", $Configuration, "--no-restore", "-o", $packageOutput, "--nologo"
    )
    Invoke-DotNet @(
        "pack", "Host/MyAvaloniaManagement.PluginSdk.UI/MyAvaloniaManagement.PluginSdk.UI.csproj",
        "-c", $Configuration, "--no-restore", "-o", $packageOutput, "--nologo"
    )

    $basePackage = Join-Path $packageOutput "MyAvaloniaManagement.PluginSdk.$sdkVersion.nupkg"
    $uiPackage = Join-Path $packageOutput "MyAvaloniaManagement.PluginSdk.UI.$sdkVersion.nupkg"
    Assert-True (Test-Path -LiteralPath $basePackage) "未生成基础 SDK 包。"
    Assert-True (Test-Path -LiteralPath $uiPackage) "未生成 UI Profile 包。"

    $baseEntries = Get-PackageEntries $basePackage
    Assert-True ($baseEntries -contains "lib/net10.0/MyAvaloniaManagementCommon.dll") "基础 SDK 包缺少契约程序集。"
    Assert-True ($baseEntries -contains "lib/net10.0/MyAvaloniaManagementCommon.xml") "基础 SDK 包缺少 XML 文档。"
    Assert-True ($baseEntries -contains "README.md") "基础 SDK 包缺少 README。"
    Assert-True (-not ($baseEntries -contains "lib/net10.0/MyAvaloniaManagement.dll")) "基础 SDK 包错误包含 Host 程序集。"

    $uiEntries = Get-PackageEntries $uiPackage
    Assert-True ($uiEntries -contains "lib/net10.0/_._") "UI Profile 缺少 NuGet 元包占位。"
    Assert-True (-not ($uiEntries | Where-Object { $_ -like "lib/*/*.dll" })) "UI Profile 不应包含运行时程序集。"

    $baseNuspec = Read-Nuspec $basePackage
    $uiNuspec = Read-Nuspec $uiPackage
    $baseDependencyIds = @($baseNuspec.package.metadata.dependencies.group.dependency | ForEach-Object id)
    $forbiddenBaseDependencies = @(
        "Avalonia.Desktop", "Avalonia.Fonts.Inter", "Avalonia.Themes.Fluent",
        "Dock.Avalonia", "Dock.Avalonia.Themes.Fluent",
        "Dock.Controls.ProportionalStackPanel", "Dock.Controls.Recycling",
        "Dock.Controls.Recycling.Model", "Irihi.Ursa", "Irihi.Ursa.Themes.Semi", "Semi.Avalonia"
    )
    foreach ($dependency in $forbiddenBaseDependencies) {
        Assert-True ($baseDependencyIds -notcontains $dependency) "基础 SDK 依赖图错误包含 $dependency。"
    }

    $uiDependencies = @{}
    foreach ($dependency in $uiNuspec.package.metadata.dependencies.group.dependency) {
        $uiDependencies[$dependency.id] = $dependency.version
    }
    Assert-True ($uiDependencies["MyAvaloniaManagement.PluginSdk"] -eq $sdkVersion) "UI Profile 未依赖同版本基础 SDK。"
    $expectedExactVersions = @{
        "Avalonia.Themes.Fluent" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaAvaloniaUiVersion)]"
        "Dock.Avalonia" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaDockUiVersion)]"
        "Dock.Avalonia.Themes.Fluent" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaDockUiVersion)]"
        "Dock.Controls.ProportionalStackPanel" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaDockUiVersion)]"
        "Dock.Controls.Recycling" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaDockUiVersion)]"
        "Dock.Controls.Recycling.Model" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaDockUiVersion)]"
        "Irihi.Ursa" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaUrsaUiVersion)]"
        "Irihi.Ursa.Themes.Semi" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaUrsaUiVersion)]"
        "Semi.Avalonia" = "[$(([xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaSemiUiVersion)]"
    }
    foreach ($item in $expectedExactVersions.GetEnumerator()) {
        Assert-True ($uiDependencies[$item.Key] -eq $item.Value) "UI Profile 的 $($item.Key) 版本应为 $($item.Value)，实际为 $($uiDependencies[$item.Key])。"
    }

    $basicProject = Join-Path $temporaryRoot "BasicPlugin"
    New-Item -ItemType Directory -Path $basicProject | Out-Null
    $nugetConfig = Join-Path $temporaryRoot "NuGet.Config"
    $escapedPackageOutput = [Security.SecurityElement]::Escape($packageOutput)
    Set-Content -LiteralPath $nugetConfig -Encoding UTF8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="g3-local" value="$escapedPackageOutput" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
"@
    Set-Content -LiteralPath (Join-Path $basicProject "BasicPlugin.csproj") -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="$sdkVersion" /></ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $basicProject "BasicPluginModule.cs") -Encoding UTF8 -Value @'
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;

public sealed class BasicPluginModule : IPluginModule
{
    public void Configure(IPluginRegistrationContext context)
    {
        _ = context.PluginId;
        context.Services.AddSingleton<BasicPluginService>();
    }
}

public sealed class BasicPluginService;
'@
    Invoke-DotNet @("restore", "BasicPlugin.csproj", "--configfile", $nugetConfig, "--packages", $isolatedPackageCache, "--nologo") $basicProject
    Invoke-DotNet @("build", "BasicPlugin.csproj", "-c", "Release", "--no-restore", "--nologo") $basicProject
    $basicAssets = Get-Content -Raw -LiteralPath (Join-Path $basicProject "obj/project.assets.json")
    # Dock.Model.Mvvm 12.0.0.2 自身传递依赖 Dock.Controls.Recycling.Model；
    # 它不在基础包 nuspec 的直接依赖中，但会出现在最终还原图，G3 不改变现有 Dock public 签名。
    $forbiddenResolvedDependencies = $forbiddenBaseDependencies |
        Where-Object { $_ -ne "Dock.Controls.Recycling.Model" }
    foreach ($dependency in $forbiddenResolvedDependencies) {
        Assert-True (-not $basicAssets.Contains('"' + $dependency + '/')) "基础插件还原图错误包含 $dependency。"
    }

    # 旧候选接口必须失败，防止未来为了偶然二进制兼容重新引入模块自报身份或 ConfigureServices。
    $legacyProject = Join-Path $temporaryRoot "LegacyCandidatePlugin"
    New-Item -ItemType Directory -Path $legacyProject | Out-Null
    Set-Content -LiteralPath (Join-Path $legacyProject "LegacyCandidatePlugin.csproj") -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="$sdkVersion" /></ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $legacyProject "LegacyCandidatePluginModule.cs") -Encoding UTF8 -Value @'
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;

public sealed class LegacyCandidatePluginModule : IPluginModule
{
    public PluginId PluginId { get; } = new("myavalonia.plugin.legacy-candidate");
    public void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<LegacyCandidatePluginModule>();
}
'@
    Invoke-DotNet @("restore", "LegacyCandidatePlugin.csproj", "--configfile", $nugetConfig, "--packages", $isolatedPackageCache, "--nologo") $legacyProject
    Assert-DotNetBuildFails "LegacyCandidatePlugin.csproj" $legacyProject @("CS0535", "Configure")

    $uiProject = Join-Path $temporaryRoot "UiPlugin"
    New-Item -ItemType Directory -Path $uiProject | Out-Null
    Set-Content -LiteralPath (Join-Path $uiProject "UiPlugin.csproj") -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="MyAvaloniaManagement.PluginSdk.UI" Version="$sdkVersion" /></ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $uiProject "ProfileView.axaml") -Encoding UTF8 -Value @'
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ursa="clr-namespace:Ursa.Controls;assembly=Ursa"
             xmlns:dock="clr-namespace:Dock.Avalonia.Controls;assembly=Dock.Avalonia"
             x:Class="UiPlugin.ProfileView">
  <StackPanel Background="{DynamicResource AppPanelBrush}">
    <ursa:IconButton />
    <dock:DockControl />
  </StackPanel>
</UserControl>
'@
    Set-Content -LiteralPath (Join-Path $uiProject "ProfileView.axaml.cs") -Encoding UTF8 -Value @'
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace UiPlugin;
public sealed partial class ProfileView : UserControl
{
    public ProfileView() => AvaloniaXamlLoader.Load(this);
}
'@
    Invoke-DotNet @("restore", "UiPlugin.csproj", "--configfile", $nugetConfig, "--packages", $isolatedPackageCache, "--nologo") $uiProject
    Invoke-DotNet @("build", "UiPlugin.csproj", "-c", "Release", "--no-restore", "--nologo") $uiProject

    Write-Host "G5 Plugin SDK package acceptance passed. SDK=$sdkVersion; final sample compiled; legacy candidate rejected."
}
finally {
    $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($temporaryRoot.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $temporaryRoot)) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
