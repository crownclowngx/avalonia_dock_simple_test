[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$resultsRoot = if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    Join-Path $artifactRoot 'test-results\WindowsSmoke'
}
elseif ([IO.Path]::IsPathRooted($ResultsDirectory)) {
    [IO.Path]::GetFullPath($ResultsDirectory)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $ResultsDirectory))
}
$smokeRoot = Join-Path $artifactRoot 'MyAvaloniaManagement\smoke'
$dataRoot = Join-Path $artifactRoot 'MyAvaloniaManagement\smoke-data'

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)] [string]$Candidate,
        [Parameter(Mandatory)] [string]$Parent,
        [Parameter(Mandatory)] [string]$Purpose
    )

    $resolvedCandidate = [IO.Path]::GetFullPath($Candidate)
    $resolvedParent = [IO.Path]::GetFullPath($Parent).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedCandidate.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose 拒绝操作 artifacts 之外的路径：$resolvedCandidate"
    }
}

function Write-Summary {
    param(
        [Parameter(Mandatory)] [bool]$Passed,
        [AllowNull()] [Nullable[int]]$ExitCode,
        [Parameter(Mandatory)] [bool]$LayoutSaved,
        [AllowNull()] [Nullable[int]]$LayoutSchemaVersion,
        [Parameter(Mandatory)] [bool]$LegacyLayoutAbsent,
        [AllowNull()] [string]$ErrorMessage,
        [Parameter(Mandatory)] [Diagnostics.Stopwatch]$Stopwatch
    )

    $summary = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        platform = 'win-x64'
        passed = $Passed
        exitCode = $ExitCode
        layoutSaved = $LayoutSaved
        layoutFileName = 'layout-v2.json'
        layoutSchemaVersion = $LayoutSchemaVersion
        legacyLayoutAbsent = $LegacyLayoutAbsent
        isolatedDataDirectory = $true
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        durationMilliseconds = $Stopwatch.ElapsedMilliseconds
        error = $ErrorMessage
    }
    [IO.File]::WriteAllText(
        (Join-Path $resultsRoot 'summary.json'),
        ($summary | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))
}

if ($env:OS -ne 'Windows_NT' -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'Windows 真实窗口 Smoke 只支持 Windows x64。'
}

foreach ($path in @($resultsRoot, $smokeRoot, $dataRoot)) {
    Assert-ChildPath -Candidate $path -Parent $artifactRoot -Purpose 'Windows Smoke'
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $path | Out-Null
}

$previousDataDirectory = $env:MYAVALONIA_DATA_DIRECTORY
$previousSmokeMode = $env:MYAVALONIA_SMOKE_TEST
$process = $null
$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$exitCode = $null
$layoutSaved = $false
$layoutSchemaVersion = $null
$legacyLayoutAbsent = $false
try {
    $publishArguments = @(
        'publish',
        (Join-Path $repositoryRoot 'Host\MyAvaloniaManagement\MyAvaloniaManagement.csproj'),
        '-c', $Configuration,
        '-o', $smokeRoot,
        '-p:SkipPluginDeploy=true',
        '--nologo'
    )
    if ($NoRestore) { $publishArguments += '--no-restore' }

    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Windows Smoke 发布失败，退出码 $LASTEXITCODE。"
    }

    $executable = Join-Path $smokeRoot 'MyAvaloniaManagement.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Windows Smoke 没有找到发布后的宿主程序：$executable"
    }

    # 真实进程仍会创建并打开主窗口；两个环境变量只把数据所有权限制到本轮 artifacts，
    # 并在 Opened 后请求正常关闭，不会读取或覆盖用户 LocalAppData 中的正式布局。
    $env:MYAVALONIA_DATA_DIRECTORY = $dataRoot
    $env:MYAVALONIA_SMOKE_TEST = '1'
    $process = Start-Process `
        -FilePath $executable `
        -WorkingDirectory $smokeRoot `
        -WindowStyle Hidden `
        -PassThru
    if (-not $process.WaitForExit(15000)) {
        throw '宿主没有在 15 秒内完成真实窗口 Opened/Closing 并正常退出。'
    }
    $exitCode = $process.ExitCode
    if ($exitCode -ne 0) {
        throw "Windows Smoke 宿主退出码为 $exitCode。"
    }

    # G14 的真实窗口门禁验证的是当前唯一生产格式，而不是只检查“某个布局文件存在”。
    # 这里同时检查文件名、schema 和 V1 文件缺失，防止 Host 误回退到历史 writer 后仍被 Smoke 放行。
    $layoutPath = Join-Path $dataRoot 'layout-v2.json'
    $layoutSaved = Test-Path -LiteralPath $layoutPath -PathType Leaf
    if (-not $layoutSaved) {
        throw 'Windows Smoke 没有在隔离数据目录保存 layout-v2.json。'
    }
    $layout = Get-Content -LiteralPath $layoutPath -Raw | ConvertFrom-Json
    $layoutSchemaVersion = [int]$layout.schemaVersion
    if ($layoutSchemaVersion -ne 2) {
        throw "Windows Smoke 保存的布局 schema 不是 2：$($layout.schemaVersion)。"
    }
    $legacyLayoutPath = Join-Path $dataRoot 'layout-v1.json'
    $legacyLayoutAbsent = -not (Test-Path -LiteralPath $legacyLayoutPath -PathType Leaf)
    if (-not $legacyLayoutAbsent) {
        throw 'Windows Smoke 隔离数据目录意外生成 layout-v1.json。'
    }

    $stopwatch.Stop()
    Write-Summary -Passed $true -ExitCode $exitCode -LayoutSaved $layoutSaved `
        -LayoutSchemaVersion $layoutSchemaVersion -LegacyLayoutAbsent $legacyLayoutAbsent `
        -ErrorMessage $null -Stopwatch $stopwatch
    Write-Host "Windows Smoke 通过；机器可读结果：$(Join-Path $resultsRoot 'summary.json')"
}
catch {
    $stopwatch.Stop()
    Write-Summary -Passed $false -ExitCode $exitCode -LayoutSaved $layoutSaved `
        -LayoutSchemaVersion $layoutSchemaVersion -LegacyLayoutAbsent $legacyLayoutAbsent `
        -ErrorMessage $_.Exception.Message -Stopwatch $stopwatch
    throw
}
finally {
    $env:MYAVALONIA_DATA_DIRECTORY = $previousDataDirectory
    $env:MYAVALONIA_SMOKE_TEST = $previousSmokeMode
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
    if ($process) { $process.Dispose() }
}
