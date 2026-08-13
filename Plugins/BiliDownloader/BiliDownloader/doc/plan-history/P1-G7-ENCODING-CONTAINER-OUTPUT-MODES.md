# P1-G7：编码、容器与输出模式实施记录

> 实施日期：2026-08-07  
> 状态：已实现并通过单元测试、覆盖率门禁和离线 ffprobe 媒体验收  
> 范围：AVC、HEVC、AV1；MP4、MKV、原生 AAC；音视频、仅视频、仅音频

## 1. 目标与边界

P1-G7 把“用户选择的输出意图”贯通到 DASH 解析、预检、任务事实、下载、恢复、封装、历史和界面。所有视频输出使用 stream copy，不转码、不补帧，也不在显式编码不可用时静默选择其他编码。

本组只主动选择普通 AAC/MP4A 音频。杜比和 Hi-Res 流会保留来源、编码、MIME 和容器提示，但主动选择推迟到 P1-G8。字幕、弹幕格式增强和软字幕属于 P1-G9，限速属于 P1-G10。

## 2. SOLID 职责划分

| 边界 | 单一职责 | 副作用 |
| --- | --- | --- |
| `IMediaStreamSelectionPolicy` | 在指定画质内选择明确编码和普通音频 | 无 |
| `IOutputArtifactPolicy` | 合法组合、扩展名和身份规范化 | 无 |
| `IMediaSizeCalculator` | 根据已选流与时长估算峰值空间 | 无 |
| `IMediaPreflightAnalyzer` | 一次 DASH 请求组合选择与估算 | 网络，只返回安全计划 |
| `IMediaMuxerCapabilityProvider` | 探测并按已验证 ffmpeg 路径缓存 muxer 能力 | 本地进程 |
| `IMediaMuxer` | 使用明确输入集执行 stream copy | 本地进程、staging 文件 |
| `INativeAudioPublisher` | 同目录 staging、落盘和原子发布 | 文件系统 |
| Coordinator | 任务 ID、状态与事实持久化、恢复编排 | SQLite、调度 |

这些边界使选择规则不依赖网络或 ffmpeg，预检不拥有任务写入权，下载器不解释 UI 文案，文件发布器也不知道 DASH 或任务状态。

## 3. 选择与输出矩阵

### 3.1 视频编码

- 只考察用户选定 `VideoQualityId` 的流，不跨画质兜底。
- `AutoCompatibility` 固定按 AVC、HEVC、AV1 查找；同编码取最高带宽。
- 显式 AVC、HEVC 或 AV1 不存在时返回 `ExplicitVideoCodecUnavailable` 和该画质实际可用编码集合。
- `codecid` 与 `codecs` 同时存在但矛盾时按未知处理；只有单一可信来源时允许识别。

### 3.2 输出组合

| 模式 | 视频 | 音频 | 容器 | 扩展名 | ffmpeg |
| --- | --- | --- | --- | --- | --- |
| 音视频 | 必需 | 普通 AAC 必需 | MP4 / MKV | `.mp4` / `.mkv` | 必需 |
| 仅视频 | 必需 | 不选择、不下载 | MP4 / MKV | `.mp4` / `.mkv` | 必需 |
| 仅音频 | 不选择、不下载 | 普通 AAC 必需 | NativeAudio | `.m4a` | 不需要 |

仅音频身份会清除视频画质和编码，AV视频身份会清除音频质量，避免不可见设置制造重复 rendition。

## 4. 预检、身份与敏感数据边界

`DashMediaPreflightAnalyzer` 每个项目只请求一次 DASH，在同一个响应上完成流选择和峰值空间估算。返回的 `MediaOutputPlan` 只包含实际编码、模式、容器、扩展名和带宽，不持有 URL、Cookie 或请求头。预检指纹纳入实际编码和输出计划，但不会把临时签名地址写入报告或 SQLite。

Prepared 提交为每个新任务创建独立 GUID `TaskId`；`DownloadSubmissionItem.ItemId` 只关联当前 Document 会话。`SubmissionCommitResult.EffectiveCommittedTasks` 返回会话项到任务 ID 的映射，Document 据此接收后续进度；跨来源重复仍由 `RenditionFingerprint` 阻止。

