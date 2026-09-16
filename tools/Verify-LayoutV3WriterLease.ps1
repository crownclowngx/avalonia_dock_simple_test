param(
    [ValidateSet('Verify', 'Hold')][string]$Mode = 'Verify',
    [string]$DataDirectory,
    [string]$SignalPath,
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '../Host/MyAvaloniaManagement/bin/Release/net10.0/MyAvaloniaManagement.dll')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# 专用开发验证：只加载当前 Store，不启动 Avalonia、CI、Smoke 或发布流程。
# 反射仅用于 internal 测试边界，不向生产 API 增加测试开关。
if ([Environment]::Version.Major -lt 10) { throw '需要使用 .NET 10 的 PowerShell 7.6 或更新版本。' }
$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($AssemblyPath))
$type = $assembly.GetType('MyAvaloniaManagement.Business.Layout.DockLayoutV3Store', $true)
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$constructor = $type.GetConstructors($flags)[0]
function New-Store([string]$directory) { return $constructor.Invoke([object[]]@($directory, $null)) }
function Can-Write($store) { return $type.GetProperty('CanWrite', $flags).GetValue($store) }

if ($Mode -eq 'Hold') {
    $holderStore = New-Store $DataDirectory
    try {
        [IO.File]::WriteAllText($SignalPath, (Can-Write $holderStore).ToString())
        # 父进程终止专属子进程，验证未执行 finally 时 OS 仍释放句柄。
        [Threading.Thread]::Sleep([Threading.Timeout]::Infinite)
    }
    finally { $holderStore.Dispose() }
    exit
}

$parentDirectory = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = [IO.Path]::GetFullPath((Join-Path $parentDirectory ('myavalonia-v11-lock-' + [Guid]::NewGuid().ToString('N'))))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$first = Join-Path $testRoot 'first'
$second = Join-Path $testRoot 'second'
$ready = Join-Path $testRoot 'ready.txt'
$child = $null
$reader = $null
$independent = $null
$successor = $null
try {
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($argument in @('-NoProfile', '-File', $PSCommandPath, '-Mode', 'Hold', '-DataDirectory', $first,
            '-SignalPath', $ready, '-AssemblyPath', [IO.Path]::GetFullPath($AssemblyPath))) { $start.ArgumentList.Add($argument) }
    $child = [Diagnostics.Process]::Start($start)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $ready)) {
        if ($child.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw '写入者未在限时内就绪。' }
        Start-Sleep -Milliseconds 20
    }
    if ((Get-Content -LiteralPath $ready -Raw) -ne 'True') { throw '子进程未取得写入权。' }
    $legacy = Join-Path $first 'layout-v2.json'
    $original = '{"schemaVersion":2,"panes":[],"tools":[],"activeToolId":null}'
    [IO.File]::WriteAllText($legacy, $original)
    $reader = New-Store $first
    if (Can-Write $reader) { throw '同根第二实例意外可写。' }
    if ($null -ne $type.GetMethod('Load', $flags).Invoke($reader, $null)) { throw '只读实例不应迁移 V2。' }
    $independent = New-Store $second
    if (-not (Can-Write $independent)) { throw '不同数据根互相干扰。' }
    $child.Kill($true)
    $child.WaitForExit()
    if (Can-Write $reader) { throw '只读实例不得自动接管。' }
    $successor = New-Store $first
    if (-not (Can-Write $successor)) { throw '崩溃后写锁未释放。' }
    $converted = $type.GetMethod('Load', $flags).Invoke($successor, $null)
    if ($null -eq $converted -or $converted.SchemaVersion -ne 3) { throw '新实例未能只读转换 V2。' }
    if ([IO.File]::ReadAllText($legacy) -ne $original) { throw 'V2 原件被改写。' }
    [ordered]@{ test = 'layout-v3-cross-process-lease'; passed = $true; checks = 7;
        assemblySha256 = (Get-FileHash -LiteralPath $AssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant() } | ConvertTo-Json
}
finally {
    foreach ($store in @($successor, $independent, $reader)) { if ($null -ne $store) { $store.Dispose() } }
    if ($null -ne $child) { if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit() }; $child.Dispose() }
    # 清理前检查解析后的绝对路径，禁止删除临时目录以外的数据。
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith($parentDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) { throw '拒绝清理越界路径。' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
