# 真实媒体测试资产

## 1. 目的与边界

`MySmallTools.Tests/TestAssets/RealMedia/` 保存可直接随测试仓库取得的真实 MP4 和 WebM 文件。它们用于替代开发机私人视频，并为后续 SECVID03 加密、解密、LibVLC 解码和 Seek 集成测试提供固定输入。

G0 自动化验证资产清单、来源、授权、容器签名、长度和 SHA-256。G3 已使用同一资产完成真实 LibVLC 解码、播放、三块读取、Seek、Dock 和解密还原门禁，证据见 [G3 真实播放与 Dock 稳定性](G3-REAL-MEDIA-PLAYBACK-DOCK-STABILITY.md)。

## 2. 资产矩阵

| 文件 | 媒体属性 | 覆盖目的 |
| --- | --- | --- |
| `synthetic-av-short.mp4` | 3 秒、320×180、H.264、AAC 单声道 | MP4、有声、短时长 |
| `synthetic-silent-multiblock.webm` | 6 秒、640×360、VP9、无音轨 | WebM、无声、跨至少三个 1 MiB 明文块 |

机器可读的字节数、SHA-256、生成器版本和完整命令只维护在 [`manifest.json`](../../../MySmallTools.Tests/TestAssets/RealMedia/manifest.json) 中，避免文档和测试各自维护一份哈希。

两份媒体均由 FFmpeg 合成源生成，不包含私人视频或第三方视听作品，按 `CC0-1.0` 提供；详细声明见资产目录中的 `ASSET-LICENSE.md`。

## 3. 固定生成环境

- FFmpeg：`8.1.2-essentials_build-www.gyan.dev`
- Windows 构建来源：FFmpeg 官方下载页列出的 gyan.dev release essentials 构建
- 视频源：`testsrc2`；WebM 额外使用固定种子 `noise` 滤镜
- 音频源：1000 Hz `sine`，仅用于 MP4
- 所有外部元数据在编码时移除

生成命令以清单为准。生成后必须使用同一构建中的 `ffprobe` 检查轨道和时长：

```powershell
ffprobe -v error -show_entries "format=format_name,duration,size:stream=index,codec_name,codec_type,width,height,channels" -of json synthetic-av-short.mp4
ffprobe -v error -show_entries "format=format_name,duration,size:stream=index,codec_name,codec_type,width,height,channels" -of json synthetic-silent-multiblock.webm
```

## 4. 更新流程

1. 只使用清单中的固定版本和生成命令重新生成目标文件。
2. 用 `ffprobe` 确认容器、编码、轨道、分辨率和时长；多块 WebM 必须大于 `2 × Secvid03Format.ChunkSize`。
3. 更新清单中的实际字节数和大写 SHA-256，不在测试代码中复制这些值。
4. 串行运行 `MySmallTools.Tests` 和宿主插件测试；不得让测试动态下载或生成媒体。
5. 若素材用途、生成器或授权发生变化，同步更新本页和 `ASSET-LICENSE.md`。
