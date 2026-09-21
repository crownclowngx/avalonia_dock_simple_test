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
# 输入是当前线格式的最小有效快照；实际解析和保存仍走 Host 实现，不复制 Store 的写锁逻辑。
$seedJson = '{"schemaVersion":3,"mainWindow":{"id":"main","bounds":{"x":80,"y":80,"width":1000,"height":700,"maximized":false,"screen":null},"root":{"kind":"documents","id":"Documents","proportion":1,"orientation":null,"children":[],"toolIds":[],"activeToolId":null}},"floatingWindows":[],"tools":[]}'
function Read-Snapshot([string]$json) {
    $stream = [IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($json))
    try {
        $jsonType = $assembly.GetType('MyAvaloniaManagement.Business.Layout.DockLayoutV3Json', $true)
        return $jsonType.GetMethod('Read', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, [object[]]@($stream))
    }
    finally { $stream.Dispose() }
}
function Save-Snapshot($store, $snapshot) { return $type.GetMethod('Save', $flags).Invoke($store, [object[]]@($snapshot)) }
function Load-Snapshot($store) { return $type.GetMethod('Load', $flags).Invoke($store, $null) }

if ($Mode -eq 'Hold') {
    $holderStore = New-Store $DataDirectory
    try {
        if (-not (Save-Snapshot $holderStore (Read-Snapshot $seedJson))) { throw '持锁进程未能保存 V3 输入。' }
        # 就绪信号只在取得写锁并提交有效文件后发出，父进程无需猜测启动耗时。
        [IO.File]::WriteAllText($SignalPath, (Can-Write $holderStore).ToString())
        # 父进程终止专属子进程，验证未执行 finally 时 OS 仍释放句柄。
        [Threading.Thread]::Sleep([Threading.Timeout]::Infinite)
    }
    finally { $holderStore.Dispose() }
    exit
}

$parentDirectory = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = [IO.Path]::GetFullPath((Join-Path $parentDirectory ('myavalonia-v20-lock-' + [Guid]::NewGuid().ToString('N'))))
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
    $layoutPath = Join-Path $first 'layout-v3.json'
    $original = [IO.File]::ReadAllText($layoutPath)
    $reader = New-Store $first
    if (Can-Write $reader) { throw '同根第二实例意外可写。' }
    $loaded = Load-Snapshot $reader
    if ($null -eq $loaded -or $loaded.SchemaVersion -ne 3 -or $loaded.MainWindow.Bounds.Width -ne 1000) { throw '只读实例未正确读取 V3。' }
    if (Save-Snapshot $reader $loaded) { throw '只读实例意外提交布局。' }
    if ([IO.File]::ReadAllText($layoutPath) -ne $original) { throw '只读实例改变了当前文件。' }
    $independent = New-Store $second
    if (-not (Can-Write $independent)) { throw '不同数据根互相干扰。' }
    if (-not (Save-Snapshot $independent $loaded)) { throw '独立数据根未能保存 V3。' }
    $child.Kill($true)
    $child.WaitForExit()
    if (Can-Write $reader) { throw '只读实例不得自动接管。' }
    if (Save-Snapshot $reader $loaded) { throw '持锁进程退出后旧只读实例意外提交。' }
    $successor = New-Store $first
    if (-not (Can-Write $successor)) { throw '崩溃后写锁未释放。' }
    $recovered = Load-Snapshot $successor
    if ($null -eq $recovered -or $recovered.SchemaVersion -ne 3 -or $recovered.MainWindow.Bounds.Width -ne 1000) { throw '新实例未正确读取持锁者留下的 V3。' }
    if (-not (Save-Snapshot $successor (Read-Snapshot ($seedJson.Replace('"width":1000', '"width":1200'))))) { throw '新实例未能提交 V3 更新。' }
    $saved = [IO.File]::ReadAllText($layoutPath) | ConvertFrom-Json
    if ($saved.schemaVersion -ne 3 -or $saved.mainWindow.bounds.width -ne 1200) { throw '新实例的 V3 输出不正确。' }
    if ([IO.File]::ReadAllText($layoutPath + '.bak') -ne $original) { throw '上一有效 V3 备份不正确。' }
    [ordered]@{ test = 'layout-v3-cross-process-lease'; passed = $true;
        checks = @('holder-ready-after-save', 'same-root-read-only', 'read-only-v3-load', 'read-only-save-rejected',
            'other-root-v3-save', 'old-reader-no-takeover', 'crash-releases-lease', 'new-writer-v3-load', 'new-writer-v3-save', 'previous-v3-backup');
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
