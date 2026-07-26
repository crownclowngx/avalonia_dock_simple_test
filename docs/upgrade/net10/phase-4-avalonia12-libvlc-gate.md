# 阶段 4：Avalonia 12 与 LibVLCSharp 兼容性闸门

## 固定版本

- .NET 10
- Avalonia / Semi 12.1.0
- Ursa / Ursa Semi 2.1.0
- Dock 12.0.0.2
- Xaml.Behaviors 12.0.5
- StaticViewLocator 0.4.0
- LibVLCSharp 3.10.0
- VideoLAN.LibVLC.Windows 3.0.23.1

## 自动闸门

必须在交互式 Windows x64 桌面、clean commit 上执行：

```powershell
.\scripts\Invoke-MySmallToolsPhase4.ps1
```

脚本依次执行 locked restore、依赖图检查、Release 零警告构建、全解决方案测试、G3 真实窗口矩阵和 G8 八 Document 隔离矩阵。主报告为
`TestResults/Phase4/avalonia12-libvlcsharp.json`。

资源起点在一次同规模、覆盖完整 G3/G8 路径的进程内预热之后采集。
预热只排除 Avalonia、D3D11 与 LibVLC 的一次性初始化成本；正式的 100 次生命周期和
8 Document 矩阵仍完整执行，终点继续使用 Handle `+10` 与私有内存 `+64 MiB` 硬闸门。

自动报告必须同时满足：

- `success=true`，`manualSignoff=pending`；
- 真实原生表面描述符为 `HWND` 且句柄非零；
- Surface 创建数等于销毁数，活动 Surface 为零；
- Player、Media、输入、加密流、缓存和原生调度资源归零；
- 最终 Handle Count 不高于起点 `+10`；
- 最终私有内存不高于起点 `+64 MiB`；
- 未处理异常、黑屏、vout 错误和超时均为零；
- G3 与 G8 子报告属于同一次运行。

若句柄总数失败，报告中的 `handleTypesStart`、`handleTypesFinal` 和
`handleTypeDeltas` 只记录内核对象类型与数量，不记录文件名、路径或句柄值。

任何一项失败即为 NO-GO，不得调整阈值或跳过测试。

## 人工清单

自动闸门通过后，在同一源码提交和同一发布输出上人工确认：

1. MP4 画面持续可见，没有黑屏或独立顶层视频窗口。
2. 音频可听，音轨切换正确。
3. 全屏进入、退出和 Esc 均正常，返回后画面继续输出。
4. Dock 隐藏/显示、切换、浮动/停靠及结构布局恢复正常。

确认后执行：

```powershell
.\scripts\Approve-MySmallToolsPhase4.ps1 `
  -Approver '验收人' `
  -ConfirmPicture -ConfirmAudio -ConfirmFullscreen -ConfirmDockRestore
```

只有生成 `avalonia12-libvlcsharp-go.json` 且 `decision=GO` 后，阶段 5–8 才能形成正式发布候选。人工确认无法由自动化或 Headless 环境替代。
