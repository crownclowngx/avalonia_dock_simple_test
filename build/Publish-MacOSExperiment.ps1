#Requires -Version 7.0
param(
    [ValidateSet('osx-arm64', 'osx-x64')]
    [string[]]$RuntimeIdentifier = @('osx-arm64', 'osx-x64'),
    [string]$OutputDirectory,
    [string]$Python = 'python'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot 'artifacts/macos-experiment' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$version = ([xml](Get-Content -Raw (Join-Path $repositoryRoot 'Directory.Version.props'))).Project.PropertyGroup.MyAvaloniaProductVersion
$runRoot = Join-Path $outputRoot ('work-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}

Push-Location $repositoryRoot
try {
    foreach ($rid in $RuntimeIdentifier) {
        $architecture = if ($rid -eq 'osx-arm64') { 'arm64' } else { 'x64' }
        $ridRoot = Join-Path $runRoot $rid
        $hostRoot = Join-Path $ridRoot 'host'
        $installationName = "MyAvaloniaManagement-$version-$rid-experiment"
        $installationRoot = Join-Path $hostRoot $installationName
        $appRoot = Join-Path $installationRoot 'MyAvaloniaManagement.app'
        $macOSRoot = Join-Path $appRoot 'Contents/MacOS'
        $pluginRoot = Join-Path $ridRoot 'plugin'
        $pluginDeployRoot = Join-Path $pluginRoot 'Controls'
        $hostArtifacts = Join-Path $ridRoot 'build-host'
        $pluginArtifacts = Join-Path $ridRoot 'build-plugin'
        # Each project gets an ignored RID-specific lock. Cross restore never rewrites committed Windows locks.
        $lockProperty = "-p:NuGetLockFilePath=obj/macos-experiment/$rid/packages.lock.json"

        Invoke-DotNet @(
            'publish', 'Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj',
            '-c', 'Release', '-r', $rid, '--self-contained', 'true', '--nologo',
            '--artifacts-path', $hostArtifacts, '-o', $macOSRoot,
            "-p:PlatformTarget=$architecture", '-p:UseAppHost=true',
            '-p:PublishAot=false', '-p:PublishTrimmed=false', '-p:PublishSingleFile=false',
            $lockProperty
        )

        Invoke-DotNet @(
            'build', 'Plugins/MyPlugTest/MyPlugTest/MyPlugTest.csproj',
            '-c', 'Release', '-r', $rid, '--nologo', '--artifacts-path', $pluginArtifacts,
            '-p:PlatformTarget=AnyCPU', '-p:SelfContained=false',
            '-p:ManagedPluginExperimentalMacOS=true', "-p:ManagedPluginRuntimeIdentifier=$rid",
            "-p:ManagedPluginDeployRoot=$pluginDeployRoot", '-p:SkipPluginDeploy=false',
            $lockProperty
        )

        New-Item -ItemType Directory -Path (Join-Path $installationRoot 'Controls'), (Join-Path $appRoot 'Contents/Resources') -Force | Out-Null
        $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleExecutable</key><string>MyAvaloniaManagement</string>
  <key>CFBundleName</key><string>MyAvalonia</string>
  <key>CFBundleDisplayName</key><string>MyAvaloniaManagement Experiment</string>
  <key>CFBundleIdentifier</key><string>local.myavalonia.management.experiment</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
"@
        [IO.File]::WriteAllText((Join-Path $appRoot 'Contents/Info.plist'), $plist.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
        foreach ($name in @('Prepare.command', 'Debug.command', 'Experiment.entitlements')) {
            $content = Get-Content -Raw (Join-Path $PSScriptRoot "macos-experiment/$name")
            [IO.File]::WriteAllText((Join-Path $installationRoot $name), $content.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
        }
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs/quick-start/macos-experiment.md') -Destination (Join-Path $installationRoot 'README.md')
        [IO.File]::WriteAllText((Join-Path $installationRoot 'Controls/README.txt'), 'Copy complete plugin folders here, then restart the Host.')
        [IO.File]::WriteAllText((Join-Path $installationRoot 'runtime-identifier.txt'), $rid)

        & $Python (Join-Path $PSScriptRoot 'macos-experiment/package.py') `
            --host-root $hostRoot --plugin-root $pluginRoot --output $outputRoot --rid $rid --version $version
        if ($LASTEXITCODE -ne 0) { throw "Package validation failed: $rid" }
    }
    Write-Host "macOS experiment packages: $outputRoot"
    Write-Host "Build and unpacked files retained for inspection: $runRoot"
}
finally { Pop-Location }
