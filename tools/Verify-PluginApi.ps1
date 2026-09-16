param(
    [string]$OutputDirectory,
    [switch]$RunSelfTests
)
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$baseline = Get-Content (Join-Path $repo 'build/ApiBaseline/3.4.1.json') -Raw | ConvertFrom-Json
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo ('artifacts/api-compat/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$cache = Join-Path $repo 'artifacts/api-baseline/3.4.1'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$results = [Collections.Generic.List[object]]::new()
$allPassed = $true

# 设计思路：复用固定版本的微软比较器，不解释 public 签名。这里仅负责已发布输入的哈希、
# 工具参数和证据归档；默认方向是“已发布 → 当前”，兼容新增允许，严格比较仅作分类。
# 不调用 seal，不改写 Shipped/Unshipped，不生成 suppression，不关闭任何兼容规则。
function Compare-Api([string]$left, [string]$right, [string]$name, [bool]$strict = $false) {
    $arguments = @('apicompat', '-l', $left, '-r', $right, '--enable-rule-cannot-change-parameter-name')
    if ($strict) { $arguments += '--strict-mode' }
    $lines = & dotnet @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $lines | Set-Content (Join-Path $output ($name + '.log')) -Encoding utf8
    return $exitCode
}

Push-Location $repo
try {
    foreach ($package in $baseline.packages) {
        $id = $package.id.ToLowerInvariant()
        $archive = Join-Path $cache ($id + '.3.4.1.nupkg')
        if (-not (Test-Path -LiteralPath $archive)) {
            Invoke-WebRequest -Uri $package.source -OutFile $archive
        }
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $package.packageSha256) {
            throw "已发布包摘要不匹配：$id；保留输入供调查，不替换基线。"
        }
        $extracted = Join-Path $cache $package.id
        if (-not (Test-Path -LiteralPath $extracted)) { [IO.Compression.ZipFile]::ExtractToDirectory($archive, $extracted) }
        $left = Join-Path $extracted ('lib/net10.0/' + $package.id + '.dll')
        if ((Get-FileHash -LiteralPath $left -Algorithm SHA256).Hash -ne $package.assemblySha256) { throw "基线程序集摘要不匹配：$id" }
        $right = Join-Path $repo ('Host/' + $package.id + '/bin/Release/net10.0/' + $package.id + '.dll')
        $compatible = (Compare-Api $left $right $id) -eq 0
        $equal = $false
        if ($compatible) { $equal = (Compare-Api $left $right ($id + '-strict') $true) -eq 0 }
        $allPassed = $allPassed -and $compatible
        $results.Add([ordered]@{ package = $package.id; baselineVersion = '3.4.1'; compatible = $compatible;
            equalSurface = $equal; baselineSha256 = $package.assemblySha256; currentSha256 = (Get-FileHash $right).Hash })
    }
    if ($RunSelfTests) {
        # 四份受控微型程序集验证工具真的能检出删除和改签名；这些文件仅位于本次证据目录。
        $sources = [ordered]@{
            old = 'public class Contract { public int Read(int value) => value; }'
            added = 'public class Contract { public int Read(int value) => value; public void Added() {} }'
            removed = 'public class Contract { }'
            changed = 'public class Contract { public int Read(string value) => value.Length; }'
        }
        foreach ($name in $sources.Keys) {
            $directory = Join-Path $output ('fixtures/' + $name)
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>ApiProbe</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup><Compile Include="Contract.cs" /></ItemGroup></Project>' |
                Set-Content (Join-Path $directory 'ApiProbe.csproj') -Encoding utf8
            $sources[$name] | Set-Content (Join-Path $directory 'Contract.cs') -Encoding utf8
            & dotnet build (Join-Path $directory 'ApiProbe.csproj') -c Release -warnaserror -m:1 *> (Join-Path $output ($name + '-build.log'))
            if ($LASTEXITCODE -ne 0) { throw "API 自测夹具构建失败：$name" }
        }
        $old = Join-Path $output 'fixtures/old/bin/Release/net10.0/ApiProbe.dll'
        foreach ($name in @('added', 'removed', 'changed')) {
            $right = Join-Path $output ('fixtures/' + $name + '/bin/Release/net10.0/ApiProbe.dll')
            $compatible = (Compare-Api $old $right ('self-' + $name)) -eq 0
            $expected = $name -eq 'added'
            $correct = $compatible -eq $expected
            if ($name -eq 'added') { $correct = $correct -and ((Compare-Api $old $right 'self-added-strict' $true) -ne 0) }
            $allPassed = $allPassed -and $correct
            $results.Add([ordered]@{ selfTest = $name; expectedCompatible = $expected; actualCompatible = $compatible; passed = $correct })
        }
    }
}
catch { $allPassed = $false; $results.Add([ordered]@{ error = $_.Exception.Message }) }
finally {
    Pop-Location
    [ordered]@{ passed = $allPassed; toolVersion = '10.0.302'; checkedAtUtc = [DateTime]::UtcNow.ToString('o'); results = $results } |
        ConvertTo-Json -Depth 10 | Set-Content (Join-Path $output 'summary.json') -Encoding utf8
}
Write-Host "开发 API 比较证据：$output"
if (-not $allPassed) { throw '开发 API 比较或工具自测失败；请检查 summary.json 和日志。' }
