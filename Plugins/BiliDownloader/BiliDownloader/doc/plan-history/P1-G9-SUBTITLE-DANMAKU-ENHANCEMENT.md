# P1-G9 字幕、软字幕与弹幕增强

> 实现日期：2026-08-09  
> 适用范围：BiliDownloader Document、下载执行器、任务中心、SQLite 与 ReleaseAcceptance  
> 状态：离线代码门禁已通过；固定版 ffmpeg 真实封装门禁与 B 站实网门禁待具备环境后人工执行

## 1. 设计边界

本功能组只处理 B 站已向当前账号公开的字幕和弹幕，不绕过权限，不烧录硬字幕，不把弹幕嵌入媒体轨，也不下载第三方字体。主媒体身份仍由 `RenditionFingerprint` 描述；字幕格式、语言和弹幕格式不进入该指纹，避免附加资源设置变化造成音视频重复下载。

主媒体与附加资源结果解耦：主媒体已经验证并发布后，即使单语言下载、文本格式化或软字幕封装失败，任务仍保持完成。失败事实写入版本化附加资源摘要，用户可以只重试失败附加资源。重试不会重新请求 DASH，也不会重新下载音视频。

## 2. SOLID 职责分解

| 边界 | 单一职责 | 设计意图 |
| --- | --- | --- |
| `ISubtitleCatalogService` | 获取、规范化并按语言去重字幕目录 | 目录选择规则不泄漏到 UI 或执行器；同语言官方/人工优先于 AI |
| `ISubtitleContentProvider` | 把平台 JSON 转成规范 `SubtitleCue` | 网络协议和业务时间轴分离 |
| `ISubtitleFormatter` | 将 cue 格式化为一种文本格式 | SRT、ASS、VTT 通过策略扩展，不修改处理器 |
| `IDanmakuFormatter` | 将规范弹幕格式化为一种外置格式 | XML、ASS、JSON 各自封装转义与稳定输出规则 |
| `ISubtitleMediaMuxer` | 生成含字幕轨的媒体候选文件 | 只生成候选，不决定是否替换可信主文件 |
| `ISubtitleTrackVerifier` | 用 ffprobe 证明 codec、语言、标题和精确轨数 | 未验证候选禁止发布 |
| `IExtrasRetryExecutor` | 只执行失败或缺失附加项 | 与媒体下载职责隔离，防止“重试字幕”变成“重下视频” |

`SubtitleExtrasHandler` 和 `DanmakuExtrasHandler` 仅负责编排上述窄接口。平台 JSON、ffmpeg 参数、SQLite 和 UI 状态均不进入处理器核心算法。

## 3. 契约与不变量

### 3.1 字幕

- `SubtitleTrackDescriptor` 保存稳定语言键、显示名、来源类型、平台轨道 ID；下载 URL 仅存在于运行时对象。
- `SubtitleCueNormalizer` 删除空文本、非有限时间和无效区间；负开始时间钳制为零；按开始、结束和原始序号稳定排序。
- `SubtitleOptions` 支持 `None`、`All`、`SelectedLanguages`，格式为 SRT/ASS/VTT，交付为 External/SoftMuxed/ExternalAndSoftMuxed。
- 语言键去空、按不区分大小写去重并稳定排序；原始稳定键进入字幕元数据和结果摘要，安全化键只用于文件名。
- 同一语言最多保留一轨，优先级为官方/人工、AI、未知；优先级相同则保持平台目录原始顺序。

### 3.2 弹幕

- `DanmakuOptions` 可同时选择 XML、ASS、JSON；当前只接受内置 `default` ASS 样式。
- ASS 固定 1920×1080、Arial 36px、2px 描边；滚动 8 秒，顶部/底部 5 秒。
- 轨道分配只使用出现时间、模式和稳定序号，不依赖随机数；相同输入必得相同输出。
- 分段按 360 秒获取，按弹幕 ID 去重，再按出现时间、ID、原始序号稳定排序。
- XML 进行标准 XML 转义；JSON 使用 UTF-8 camelCase DTO；ASS 转义反斜杠、花括号和换行。

## 4. 软字幕兼容矩阵

| 输出 | 外置 SRT/ASS/VTT | SRT 软字幕 | ASS 软字幕 | VTT 软字幕 |
| --- | --- | --- | --- | --- |
| MP4 音视频/仅视频 | 支持 | `mov_text` | `mov_text` | `mov_text` |
| MKV 音视频/仅视频 | 支持 | SubRip | ASS | 预检阻止 |
| 原生音频 | 支持 | 预检阻止 | 预检阻止 | 预检阻止 |

预检同时验证值对象枚举、所选语言、容器和输出模式。非法软封装组合不会静默降级；用户可以切换为外置字幕后继续提交。

## 5. 文件发布与冲突规则

- 字幕：`{主文件名}.{安全语言键}.srt|ass|vtt`
- 弹幕：`{主文件名}.xml|ass|json`
- 封面：沿用 `{主文件名}_cover.jpg`

提交预检按最终字幕格式、交付方式、所选语言和弹幕格式计算附加文件冲突。`All` 语言模式在主动提交时重新枚举 `{主文件名}.*.{格式}`，避免缓存变化导致覆盖。P0 已存在的 XML 和封面保守冲突域继续保留，不降低旧版本覆盖保护。

软字幕执行顺序如下：

