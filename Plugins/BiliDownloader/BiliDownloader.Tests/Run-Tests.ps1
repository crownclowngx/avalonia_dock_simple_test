[CmdletBinding()]
param(
    [switch]$KeepResults,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$testDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $testDirectory "BiliDownloader.Tests.csproj"
$settings = Join-Path $testDirectory "coverage.runsettings"
$baselinePath = Join-Path $testDirectory "coverage-baseline.json"
$resultRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "BiliDownloader.Tests-" + [Guid]::NewGuid().ToString("N"))

function Get-CoverageCounts {
    param([object[]]$Classes)

    $files = $Classes |
        Where-Object {
            $normalized = $_.filename.Replace("\", "/")
            $normalized -like "*/Plugins/BiliDownloader/BiliDownloader/*" -and
            $normalized -notmatch "/obj/|/Views/"
        } |
        Group-Object filename |
        ForEach-Object {
            $lines = @($_.Group.lines.line)
            $branchCovered = 0
            $branchValid = 0
            foreach ($line in $lines) {
                if ($line.'condition-coverage' -match "\((\d+)/(\d+)\)") {
                    $branchCovered += [int]$Matches[1]
                    $branchValid += [int]$Matches[2]
                }
            }

            [pscustomobject]@{
                File = $_.Name.Replace("\", "/")
                LineCovered = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
                LineValid = $lines.Count
                BranchCovered = $branchCovered
                BranchValid = $branchValid
            }
        }

    return @($files)
}

function Get-Metric {
    param([object[]]$Files)

    $lineCovered = ($Files | Measure-Object LineCovered -Sum).Sum
    $lineValid = ($Files | Measure-Object LineValid -Sum).Sum
    $branchCovered = ($Files | Measure-Object BranchCovered -Sum).Sum
    $branchValid = ($Files | Measure-Object BranchValid -Sum).Sum

    [pscustomobject]@{
        Line = if ($lineValid -gt 0) {
            [Math]::Round(100 * $lineCovered / $lineValid, 2)
        } else { 100.0 }
        Branch = if ($branchValid -gt 0) {
            [Math]::Round(100 * $branchCovered / $branchValid, 2)
        } else { 100.0 }
        LineCovered = $lineCovered
        LineValid = $lineValid
        BranchCovered = $branchCovered
        BranchValid = $branchValid
    }
}

function Assert-Minimum {
    param(
        [string]$Name,
        [object]$Actual,
        [double]$MinimumLine,
        [double]$MinimumBranch
    )

    if ($Actual.Line -lt $MinimumLine) {
        throw "$Name line coverage $($Actual.Line)% is below $MinimumLine%."
    }
    if ($Actual.Branch -lt $MinimumBranch) {
        throw "$Name branch coverage $($Actual.Branch)% is below $MinimumBranch%."
    }
}

try {
    New-Item -ItemType Directory -Path $resultRoot | Out-Null
    $arguments = @(
        "test",
        $project,
        "-c", "Release",
        "-p:SkipPluginDeploy=true",
        "--settings", $settings,
        "--collect:XPlat Code Coverage",
        "--results-directory", $resultRoot,
        "--logger", "trx;LogFileName=results.trx"
    )
    if ($NoRestore) {
        $arguments += "--no-restore"
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "BiliDownloader.Tests failed with exit code $LASTEXITCODE."
    }

    $trxPath = Get-ChildItem $resultRoot -Recurse -Filter results.trx |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $trxPath) {
        throw "TRX test result was not found."
    }
    [xml]$trx = Get-Content -LiteralPath $trxPath
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.failed -ne 0 -or
        [int]$counters.notExecuted -ne 0 -or
        [int]$counters.executed -ne [int]$counters.passed) {
        throw "Test gate failed: passed=$($counters.passed), failed=$($counters.failed), notExecuted=$($counters.notExecuted)."
    }

    $coveragePath = Get-ChildItem $resultRoot -Recurse -Filter coverage.cobertura.xml |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $coveragePath) {
        throw "Cobertura coverage report was not found."
    }
    [xml]$coverage = Get-Content -LiteralPath $coveragePath
    $files = Get-CoverageCounts $coverage.coverage.packages.package.classes.class
    $baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json

    $patterns = @{
        A = "Services/Auth/|Services/Persistence/|Services/Download/BiliDownloadCoordinator.cs|Services/Download/DownloadProgressTracker.cs|Services/Infrastructure/SensitiveDataSanitizer.cs|Services/Infrastructure/BiliLocalStateInitializer.cs|Models/DownloadTaskStatus.cs"
        B = "Services/Api/|Services/ContentSources/|Services/Download/"
        C = "ViewModels/|Converters/|Create/"
    }

    $overall = Get-Metric $files
    $groupA = Get-Metric @($files | Where-Object { $_.File -match $patterns.A })
    $groupB = Get-Metric @($files | Where-Object { $_.File -match $patterns.B })
    $groupC = Get-Metric @($files | Where-Object { $_.File -match $patterns.C })

    Assert-Minimum "Group A" $groupA $baseline.groups.A.line $baseline.groups.A.branch
    Assert-Minimum "Group B" $groupB $baseline.groups.B.line $baseline.groups.B.branch
    Assert-Minimum "Group C" $groupC $baseline.groups.C.line $baseline.groups.C.branch

    $overallMinimumLine = [double]$baseline.overall.line - [double]$baseline.overall.tolerance
    $overallMinimumBranch = [double]$baseline.overall.branch - [double]$baseline.overall.tolerance
    Assert-Minimum "Filtered overall" $overall $overallMinimumLine $overallMinimumBranch

    foreach ($relativePath in $baseline.criticalFiles) {
        $file = $files | Where-Object {
            $_.File.Replace("\", "/").EndsWith(
                $relativePath,
                [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1
        if (-not $file) {
            throw "Critical file is missing from coverage report: $relativePath"
        }
        $metric = Get-Metric @($file)
        if ($metric.Line -lt [double]$baseline.criticalFileMinimumLine) {
            throw "Critical file $relativePath line coverage $($metric.Line)% is below $($baseline.criticalFileMinimumLine)%."
        }
    }

    Write-Host (
        "Passed: {0}/{1}; overall line {2}% / branch {3}%; A {4}%/{5}%; B {6}%/{7}%; C {8}%/{9}%" -f
        $counters.passed, $counters.total,
        $overall.Line, $overall.Branch,
        $groupA.Line, $groupA.Branch,
        $groupB.Line, $groupB.Branch,
        $groupC.Line, $groupC.Branch)

    if ($KeepResults) {
        Write-Host "Test and coverage results: $resultRoot"
    }
}
finally {
    if (-not $KeepResults -and (Test-Path -LiteralPath $resultRoot)) {
        $resolvedResult = [IO.Path]::GetFullPath($resultRoot)
        $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedResult.StartsWith(
            $resolvedTemp,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean a path outside the temporary directory: $resolvedResult"
        }
        Remove-Item -LiteralPath $resolvedResult -Recurse -Force
    }
}
