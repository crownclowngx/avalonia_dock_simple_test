[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssetsFile,
    [Parameter(Mandatory)][string]$TargetFramework,
    [Parameter(Mandatory)][string]$OutputFile
)
$ErrorActionPreference = 'Stop'
# 只读取本次还原事实。不得记录时间、随机数或个人路径，否则相同输入不能产生相同插件包。
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json -AsHashtable
$packages = @($assets.libraries.GetEnumerator() | Where-Object { $_.Value.type -eq 'package' } |
    ForEach-Object {
        $split = $_.Key.LastIndexOf('/')
        [ordered]@{ id = $_.Key.Substring(0, $split); version = $_.Key.Substring($split + 1) }
    } | Sort-Object { $_.id })
$build = @($packages | Where-Object { $_.id -eq 'MyAvaloniaManagement.Plugin.Build' })
$toolVersion = if ($build.Count -eq 1) { $build[0].version } else { 'source-v10' }
$result = [ordered]@{ schemaVersion = 1; targetFramework = $TargetFramework; buildVersion = $toolVersion; packages = $packages }
$json = ($result | ConvertTo-Json -Depth 6).Replace("`r`n", "`n") + "`n"
$full = [IO.Path]::GetFullPath($OutputFile)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full)) | Out-Null
[IO.File]::WriteAllText($full, $json, [Text.UTF8Encoding]::new($false))
