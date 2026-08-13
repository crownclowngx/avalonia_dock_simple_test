# P1-G4：Document V3 与可复用方案

> 完成日期：2026-08-07
> 保存主版本：3.0
> 适用范围：BiliDownloader Document，不包含 SQLite 任务事实、远端页面缓存和下载执行状态

## 1. 目标与设计边界

Document V3 把一次性下载工作台升级为可再次打开的下载意图。它保存“用户想从哪里读取、如何筛选、如何输出”，但不成为任务事实源，也不代表打开文件时应立即访问远端。

职责划分如下：

- Document V3：来源、筛选、预设、命名、输出规则和轻量增量基线。
- SQLite：任务状态、进度、错误、恢复检查点和最终输出事实。
- Provider：规范化输入、分页和解析远端内容；只有用户明确操作才调用。
- Coordinator：提交、去重、写入任务、启动、暂停、重试和删除的唯一命令入口。

以下内容明确禁止写入 Document：

- Cookie、Authorization、请求头、AccessKey、CSRF、WBI 签名或其他凭据；
- DASH、CDN、字幕和封面的临时或签名 URL；
- 完整远端页面、ContinuationToken 链、页面缓存；
- 跨页逐项勾选、虚拟化控件状态；
- 下载线程、完整任务记录、断点文件和执行器内部状态。

## 2. V3 保存结构

宿主外层继续使用 `DocumentSaveData`，`PluginMetadata.Version` 固定写为 `3.0`，业务内容位于 `Content`。

| 字段 | 含义 | 默认值/约束 |
| --- | --- | --- |
| DocumentId | Document 的稳定实例 ID | 新建时生成，跨重启保持 |
| Url | 旧快速链接输入的兼容字段 | 保存前去除查询签名和片段 |
| DownloadInfo | 兼容日志摘要 | 保存前统一脱敏 |
| OutputDirectory | 输出目录 | 空值表示沿用全局默认 |
| UseGroupFolder | 使用系列子目录 | false |
| AddIndexToTitle | V1 兼容序号开关 | true |
| PresetId | 当前预设身份 | builtin_compat |
| NamingTemplate | 命名模板 | `{index}.{title}` |
| QualityId / AudioQualityId | 已选画质 ID | 0，等待解析后匹配 |
| DownloadCover | 下载封面 | false |
| ConflictPolicy | 文件冲突策略 | AutoNumber |
| Source | 白名单来源描述 | 可空，不序列化 Provider 对象 |
| Filters | 来源筛选意图 | 空关键词、空日期、全部类型、来源默认排序 |
| Baseline | 轻量增量基线 | 版本 1、无 token、无边界键 |
| VideoCodecPreference | 编码偏好 | AutoCompatibility |
| OutputContainer | 输出容器 | Mp4 |
| OutputMediaMode | 输出流模式 | AudioVideo |
| VideoDynamicRangePreference | 动态范围偏好 | Auto |
| AudioFeaturePreference | 音频能力偏好 | Auto |
| SubtitleOptions | 字幕选择、语言、格式和交付 | None、外置 SRT |
| DanmakuOptions | 弹幕格式和 ASS 样式 ID | 无格式、default |
| PerTaskRateLimitBytesPerSecond | 单任务总限速 | 0 表示不限速，禁止负数 |

### 2.1 来源白名单

`SourceDescriptorSaveData` 只包含：

- `Kind`：字符串来源类型。使用字符串是为了在 Provider 缺失时仍能查看和另存。
- `StableSourceId`：平台稳定身份，禁止 URL、查询串、片段和敏感字段特征。
- `DisplayName`：脱敏后的用户可读名称。
- `CapabilityVersion`：描述符生成时的 Provider 能力版本。
- `AutoOpen`：当前唯一允许的公开参数，用于直接课程来源。

Provider 若新增必须持久化的公开参数，必须先扩展白名单 DTO、迁移说明和安全测试。未知字典项不会被悄悄写入。

### 2.2 筛选与基线

筛选保存关键词、起止时间、媒体类型和排序，不保存服务端/客户端拆分后的查询计划。

