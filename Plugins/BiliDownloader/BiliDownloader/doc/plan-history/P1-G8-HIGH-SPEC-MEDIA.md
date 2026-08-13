# P1-G8 高规格媒体识别与选择

## 1. 交付范围

P1-G8 为 HDR、杜比视界、Hi-Res 和杜比全景声建立从 Bilibili DASH 响应、批量选择界面、提交预检、任务快照、下载执行、发布前验证到历史导出的闭环。

本功能只做无转码选择与封装，不执行 HDR/SDR 转换，不升级普通媒体，不通过替代端点绕过登录或会员限制。

## 2. 可信证据规则

| 特征 | API 证据 | 发布前 ffprobe 证据 | 禁止的推断 |
| --- | --- | --- | --- |
| HDR | `dash.video[].id = 125` | `color_primaries=bt2020` 且 `color_transfer=smpte2084` | HEVC、4K、高码率或标题文案 |
| 杜比视界 | `dash.video[].id = 126` | `DOVI configuration record` 或正数 `dv_profile` | HEVC、HDR 或“杜比”描述 |
| Hi-Res | `dash.flac.audio` 存在且编码为 FLAC | 音频 `codec_name=flac` | 高码率、文件扩展名或 URL |
| 杜比全景声 | `dash.dolby.type = 2` 且实际音频为 E-AC-3 | E-AC-3 且 profile 明确包含 Atmos | `type=1` 普通杜比、E-AC-3 单独出现或高码率 |

能力状态使用 `Available`、`Unavailable`、`RequiresLogin`、`RequiresPremium` 和 `Unknown`。只有结构化 `need_login` / `need_vip` 字段可以产生权限状态；证据代码不得包含 Cookie、签名 URL、接口原文或账号信息。

## 3. 选择与容器矩阵

自动视频优先级为“杜比视界 → HDR → 所选普通画质”，自动音频优先级为“杜比全景声 → Hi-Res → 所选或最高码率普通 AAC”。每一层必须同时满足流证据、编码可识别和容器兼容，才会进入下一步编码选择。

显式高规格不可用、权限受限、编码不可识别或容器不兼容时返回结构化失败，绝不回退到标准流。普通视频质量只负责标准动态范围的回退；特殊 `qn=125/126` 不进入普通画质下拉。

| 输出模式 | MP4 | MKV | 原生音频 |
| --- | --- | --- | --- |
| 音视频 HDR / 杜比视界 | 支持 | 支持 | 不适用 |
| 音视频 Atmos | 支持 | 支持 | 不适用 |
| 音视频 Hi-Res/FLAC | 阻止 | 支持 | 不适用 |
| 仅音频 Atmos | 不适用 | 不适用 | `.m4a` |
| 仅音频 Hi-Res | 不适用 | 不适用 | `.flac` |

## 4. 批量能力交集

工作区根据当前勾选媒体显示交集：只有所有项目均为 `Available` 时，批量状态才是可用。任一项目明确不可用时为 `Unavailable`；否则依次显示会员限制、登录限制或未知，并显示“可用数/总数”。

- 勾选变化采用 250 ms 防抖；旧异步结果通过版本号与取消令牌隔离。
- 单次最多四路 DASH 探测。
- 会话缓存键包含媒体稳定身份和普通画质；缓存只保存脱敏能力快照，不保存 DASH URL。
- 解析新来源时清空缓存；登录态改变后的提交仍必须重新预检。
- 恢复的显式设置如果变为不可用，界面保留原值并标记无效，不自动改成 Auto。

UI 探测仅用于即时反馈。提交预检和执行阶段分别重新取证，因此旧缓存不能授予下载权限。

## 5. 任务事实与兼容

新提交使用 `submission_snapshot_version = 2` 和 `rf2:` 输出指纹。`rf2` 在 G7 的质量、编码、容器、模式基础上加入视频动态范围偏好和音频特征偏好，并按输出模式清除不消费的维度。

SQLite 新增字段：

- `video_dynamic_range_preference`
- `audio_feature_preference`
- `requested_media_features`
- `expected_media_features`
- `actual_media_features`

空字符串表示旧任务未知；字符串 `None` 表示新任务已知为标准规格。`Requested` 只记录用户显式选择，Auto 不伪装为显式要求；`Expected` 保存可信 DASH 选择结果；`Actual` 只在成品通过发布前验证后写入。

读取层继续接受 `rf1:`，但不会把缺少高规格语义的旧指纹与 `rf2:` 去重。历史重下对 v1 任务使用 Auto 并要求兼容确认，不根据旧文件名、扩展名或当前默认值回填历史事实。历史 JSON 导出升级为 schema v2，增加偏好和三组特征字段，仍不导出能力证据、权限细节或临时 URL。

## 6. 发布事务与失败恢复

当 `ExpectedMediaFeatures != None` 时，下载服务在最终输出目录生成 staging，随后用同一 ffmpeg 运行时目录中的 ffprobe 检查：

1. ffprobe 使用参数列表启动，不经过 Shell；超时为 15 秒，并传播外部取消。
2. 实际高规格位必须与预期完全一致；缺少或额外特征均视为冲突。
3. 验证成功后才使用同卷 `File.Move` 原子发布，并写入 `ActualMediaFeatures`。
4. 验证失败、JSON 损坏、进程失败或超时均分类为 `media_validation`。
5. 未验证的 staging 会删除，已经通过长度校验的 `video.tmp` / `audio.tmp` 会保留，供修复依赖后完整重试。
6. 仅合并重试使用任务中持久化的 `ExpectedMediaFeatures` 执行同样验证。

标准规格仍沿用 G7 的原子发布，不为普通下载增加强制 ffprobe 开销。

## 7. SOLID 与设计取舍

- `MediaStreamSelectionPolicy` 是无网络、无磁盘副作用的纯策略，负责特征层级与编码选择。
- `OutputArtifactPolicy` 是模式、容器、扩展名和高规格兼容矩阵的唯一规则源。
- `MediaCapabilityInspectionService` 只负责脱敏能力交集、并发和缓存，不承担提交授权。
- `IMediaOutputVerifier` 隔离外部进程和 ffprobe JSON；下载服务只编排“验证后发布”。
- 仓储只保存稳定枚举和位标志，不持久化 DASH DTO 或探测原文。

这里使用策略模式和依赖倒置解决真实变化点，没有为单一映射引入额外工厂层。

## 8. 测试与验收

自动化覆盖：四类特征存在/缺失/组合、普通杜比与 Atmos 区分、权限状态、Auto 优先级、显式不降级、MP4/MKV/原生音频矩阵、rf1/rf2、v1/v2、SQLite Unknown/None、历史 schema v2、ffprobe 成功/冲突/损坏 JSON/超时/取消，以及 G7 全量回归。

真实样本验收必须使用有合法访问权限且摘要固定的本地样本；报告只记录 SHA-256、容器、codec/profile、色彩字段和判定结果，不记录播放地址或账号信息。没有可再分发样本时不得用生成的普通媒体宣称完成真实 HDR/DV/Atmos 验收。