1. 读取已经验证且可播放的主文件。
2. 获取并规范化各语言字幕；单语言失败不影响其他语言。
3. 生成临时字幕和所需外置文件。
4. muxer 只生成同目录候选文件，不修改主文件。
5. ffprobe 校验字幕 codec、语言、标题和精确轨数。
6. 校验成功后原子替换主文件；失败则删除候选并保留原主文件。

独立重试软字幕时会从当前主文件只映射视频/音频轨，并重新建立完整字幕集合，因此不会累积重复字幕轨。

## 6. 结构化结果与失败语义

`ExtrasExecutionSummary` 当前版本为 1，条目键固定为：

- `subtitle:{language}:{format}:{delivery}`
- `danmaku:{format}`
- `cover`

状态为 Success、PartialSuccess、Failed、Unavailable、LegacyUnknown。摘要只保存状态、错误分类、输出文件名和失败弹幕分段，不保存字幕/弹幕正文、Cookie、Header 或下载 URL。旧字符串摘要读取为 `LegacyUnknown`，不会伪造成功事实。

`Unavailable` 表示媒体确实没有所选语言，不是可重试错误。网络、限流、写入、格式化、ffmpeg 和验证失败均为可重试错误。弹幕任一分段失败时对应格式为 `PartialSuccess`，失败分段随摘要保存；重试只补这些分段并重新生成受影响格式。

Coordinator 的“仅重试失败附加资源”命令具有以下前置条件：

- 任务状态必须为已完成；
- 主文件必须仍然存在；
- 同一任务通过 `SemaphoreSlim` 互斥，第二个并发请求不会重复执行；
- 只更新附加文件、可能的软字幕成品和结构化摘要，不修改主任务完成状态。

## 7. 持久化与兼容

- 新提交快照版本为 3，任务与历史投影保存结构化 `SubtitleOptions`、`DanmakuOptions` 和附加资源摘要。
- SQLite 可重入迁移增加 `subtitle_options_json`、`danmaku_options_json`，并沿用 `extras_result_summary` 保存版本化 JSON。
- 旧 `DownloadSubtitle=true` 映射为全部语言、外置 SRT；旧 `DownloadDanmaku=true` 映射为外置 XML。
- V1/V2/V3 Document 和旧预设均可加载；恢复只重建离线配置，不自动检测字幕、不联网。用户下一次保存时统一写规范化 V3。
- 历史安全导出 schema 为 3，只增加语言键、格式、交付方式、状态和错误分类。

## 8. UI 行为

下载配置页提供字幕启用、范围、语言多选、格式、交付方式以及“检测字幕”命令。检测只扫描当前勾选项，最多四路并发，展示每种语言的官方/AI 来源及“可用项数/所选项数”。取消或部分接口失败不会清空上一次成功结果。

弹幕区域支持 XML/ASS/JSON 多选，ASS 固定使用内置 default 样式。活动任务和历史中心展示最终配置、逐项结果和失败原因；存在可重试失败时显示独立重试命令。

## 9. 自动化验收证据

2026-08-09 本地 Release 验证：

- `BiliDownloader.Tests`：701/701 通过，0 跳过。
- 全解决方案：1097/1097 通过，0 跳过；Release 构建未产生错误或警告。
- Release 生产输出敏感数据门禁通过：扫描数据库、日志、文本和二进制，5 个文件、0 个问题。
- BiliDownloader、ReleaseAcceptance 与测试项目编译：0 错误、0 警告。
- 覆盖配置规范化、迁移、平台 fixture、官方优先、缺失语言、三种字幕黄金输出、三种弹幕稳定输出、转义、分段部分失败/只补失败段、软封装成功/失败保护、附加重试互斥与主状态不变、SQLite 重复迁移、手动字幕检测缓存、ffprobe JSON 精确轨数和动态文件冲突。
- `OfflineMediaOutputGate` 已扩展：固定 ffmpeg 8.1.2 环境下合成音视频，通过生产 muxer 生成 MP4 `mov_text` 与 MKV SubRip/ASS，并由生产 verifier 验证语言、标题和精确轨数。

当前机器未发现可执行的 ffmpeg/ffprobe 8.1.2，因此本次没有伪造“真实进程门禁已通过”的结论。具备固定运行时后执行：

```powershell
dotnet run --project Plugins/BiliDownloader/BiliDownloader.ReleaseAcceptance/BiliDownloader.ReleaseAcceptance.csproj -c Release -- media-output --ffmpeg <ffmpeg.exe> --ffprobe <ffprobe.exe> --sandbox <空沙箱目录> --report <报告.json>
```

B 站实网验收仍是显式账号门禁，CI 默认不访问 B 站。人工验收应使用公开样例和显式提供的登录态，验证真实字幕目录、360 秒弹幕分段及限流/部分失败行为；任何证据文件在归档前继续经过敏感数据扫描。

## 10. 后续约束

- 新增字幕格式只实现新的 `ISubtitleFormatter` 并注册，不扩张处理器条件分支。
- 新增弹幕样式必须版本化样式契约，不能改变 `default` 的确定性输出。
- 若未来调整为初次主文件发布前软封装，仍必须保留“可信无字幕输入”和原子回退不变量。
- P1-G10 总验收必须实际执行固定版 ffmpeg 门禁、解决方案全测试和显式实网门禁；未执行项应继续标记为待人工验收。
