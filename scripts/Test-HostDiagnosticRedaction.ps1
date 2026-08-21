[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$productionRoots = @((Join-Path $repositoryRoot 'Host\MyAvaloniaManagement'))
$approvedSensitiveSources = @(
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'Host\MyAvaloniaManagement\Business\Diagnostics\HostDiagnostics.cs'))
)

function Get-ProductionSourceFiles {
    @($productionRoots | ForEach-Object {
        Get-ChildItem -LiteralPath $_ -Recurse -File -Filter '*.cs' |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    })
}

function Add-Matches {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.List[object]]$Findings,
        [Parameter(Mandatory)] [IO.FileInfo[]]$Files,
        [Parameter(Mandatory)] [string]$Pattern,
        [Parameter(Mandatory)] [string]$Reason
    )

    foreach ($match in @($Files | Select-String -Pattern $Pattern -CaseSensitive)) {
        $Findings.Add([pscustomobject]@{
            Path = $match.Path
            Line = $match.LineNumber
            Reason = $Reason
            Text = $match.Line.Trim()
        })
    }
}

$sources = @(Get-ProductionSourceFiles)
$defaultSources = @($sources | Where-Object {
    $approvedSensitiveSources -notcontains [IO.Path]::GetFullPath($_.FullName)
})
$findings = [Collections.Generic.List[object]]::new()

# 默认生产路径不得读取异常正文。这里只允许 Host 的显式调试出口保留原始异常；
# PluginLoadExceptionMapper 对 current.Message 中稳定错误码的只读识别不属于输出，故不匹配下列规则。
Add-Matches $findings $defaultSources `
    '(exception|ex)(\?\.)?\.Message|(exception|ex)\.ToString\(\)|\{(exception|ex)\}' `
    '默认生产路径读取或格式化了异常原文'

$technicalDetailSources = @($sources | Where-Object {
    [IO.Path]::GetFullPath($_.FullName) -ne $approvedSensitiveSources[0]
})
Add-Matches $findings $technicalDetailSources `
    'TechnicalDetail\s*=' `
    '统一白名单转换之外写入了自由格式 TechnicalDetail'

Add-Matches $findings $defaultSources `
    'Console\.(Error\.)?WriteLine\([^\r\n]*(filePath|rootPath|pluginDirectory)|文件不存在\s*:' `
    '默认控制台输出包含路径变量或原始路径提示'

$environmentMatches = @($sources | Select-String `
    -SimpleMatch 'MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS' -CaseSensitive)
foreach ($match in $environmentMatches) {
    if ($approvedSensitiveSources -notcontains [IO.Path]::GetFullPath($match.Path)) {
        $findings.Add([pscustomobject]@{
            Path = $match.Path
            Line = $match.LineNumber
            Reason = '敏感开关出现在未经批准的生产实现中'
            Text = $match.Line.Trim()
        })
    }
}

$hostDiagnosticSource = Get-Content -Raw -LiteralPath $approvedSensitiveSources[0]
# G13 删除 Legacy 生产面后，敏感诊断只剩 HostDiagnostics 这一个所有者。
# 这里直接验证唯一出口，不再使用“Host + Legacy”并行数组，避免门禁暗示第二套契约仍然存在。
if ($hostDiagnosticSource -notmatch '"MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS"' -or
    $hostDiagnosticSource -notmatch '"1"') {
    throw '敏感诊断开关必须使用固定名称并只接受精确值 1。'
}
if ($hostDiagnosticSource -match 'internal\s+string\?\s+TechnicalDetail') {
    throw 'HostDiagnosticDraft 不得重新暴露自由格式 TechnicalDetail。'
}
$draftDeclaration = [regex]::Match(
    $hostDiagnosticSource,
    'internal sealed record HostDiagnosticDraft\((?<parameters>.*?)\)\s*\{',
    [Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $draftDeclaration.Success -or
    $draftDeclaration.Groups['parameters'].Value -match 'UserMessage|TechnicalDetail') {
    throw 'HostDiagnosticDraft 只能接收错误码和阶段，不得重新暴露自由用户说明或技术详情。'
}

if ($findings.Count -gt 0) {
    $findings | Sort-Object Path, Line | ForEach-Object {
        Write-Host ("{0}:{1}: {2}`n  {3}" -f $_.Path, $_.Line, $_.Reason, $_.Text)
    }
    throw "G15 诊断脱敏源码门禁失败，共发现 $($findings.Count) 项。"
}

Write-Host (
    "G15 诊断脱敏源码门禁通过：检查 $($sources.Count) 个生产 C# 文件；" +
    '默认路径无异常正文、自由技术详情和完整路径输出。')
