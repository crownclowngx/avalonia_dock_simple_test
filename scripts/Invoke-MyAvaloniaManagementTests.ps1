[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$WindowsSmoke,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath(
    (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ".."))
$artifactRoot = [IO.Path]::GetFullPath(
    (Join-Path $repoRoot "artifacts\test-results\MyAvaloniaManagement"))
$settings = Join-Path $repoRoot (
    "Host\MyAvaloniaManagement.Tests\coverage.runsettings")
$baselinePath = Join-Path $repoRoot (
    "Host\MyAvaloniaManagement.Tests\coverage-baseline.json")
$projects = @(
    @{
        Name = "Unit"
        Path = "Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj"
    },
    @{
        Name = "UI"
        Path = "Host\MyAvaloniaManagement.UiTests\MyAvaloniaManagement.UiTests.csproj"
    },
    @{
        Name = "Plugin"
        Path = "Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj"
    }
)

function Assert-ChildPath {
    param(
        [string]$Candidate,
        [string]$Parent
    )

    $resolvedCandidate = [IO.Path]::GetFullPath($Candidate)
    $resolvedParent = [IO.Path]::GetFullPath($Parent).TrimEnd(
        [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedCandidate.StartsWith(
        $resolvedParent,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside '$resolvedParent': $resolvedCandidate"
    }
}

function Invoke-DotNet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-TrxPassed {
    param(
        [string]$ResultDirectory,
        [string]$SuiteName
    )

    $trxPath = Get-ChildItem -LiteralPath $ResultDirectory -Recurse `
        -Filter "$SuiteName.trx" |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $trxPath) {
        throw "$SuiteName TRX result was not found."
    }

    [xml]$trx = Get-Content -LiteralPath $trxPath
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.failed -ne 0 -or
        [int]$counters.notExecuted -ne 0 -or
        [int]$counters.executed -ne [int]$counters.passed) {
        throw (
            "$SuiteName test gate failed: passed=$($counters.passed), " +
            "failed=$($counters.failed), notExecuted=$($counters.notExecuted).")
    }

    return [int]$counters.passed
}

function Get-FileLineCoverage {
    param(
        [object[]]$Classes,
        [string]$RelativePath
    )

    $matching = @($Classes | Where-Object {
        $_.filename.Replace("\", "/").EndsWith(
            $RelativePath,
            [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matching.Count -eq 0) {
        throw "Critical file is missing from coverage report: $RelativePath"
    }

    $lines = @($matching |
        ForEach-Object { $_.lines.line } |
        Group-Object number |
        ForEach-Object {
            [pscustomobject]@{
                Covered = @($_.Group | Where-Object {
                    [int]$_.hits -gt 0
                }).Count -gt 0
            }
        })
    if ($lines.Count -eq 0) {
        return 100.0
    }

    return [Math]::Round(
        100 * @($lines | Where-Object Covered).Count / $lines.Count,
        2)
}

function Invoke-WindowsSmoke {
    if ($env:OS -ne "Windows_NT") {
        throw "Windows smoke testing is only supported on Windows."
    }

    $smokeRoot = Join-Path $repoRoot "artifacts\MyAvaloniaManagement\smoke"
    $dataRoot = Join-Path $repoRoot "artifacts\MyAvaloniaManagement\smoke-data"
    Assert-ChildPath $smokeRoot $repoRoot
    Assert-ChildPath $dataRoot $repoRoot
    foreach ($path in @($smokeRoot, $dataRoot)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
        New-Item -ItemType Directory -Path $path | Out-Null
    }

    $publishArguments = @(
        "publish",
        (Join-Path $repoRoot "Host\MyAvaloniaManagement\MyAvaloniaManagement.csproj"),
        "-c", $Configuration,
        "-o", $smokeRoot,
        "-p:SkipPluginDeploy=true"
    )
    if ($NoRestore) {
        $publishArguments += "--no-restore"
    }
    Invoke-DotNet $publishArguments

    $executable = Join-Path $smokeRoot "MyAvaloniaManagement.exe"
    if (-not (Test-Path -LiteralPath $executable)) {
        throw "Published host executable was not found: $executable"
    }

    $previousDataDirectory = $env:MYAVALONIA_DATA_DIRECTORY
    $previousSmokeMode = $env:MYAVALONIA_SMOKE_TEST
    $process = $null
    try {
        $env:MYAVALONIA_DATA_DIRECTORY = $dataRoot
        $env:MYAVALONIA_SMOKE_TEST = "1"
        $process = Start-Process `
            -FilePath $executable `
            -WorkingDirectory $smokeRoot `
            -WindowStyle Hidden `
            -PassThru
        if (-not $process.WaitForExit(15000)) {
            throw (
                "Host did not open and shut down cleanly " +
                "within 15 seconds.")
        }
        if ($process.ExitCode -ne 0) {
            throw "Host smoke test exited with code $($process.ExitCode)."
        }
        if (-not (Test-Path -LiteralPath (
            Join-Path $dataRoot "layout-v1.json"))) {
            throw "Host smoke test did not save its isolated layout."
        }
    }
    finally {
        $env:MYAVALONIA_DATA_DIRECTORY = $previousDataDirectory
        $env:MYAVALONIA_SMOKE_TEST = $previousSmokeMode
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
        if ($process) {
            $process.Dispose()
        }
    }
}

Assert-ChildPath $artifactRoot $repoRoot
if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactRoot | Out-Null

Push-Location $repoRoot
try {
    Invoke-DotNet @("tool", "restore")
    $totalPassed = 0
    foreach ($suite in $projects) {
        $resultDirectory = Join-Path $artifactRoot $suite.Name
        New-Item -ItemType Directory -Path $resultDirectory | Out-Null
        $arguments = @(
            "test",
            (Join-Path $repoRoot $suite.Path),
            "-c", $Configuration,
            "-p:SkipPluginDeploy=true",
            "--settings", $settings,
            "--collect:XPlat Code Coverage",
            "--results-directory", $resultDirectory,
            "--logger", "trx;LogFileName=$($suite.Name).trx",
            "--logger", "console;verbosity=minimal"
        )
        if ($NoRestore) {
            $arguments += "--no-restore"
        }
        Invoke-DotNet $arguments
        $totalPassed += Assert-TrxPassed $resultDirectory $suite.Name
    }

    $coverageReports = @(Get-ChildItem -LiteralPath $artifactRoot -Recurse `
        -Filter coverage.cobertura.xml |
        Get-FileHash |
        Group-Object Hash |
        ForEach-Object { $_.Group[0].Path })
    if ($coverageReports.Count -ne $projects.Count) {
        throw (
            "Expected $($projects.Count) coverage reports, " +
            "found $($coverageReports.Count).")
    }

    $reportRoot = Join-Path $artifactRoot "coverage"
    & dotnet reportgenerator `
        "-reports:$($coverageReports -join ';')" `
        "-targetdir:$reportRoot" `
        "-reporttypes:Html;Cobertura;JsonSummary" `
        "-assemblyfilters:+MyAvaloniaManagement;-*.Tests" `
        "-filefilters:-*/obj/*;-*.g.cs;-*.g.i.cs"
    if ($LASTEXITCODE -ne 0) {
        throw "ReportGenerator failed with exit code $LASTEXITCODE."
    }

    $coveragePath = Join-Path $reportRoot "Cobertura.xml"
    [xml]$coverage = Get-Content -LiteralPath $coveragePath
    $line = [Math]::Round(100 * [double]$coverage.coverage.'line-rate', 2)
    $branch = [Math]::Round(100 * [double]$coverage.coverage.'branch-rate', 2)
    $baseline = Get-Content -LiteralPath $baselinePath -Raw |
        ConvertFrom-Json
    if ($line -lt [double]$baseline.overall.line) {
        throw (
            "Host line coverage $line% is below " +
            "$($baseline.overall.line)%.")
    }
    if ($branch -lt [double]$baseline.overall.branch) {
        throw (
            "Host branch coverage $branch% is below " +
            "$($baseline.overall.branch)%.")
    }

    $classes = @($coverage.coverage.packages.package.classes.class)
    foreach ($property in $baseline.criticalFiles.PSObject.Properties) {
        $actual = Get-FileLineCoverage $classes $property.Name
        if ($actual -lt [double]$property.Value) {
            throw (
                "Critical file $($property.Name) line coverage $actual% " +
                "is below $($property.Value)%.")
        }
    }

    if ($WindowsSmoke) {
        Invoke-WindowsSmoke
    }

    $summary = [ordered]@{
        passed = $totalPassed
        lineCoverage = $line
        branchCoverage = $branch
        windowsSmoke = [bool]$WindowsSmoke
        generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    }
    $summary | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $artifactRoot "summary.json") `
            -Encoding utf8
    Write-Host (
        "Passed: $totalPassed; host line coverage $line%; " +
        "branch coverage $branch%; results: $artifactRoot")
}
finally {
    Pop-Location
}