旧 V1/V2 Document 和旧任务按 AutoCompatibility、MP4、AudioVideo 读取。旧完成任务的实际编码保持空值；只有未完成任务真正重新解析 DASH 后，下载前回调才调用仓储窄方法更新 `actual_video_codec`。

## 5. 下载、进度与原子发布

ffmpeg 请求显式使用 `-map` 和 `-c copy`：音视频映射 `0:v:0` 与 `1:a:0`；仅视频映射 `0:v:0` 并使用 `-an`。任何生产参数都不包含视频或音频编码器。

下载器只创建当前模式需要的临时流。不存在的流使用预期字节 0、完整性 false 的内存事实，不创建空文件。进度权重如下：

- 音视频：视频 45%、音频 45%、封装 10%。
- 仅视频：视频 90%、封装 10%，音频阶段不适用。
- 仅音频：音频 100%，视频与封装阶段不适用。

NativeAudio 不启动 ffmpeg。`NativeAudioPublisher` 在最终目录写入 staging，刷新到磁盘后以同卷移动作为唯一可见性切换点；失败或取消会清理 staging。MP4/MKV 同样在最终目录生成带真实扩展名的 staging，再原子移动到保留路径。

## 6. 恢复规则

| 操作 | 音视频 | 仅视频 | 仅音频 |
| --- | --- | --- | --- |
| 暂停/恢复/普通重试/重新开始 | 支持 | 支持 | 支持 |
| 检查点要求 | 可信视频与音频 | 可信视频 | 可信音频 |
| 仅重试合并 | 支持 | 支持 | 明确拒绝 |
| 完成阶段进度 | 100/100/100 | 100/不适用/100 | 不适用/100/不适用 |

旧快照继续使用宽松的断点发现规则，避免升级后丢失可恢复数据；新快照严格按模式验证预期长度、完整性和临时文件集合。

## 7. 界面与历史

下载配置提供中文编码、容器和输出模式。切换仅音频会自动选择 NativeAudio 并禁用视频画质/编码；切回视频模式恢复此前 MP4/MKV。仅视频会禁用音频质量。预检仍会拒绝绕过 UI 手工构造的非法 Submission。

活动任务和历史中心显示“用户选择 → 实际编码”、容器与模式。仅音频显示“不适用”，旧任务显示“未知”，任何展示都不根据文件名猜测实际编码或容器。

## 8. 验收命令与证据

普通 Release 与覆盖率门禁：

```powershell
cd Plugins\BiliDownloader\BiliDownloader.Tests
.\Run-Tests.ps1 -NoRestore
```

固定版本离线媒体验收（不需要 Bilibili 凭据）：

```powershell
dotnet run --project ..\BiliDownloader.ReleaseAcceptance\BiliDownloader.ReleaseAcceptance.csproj `
  -c Release -- media-output `
  --ffmpeg <ffmpeg-8.1.2\bin\ffmpeg.exe> `
  --ffprobe <ffmpeg-8.1.2\bin\ffprobe.exe> `
  --sandbox <临时目录> --report <报告.json>
```

2026-08-07 证据：固定清单 SHA-256 `db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec` 验证通过；生产 muxer 对 AVC/MP4、HEVC/MKV、AV1/仅视频 MKV 和 AAC/M4A 共 4 个成品执行 stream copy；ffprobe 的 codec、format 和 stream 数量全部符合计划，转码参数数量为 0。

自动化门禁共 660/660 通过、0 跳过；总体行覆盖率 84.34%、分支覆盖率 67.76%，A/B/C 风险组均超过现行阈值；全解决方案 Release 回归 1056/1056 通过、0 跳过。完整证据见 `TESTING.md` 的 P1-G7 基线。

## 9. 安全约束

- 报告、指纹、任务、历史和导出不得包含 Cookie、Authorization、签名 URL 或请求头。
- 显式编码不可用不得降级；未知编码不得伪装为 AVC。
- NativeAudio 不以“无需 ffmpeg”为由跳过完整性校验或原子发布。
- 覆盖只接受预检确认产生的授权；自动编号、迁移和附加资源基础名始终保留实际扩展名。