基线保存最后一次完整检查时间、来源快照 token、基线版本和最多 100 个边界项目键。100 与 Provider 单页上限一致，可以表达边界而不会把远端列表复制进 Document；快照 token 最长 2048 字符并拒绝临时 URL 或凭据特征。

P1-G4 只建立该契约。生成新基线、差异分类和跨来源去重属于 P1-G5。

## 3. 版本迁移

| 输入 | 加载 | 下一次保存 |
| --- | --- | --- |
| V1 | 恢复六个历史字段；规范 BV/AV/EP/SS/MD 离线转换为 DirectLink | V3 |
| V2 | 完整恢复 P0 配置；补齐来源、结构化附加资源和 P1 默认值 | V3 |
| V3 | 完整恢复意图和基线，忽略未知附加字段 | V3 |
| 未知主版本 | 只恢复 DocumentId、URL、输出目录等安全公共字段 | 强制选择新路径另存 V3 |
| 损坏 JSON/版本元数据 | 拒绝创建 Document 标签并显示稳定错误 | 不允许保存，原文件不变 |

V1/V2 的字幕布尔值 `true` 映射为“全部可用语言、外置 SRT”；弹幕布尔值 `true` 映射为外置 XML。视频编码、容器、输出模式、高规格偏好和单任务限速使用兼容默认值。

短链展开需要网络，因此旧 b23 短链不会在加载时规范化。系统保留经过清理的旧输入，等待用户明确点击解析。

## 4. 离线恢复流程

1. 宿主读取外层 `DocumentSaveData`。
2. V3 映射器识别主版本、完成单向迁移并执行安全校验。
3. ViewModel 在恢复保护区内应用本地字段，不触发 `IsModified`。
4. DirectLink 回到快速链接入口；其他已支持来源挂载一个空浏览层级。
5. 缺失 Provider 时展示来源类型、名称和稳定 ID，继续保留原 DTO。
6. View 首次挂载时只按已恢复的 `DocumentId` 查询 SQLite 任务投影。
7. 只有用户点击解析、刷新或更改筛选后，才进入 Provider 查询路径；仍不会自动提交任务。

离线挂载不会恢复页面、游标、列表项和勾选。筛选变化会使 Document 置脏，并按现有 G3 行为执行一次明确的用户查询。

## 5. 可复用预设

`DownloadProfile` 和 `DownloadPreset` 同步携带 V3 的输出字段。旧自定义预设缺少这些字段时使用兼容默认值；新建、复制和重命名均往返完整结构。

来源、筛选和增量基线属于单个 Document，不进入全局预设。这样一个预设可以复用于不同来源，同时不会错误共享账号内容身份或增量边界。

P1-G4 不改变下载执行器：AVC/HEVC/AV1、MP4/MKV、仅音频/仅视频、高规格、字幕弹幕转换和限速的实际执行分别由 P1-G7～G10 接入。当前内置预设全部保持 P0 兼容输出，避免仅因升级保存格式改变下载结果。

## 6. 修改状态与保存保护

以下变化会设置 `IsModified`：URL、来源、筛选、基线、预设、命名、目录、画质、附加资源、冲突策略以及全部 P1 输出字段。分页、缓存、选择、忙碌状态和纯展示信息不会置脏。

创建 JSON 不等于磁盘保存成功。只有宿主完成写入并调用 `NotifySaveCompleted` 后，Document 才清除 `IsModified`。

未知未来版本实现 `IDocumentSavePathPolicy`：宿主始终打开保存选择器，拒绝选择原路径，写入成功后才解除保护。该接口位于 Common，宿主不依赖 BiliDownloader 具体类型。

## 7. 自动化证据

`DocumentV3G4Tests` 覆盖：

- V1→V3、V2→V3、V3 往返和未知字段；
- 九种当前来源、筛选、基线和完整输出配置；
- 缺失 Provider、损坏文件、未知主版本和强制另存；
- 打开/初始化零 Provider 调用、按 DocumentId 恢复 SQLite 投影；
- 敏感来源、签名 URL、基线容量和固定 JSON 字段快照；
- 预设兼容默认值和持久状态置脏。

宿主测试覆盖批量打开隔离、错误条、强制另存、拒绝覆盖原路径和成功写盘通知；Headless UI 测试覆盖错误条显示与关闭。
