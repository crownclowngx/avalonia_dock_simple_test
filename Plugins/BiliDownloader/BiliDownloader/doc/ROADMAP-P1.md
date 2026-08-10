> 文档定位：P1 功能组的实施顺序、接口边界、迁移策略与验收计划
> 总体路线图：[ROADMAP.md](ROADMAP.md)
> 产品基线：[PRODUCT.md](../PRODUCT.md)
> 编写基准日期：2026-08-04
> 计划周期：W13～W30，2026-08-05～2026-12-08
> 前置门禁：P0-G8 验收与发布完成后才能启动 P1
> 当前状态：P1-G0～P1-G8 已实现；P1-G9、P1-G10 代码与离线门禁已完成，固定版 ffmpeg/实网/桌面验收待执行；P1 候选不可正式发布

## 1. 文档目的

本文件将总体路线图中的“P1：个人效率”展开为可以逐组评审、实现和验收的执行方案。P1 的核心不是继续增加单个链接的解析分支，而是把 BiliDownloader 从“下载一次链接”升级为“保存并重复执行内容来源与下载规则”。

本文回答以下问题：

- P1-G0～P1-G10 应按什么顺序实施，各组依赖什么能力。
- 多种 B 站内容来源如何复用统一的分页、筛选、选择和解析边界。
- 内容身份、任务身份和输出版本身份如何区分，避免跨来源重复下载。
- Document V3、SQLite、下载执行器和 UI 分别保存什么事实。
- 增量检查如何保持“只预览、不自动提交”的安全约束。
- 编码、容器、高规格媒体、字幕弹幕和限速如何进入现有下载链路。
- 每组需要哪些自动化测试、真实链路验收和产品文档更新。

本文是实施计划，不表示其中能力已经完成。只有某个功能组通过退出条件并取得验证证据后，才能更新状态和产品能力声明。

## 2. P1 目标与非目标

### 2.1 P1 目标

- 支持 UP 主投稿、收藏夹、稍后再看、历史记录、追番追剧、订阅合集和课程等可重复内容来源。
- 对大型来源进行稳定分页、筛选和跨页选择，不一次性创建全部 UI 项。
- 将内容来源、筛选规则、预设、命名、目录和输出配置完整保存为 Document。
- 用户主动检查更新后，将结果分类为新增、已下载、下载中、失效和规则排除。
- 使用稳定媒体身份进行跨 Document、跨来源去重，同时允许用户主动生成不同输出版本。
- 提供历史搜索、重新下载、文件定位、存在性检查和脱敏导出。
- 支持 AVC、HEVC、AV1，MP4、MKV，以及音视频、仅视频和仅音频输出。
- 识别和选择 HDR、杜比视界、Hi-Res 与杜比全景声能力。
- 支持字幕语言、多格式与软字幕，支持弹幕 XML、ASS、JSON。
- 支持全局和单任务限速，并保留现有并发任务与分块连接控制。

### 2.2 P1 非目标

- 不接入 B 站以外的平台，不绕过账号权限、付费权限或 DRM。
- 不在启动、打开 Document 或后台定时器中自动检查更新或创建下载任务。
- 不实现无人值守订阅、系统计划任务或自动下载规则。
- 不实现转码；编码选择只选择服务端已有流，合并默认使用 stream copy。
- 不实现 NFO、poster、fanart、章节写入等 P2 媒体库元数据。
- 不实现 P2 的全库完整性扫描、孤立临时文件清理、诊断包或插件自动更新。
- 不将 Cookie、请求头、签名 URL 或 DASH 临时地址写入 Document、SQLite、日志和导出文件。
- 不将完整来源列表或跨页临时勾选状态永久写入 Document。
- 不改变“多个 Document、一个全局 Tool、一个 Coordinator、SQLite 为任务事实源”的架构。

## 3. 当前代码基线

P1 规划以 2026-08-04 的 P0-G0～P0-G7 实现为基础；P0-G8 仍是正式启动 P1 前的发布门禁。

当前已经具备：

- BV、AV、b23.tv、EP、SS 的解析以及单视频、多 P、番剧分集选择。
- BiliApiService 中的视频、番剧、DASH、字幕和弹幕 API 入口。
- BiliDownloaderViewModel 与分拆后的解析、列表、配置、预设和命名 ViewModel。
- DownloadSubmission、SubmissionPreflightService、BiliDownloadCoordinator 构成的唯一提交链路。
- DownloadTaskStore 的 SQLite 任务事实、恢复检查点、输出路径保留和错误分类。
- DocumentSaveDataV2 与 DocumentSaveCodec 的 V1/V2 兼容加载。
- BiliDownloadService、MultiConnectionDownloader、ffmpeg 合并与附加资源处理器。
- Tool 侧虚拟化任务列表、搜索筛选、多选、批量操作和失败行动入口。
- Release 配置下 BiliDownloader 384/384 测试、解决方案 769/769 测试通过的 G7 基线。

P1 开始时仍需正视以下限制：

1. BiliApiService 以具体 API 方法为主，没有统一的内容源提供者契约。
2. BiliVideoItem 适合一次解析结果，不能直接表达游标、来源能力和跨页选择。
3. Document V2 只保存单链接和 P0 下载配置，没有内容源、筛选和增量基线。
4. SQLite 已保存 Aid、Cid、EpId 等字段，但没有显式稳定媒体键和输出版本指纹。
5. 视频流选择仍偏向 AVC，输出路径和合并流程默认 MP4 音视频。
6. 字幕固定输出 SRT，弹幕固定输出 XML，尚无语言、格式和软字幕模型。
7. MultiConnectionDownloader 没有统一带宽调节器。

## 4. 架构与实施原则

### 4.1 Document 表达意图

Document V3 保存内容来源、筛选规则、下载预设、命名、目录、冲突策略、输出选项和轻量增量基线。Document 不保存下载线程、完整任务状态、签名 URL 或全部远端页面缓存。

### 4.2 Tool 管理执行与历史

任务中心继续读取 SQLite，展示所有 Document 的任务、历史、错误和文件状态。Document 可以显示属于自身的任务投影，但不能成为任务事实源。

### 4.3 Coordinator 是唯一任务命令入口

内容源解析、更新比较和选择发生在 Document 侧；真正提交、去重复检、任务写入、启动、暂停、重试和删除仍由单例 Coordinator 串行协调。

### 4.4 检查更新不等于下载

“检查更新”只请求来源页面并生成差异预览。新增项可以默认勾选，但必须由用户点击提交、通过预检并确认后才创建任务。应用启动、Document 打开和来源恢复均不得联网。

### 4.5 身份与输出版本分离

同一媒体内容和同一输出任务不是一个概念：

- ContentItemKey 用于来源列表与跨页选择，表达“来源中的一项”。
- MediaUnitKey 在解析完成后以 Aid + Cid 归一化，表达“一个可下载媒体单元”。
- RenditionFingerprint 在 MediaUnitKey 基础上加入画质、音频质量、编码、容器和输出模式，表达“一个具体输出版本”。

跨来源聚合使用 MediaUnitKey；任务重复判断使用 RenditionFingerprint。这样同一视频不会因为同时存在于收藏夹和 UP 主投稿中而误下载两次，但用户仍可明确生成 AVC MP4 与 AV1 MKV 等不同版本。

### 4.6 安全和兼容默认开启

- 所有来源标识只保存稳定 ID 和必要公开参数，不保存临时鉴权地址。
- V1/V2 Document、旧预设和旧任务必须可读取；缺失字段使用兼容、安全的默认值。
- 用户明确选择某项编码或高规格能力后，不允许后台静默降级。
- 附加资源失败继续与主媒体结果解耦。
- 所有导出继续经过 SensitiveDataSanitizer，并使用字段白名单。

## 5. 总体时间线与依赖

| 功能组 | 周期 | 时间 | 实施主题 |
| --- | --- | --- | --- |
| P1-G0 | W13～W14 | 2026-08-05～2026-08-18 | 内容源契约、分页模型、能力声明、稳定标识与测试桩 |
| P1-G1 | W15～W16 | 2026-08-19～2026-09-01 | UP 主投稿、收藏夹、稍后再看、历史记录 |
| P1-G2 | W17～W18 | 2026-09-02～2026-09-15 | 追番追剧、订阅合集、课程 |
| P1-G3 | W19～W20 | 2026-09-16～2026-09-29 | 筛选、跨页选择、大列表产品化 |
| P1-G4 | W21 | 2026-09-30～2026-10-06 | Document V3 与完整方案复用 |
| P1-G5 | W22～W23 | 2026-10-07～2026-10-20 | 增量检查、分类与跨来源去重 |
| P1-G6 | W24 | 2026-10-21～2026-10-27 | 历史中心、文件检查与脱敏导出 |
| P1-G7 | W25～W26 | 2026-10-28～2026-11-10 | 编码、容器和输出模式 |
| P1-G8 | W27 | 2026-11-11～2026-11-17 | 高规格媒体能力 |
| P1-G9 | W28～W29 | 2026-11-18～2026-12-01 | 字幕、软字幕与弹幕增强 |
| P1-G10 | W30 | 2026-12-02～2026-12-08 | 全局/单任务限速与 P1 总验收 |

依赖主线：

~~~text
P0-G8
  -> P1-G0
       -> P1-G1 --\
       -> P1-G2 ---+-> P1-G3 -> P1-G4 -> P1-G5 -> P1-G6 --\
                                  \-> P1-G7 -> P1-G8 -> P1-G9 ---+-> P1-G10
~~~

并行边界：

- P1-G1 与 P1-G2 可以在 P1-G0 契约冻结后并行开发，但必须共用同一 Provider 测试套件。
- P1-G6 与 P1-G7 可以在 P1-G5 的任务身份迁移冻结后并行。
- P1-G10 只有在 P1-G5、P1-G6、P1-G9 全部通过退出条件后才能开始最终回归。

## 6. P1 公共契约与数据决策

### 6.1 内容源契约

新增 IContentSourceProvider，职责限定为：

- 声明唯一 ContentSourceKind 和 ContentSourceCapabilities。
- 将用户输入规范化为 ContentSourceDescriptor。
- 以 ContentPageRequest 请求一页内容并返回 ContentPage。
- 将 ContentSourceItem 展开或解析为现有可提交媒体项。

统一模型：

| 类型 | 关键内容 |
| --- | --- |
| ContentSourceKind | DirectLink、Uploader、Favorite、WatchLater、History、FollowingBangumi、FollowingCinema、Collection、Course |
| ContentSourceCapabilities | RequiresLogin、SupportsPaging、SupportsKeyword、SupportsDateRange、SupportsTypeFilter、SupportsIncremental |
| ContentSourceDescriptor | 来源类型、稳定来源 ID、显示名称、必要公开参数、能力版本 |
| ContentPageRequest | PageSize、ContinuationToken、SourceFilterRules |
| ContentPage | Items、NextContinuationToken、HasMore、SnapshotToken |
| ContentSourceItem | ContentItemKey、标题、类型、作者、发布时间、封面摘要、Aid/Bvid/EpId 等稳定引用 |
| ContentItemKey | 来源类型 + 平台原生稳定项目 ID |
| MediaUnitKey | 解析后的 Aid + Cid |

ContinuationToken 是 Provider 私有游标的不可解释字符串。Document 和 UI 只负责原样回传，不根据其中内容计算页码。页码型 API 由 Provider 自行编码和解码 token。

### 6.2 选择模型

ContentSelectionState 使用 ContentItemKey 保存当前会话勾选集合，并支持：

- 选择/取消当前项。
- 选择/取消当前已加载页。
- 选择全部匹配结果时保存“全选规则 + 排除键”，而不是枚举所有远端项。
- 筛选规则变化后使旧的“全部结果”选择失效并要求重新确认。
- 页面卸载或虚拟化回收后仍保持选择。

跨页选择只在当前 Document 会话中存在；保存 Document 时不持久化具体勾选键。

### 6.3 Document V3

DocumentSaveDataV3 在 V2 基础上增加：

- ContentSourceDescriptor Source。
- SourceFilterRules Filters。
- IncrementalBaseline Baseline。
- VideoCodecPreference、OutputContainer、OutputMediaMode。
- VideoDynamicRangePreference、AudioFeaturePreference。
- SubtitleOptions、DanmakuOptions。
- PerTaskRateLimitBytesPerSecond。

V1/V2 迁移默认值：

- 原 Url 转换为 DirectLink 来源。
- 视频编码为 AutoCompatibility，保持优先 AVC 的历史行为。
- 容器为 Mp4，输出模式为 AudioVideo。
- DownloadSubtitle=true 映射为全部可用语言的外置 SRT。
- DownloadDanmaku=true 映射为外置 XML。
- 高规格选项为 Auto，单任务限速为 0。
- 保存后统一写 V3；加载旧版本本身不立即改写用户文件。

### 6.4 SQLite 任务扩展

download_tasks 采用可重入 ALTER TABLE 迁移，增加：

- media_unit_key。
- rendition_fingerprint。
- selected_video_codec 与 actual_video_codec。
- output_container 与 output_media_mode。
- requested_media_features 与 actual_media_features。
- subtitle_options_json 与 danmaku_options_json。
- task_rate_limit_bytes_per_second。

旧任务读取时根据 Aid、Cid 和现有质量字段尽力生成兼容身份；缺少实际编码和容器时显示“未知”，不能伪装为 AVC/MP4。迁移不得修改既有任务状态、路径保留和恢复检查点。

### 6.5 增量分类

ContentComparisonStatus 固定为：

- New：满足当前规则，且没有同输出版本的活动或完成任务。
- Downloaded：存在完成任务且最终文件存在。
- InProgress：存在排队、等待登录、下载、暂停、中断或可恢复失败任务。
- Invalid：此前可识别项目在一次完整来源扫描中确认已删除、失效或无权访问。
- RuleExcluded：远端仍存在，但不满足当前筛选或媒体规则。

仅加载部分页面时不得把未出现的旧项目标记为 Invalid。文件存在性未知时先依据任务事实展示，并由用户触发文件检查后细化。

## 7. P1-G0：统一内容源基础

**时间：W13～W14，2026-08-05～2026-08-18**

### 7.1 P1-G0 目标与非目标

目标是建立所有 P1 来源必须遵守的最小契约、Provider 注册机制、分页模型、稳定标识和可离线测试框架。DirectLinkProvider 先适配现有 BV/AV/EP/SS 解析，证明新契约不会回归 P0。

本组不接入新的用户内容来源，不实现 Document V3，不改变任务数据库和下载执行器。

### 7.2 P1-G0 前置依赖与现有入口

- 前置依赖：P0-G8 发布验收完成。
- 现有入口：BiliApiService、VideoParseViewModel、VideoListViewModel、BiliVideoCollection、BiliVideoItem。
- 必须保持现有链接输入到提交不超过三个主要操作。

### 7.3 P1-G0 接口和数据变化

- 新增 IContentSourceProvider 与 IContentSourceProviderRegistry。
- 新增第 6 节定义的来源、分页、能力和稳定键模型。
- DirectLinkProvider 包装现有 ParseVideoId、ParseBangumiId 和对应解析调用。
- Provider 不直接引用 ViewModel、Coordinator 或 SQLite。
- BiliApiService 继续负责 HTTP 与 JSON 映射；Provider 负责来源语义和分页编排。

### 7.4 P1-G0 实施步骤

1. 为现有 BV/AV/EP/SS 结果建立 ContentSourceItem 到 BiliVideoItem 的适配层。
2. 实现 ContentItemKey、MediaUnitKey 的规范化、相等比较和序列化测试。
3. 实现 ContentPageRequest 与 token 往返，限制 PageSize 合法范围。
4. 建立 Provider 注册表并拒绝重复 ContentSourceKind。
5. 将 VideoParseViewModel 的直接 API 分支收敛到 DirectLinkProvider。
6. 建立 Provider 契约测试基类，覆盖空页、末页、取消、重复 token 和异常映射。
7. 保持现有 P0 UI 和保存格式不变，完成回归后冻结接口。

### 7.5 P1-G0 异常、兼容与安全

- Provider 返回的签名 URL、Cookie 和请求头不得进入 ContentSourceItem。
- ContinuationToken 只允许短期内存使用；日志只能记录是否存在，不记录原文。
- 相同 token 连续返回且页面无新增键时停止分页并报告来源协议异常，避免死循环。
- 取消解析不得清空用户上一次成功结果。

### 7.6 P1-G0 测试矩阵

- DirectLinkProvider 对 BV、AV、b23.tv、EP、SS 的回归。
- ContentItemKey 与 MediaUnitKey 的相等、大小写和序列化。
- 空页、单页、多页、重复 token、取消和 API 错误。
- Provider 注册重复、未知类型和能力声明一致性。
- 测试桩不访问真实 B 站网络。

### 7.7 P1-G0 退出条件

- 现有所有链接类型通过统一 Provider 路径解析。
- Provider 契约不依赖 UI、Coordinator 和数据库。
- 分页协议能检测重复 token，取消安全且不泄露敏感参数。
- P0 全部自动化测试继续通过。

### 7.8 P1-G0 文档同步

- 新增 P1-G0 详细实现记录。
- 在架构文档补充 Provider 边界。
- 只有验收完成后才在 ROADMAP.md 标记 P1-G0 状态。

## 8. P1-G1：个人高频内容来源

**时间：W15～W16，2026-08-19～2026-09-01**

**状态：已实现并通过离线 fixture、插件兼容与全解决方案回归（2026-08-07）。**

实现记录见 [P1-G1-PERSONAL-CONTENT-SOURCES.md](P1-G1-PERSONAL-CONTENT-SOURCES.md)。

### 8.1 P1-G1 目标与非目标

接入 UP 主投稿、收藏夹、稍后再看和历史记录，形成第一批个人高频来源。所有来源必须使用统一分页接口并明确登录要求。

本组不实现追番、课程、跨页“全部结果”选择、增量比较和自动提交。

### 8.2 P1-G1 前置依赖与现有入口

- 前置依赖：P1-G0。
- 现有入口：BiliApiService、BiliCredentialProvider、BiliLoginStateService、VideoParseViewModel。
- 收藏夹需要区分公开收藏夹与当前账号私有收藏夹；稍后再看和历史记录始终要求登录。

### 8.3 P1-G1 接口和数据变化

- 实现 UploaderSourceProvider、FavoriteSourceProvider、WatchLaterSourceProvider、HistorySourceProvider。
- SourceDescriptor 只保存 mid、收藏夹 media_id 等稳定 ID，不保存页面 URL 中的临时参数。
- ContentSourceCapabilities 精确声明登录、分页、日期和类型筛选支持。
- BiliApiService 为每类来源提供窄 API DTO，Provider 将其映射为统一项。

### 8.4 P1-G1 实施步骤

1. 实现 UP 主输入规范化，支持空间链接、mid 和可识别的投稿页链接。
2. 实现收藏夹列表发现与具体收藏夹内容分页。
3. 实现稍后再看分页；API 不提供稳定分页时在 Provider 内建立有界快照分页。
4. 实现历史记录游标分页，保留视频、番剧等来源类型。
5. 在来源选择区展示登录要求、来源名称和支持的筛选能力。
6. 登录失效时保留已加载页面和选择，只阻止继续请求。
7. 使用固定 JSON fixture 验证 DTO 映射，不让单元测试访问线上 API。

### 8.5 P1-G1 异常、兼容与安全

- 私有来源未登录时返回结构化 AuthRequired，不伪装为空列表。
- 来源被删除、收藏夹无权访问和 UP 主不存在分别映射为 InvalidSource、Forbidden、NotFound。
- API 限流保留 Retry-After 摘要，但不在后台无限重试。
- 历史记录的播放时间等隐私字段不进入任务库和导出模型。

### 8.6 P1-G1 测试矩阵

- 四类来源的首/中/末页、空来源和取消。
- 公开/私有收藏夹，登录有效/失效。
- 历史游标重复、来源项重复和字段缺失。
- 来源名称、作者、时间、类型和稳定键映射。
- API 403、404、限流、超时和格式变化。

### 8.7 P1-G1 退出条件

- 四类来源均能稳定分页并返回无敏感数据的统一项。
- 登录失效不会丢失已加载结果，也不会被解释为来源为空。
- 同页或跨页重复项按 ContentItemKey 折叠并保留首次稳定顺序。
- 所有来源测试使用固定响应并可离线运行。

### 8.8 P1-G1 文档同步

- 更新 API 适配说明和登录能力矩阵。
- 验收后更新 PRODUCT.md 中对应来源的实现状态。

## 9. P1-G2：订阅、番剧与课程来源

**时间：W17～W18，2026-09-02～2026-09-15**

**实施状态：代码与离线自动化测试已完成；合法账号真实链路验收待执行，暂不标记最终完成。**

实现记录见 [P1-G2-SUBSCRIPTIONS-BANGUMI-COURSE-SOURCES.md](P1-G2-SUBSCRIPTIONS-BANGUMI-COURSE-SOURCES.md)。

### 9.1 P1-G2 目标与非目标

接入追番、追剧、订阅合集和课程，把具有层级、权限与失效语义的来源纳入统一模型。

本组不实现 DRM 绕过、付费解锁、课程播放地址与下载提交、课程章节元数据写入和跨来源增量比较。

### 9.2 P1-G2 前置依赖与现有入口

- 前置依赖：P1-G0，可与 P1-G1 并行。
- 现有入口：GetBangumiCollectionAsync、BiliMediaType、现有 EP/SS 解析。
- 追番追剧和课程只访问当前账号合法可见内容。

### 9.3 P1-G2 接口和数据变化

- 实现 FollowingBangumiSourceProvider、FollowingCinemaSourceProvider、CollectionSourceProvider、CourseSourceProvider。
- ContentSourceItem 支持父集合稳定键和子项目稳定键，但最终媒体仍解析为 Aid + Cid。
- Provider 能力声明区分“列表分页”和“集合内分页”。
- 权限状态使用 Available、LoginRequired、PurchaseRequired、RegionRestricted、Expired 等结构化值。

### 9.4 P1-G2 实施步骤

1. 建立追番与追剧列表分页，并将 season/ep 层级映射为统一来源项。
2. 建立订阅合集发现和合集内项目分页。
3. 建立课程列表、课程章节与课时只读映射；课程下载不进入 P1-G2。
4. 复用现有番剧解析，把来源项展开为可提交分集。
5. 在 UI 中显示不可下载原因，不为无权限项目生成提交项。
6. 对同一 EP 在追番、历史和直接 SS 来源中的 MediaUnitKey 做一致性测试。
7. 建立 API 能力探测；字段缺失时安全隐藏不受支持的筛选项。

### 9.5 P1-G2 异常、兼容与安全

- PurchaseRequired、RegionRestricted 和 DRM 内容只能展示原因，不能进入下载预检。
- 合集或课程的父级可见但子项不可见时，保留可见项并逐项标记。
- 不把付费状态、订单信息或账号课程列表写入 SQLite。
- API 返回顺序变化不能改变 ContentItemKey。

### 9.6 P1-G2 测试矩阵

- 番剧/影视订阅的分页、未追内容、失效 EP。
- 合集多层级、重复视频和顺序变化。
- 课程免费/已购/未购/过期项目。
- 同一 EP 跨直接链接和订阅来源的媒体身份一致。
- 登录失效、区域限制、权限错误和字段缺失。

### 9.7 P1-G2 退出条件

- 四类来源通过统一 Provider 和契约测试。
- 无权限内容不会进入提交边界，也不会触发规避权限的备用请求。
- 层级来源能够稳定展开，父子身份和最终媒体身份职责清晰。
- EP 重复项在来源层和媒体层均可正确归一化；课程只验证稳定来源身份，不创建媒体提交项。

### 9.8 P1-G2 文档同步

- 更新来源权限矩阵和内容合规说明。
- 验收后更新 PRODUCT.md 的追番追剧、合集和课程状态。

## 10. P1-G3：大列表筛选与跨页选择

**时间：W19～W20，2026-09-16～2026-09-29**

> 实施状态：已完成（2026-08-07，提前实施）
>
> 实现证据：筛选规则指纹、能力拆分、文档会话级 LRU 页面缓存、generation guard、规则式跨页选择、提交期完整枚举与 Headless 虚拟化验收均已落地。

### 10.1 P1-G3 目标与非目标

将分页结果产品化，支持关键词、日期、类型筛选、当前页选择和全部匹配结果选择，并在至少 100 项规模下保持流畅。

本组不保存具体勾选项，不执行增量分类，不改变任务中心现有筛选模型。

### 10.2 P1-G3 前置依赖与现有入口

- 前置依赖：P1-G1、P1-G2。
- 现有入口：VideoListViewModel、VideoListView、现有任务中心虚拟化与筛选经验。
- Provider 能力声明决定哪些筛选可下推到服务器。

### 10.3 P1-G3 接口和数据变化

- 新增 SourceFilterRules：Keyword、PublishedFrom、PublishedTo、MediaTypes、SortOrder。
- 新增 ContentSelectionState 和 SelectionScope。
- 新增页面缓存接口，缓存键包含来源描述、筛选指纹和 continuation token。
- 服务端不支持的筛选由客户端只作用于已加载页；UI 必须明确显示作用范围。

### 10.4 P1-G3 实施步骤

1. 将来源结果列表改为增量加载和虚拟化集合。
2. 对筛选输入做防抖；旧请求取消后不得覆盖新筛选结果。
3. 为每次筛选生成稳定 FilterFingerprint。
4. 实现当前页全选、当前页全不选和逐项选择。
5. 实现“全部匹配结果”规则选择；提交时按页重新枚举并应用排除键。
6. 筛选变化时清除旧页面 token，保留可复用的已解析媒体缓存但不复用旧选择规则。
7. UI 展示已加载数量、已选择数量和“全部结果”范围。
8. 对 100、500、1000 个 fixture 项进行滚动、筛选和选择性能测试。

### 10.5 P1-G3 异常、兼容与安全

- 后返回的旧请求通过 generation ID 丢弃，不能污染当前页面。
- 全部结果枚举遇到来源变化时停止提交并要求重新确认，不提交不完整快照。
- 页面缓存有数量上限并仅驻留内存，不写入 Document。
- 筛选文本进入日志前脱敏并限制长度。

### 10.6 P1-G3 测试矩阵

- 关键词、日期边界、类型组合和排序。
- 快速切换筛选导致的取消与乱序响应。
- 页面回收后的选择保持。
- 当前页、全部结果、排除项和筛选变化。
- 100/500/1000 项虚拟化，不一次性创建全部项 ViewModel。

### 10.7 P1-G3 退出条件

- 大型来源可以稳定翻页，已加载页回访不丢选择。
- “全部结果”不会在未知总量下创建全部 UI 项。
- 筛选变化不会出现旧请求覆盖新结果。
- 100 项为正式验收基线，500/1000 项作为性能回归保护。

### 10.8 P1-G3 文档同步

- 更新 UI 交互说明、选择范围和性能基线。
- 记录服务端筛选与客户端筛选的能力差异。

已实现交互约定：

- 连续列表中的批量入口统一命名为“全选已加载”，不把最后一次加载批次伪装成“当前页”。
- 全选已加载后可显式升级为“全部匹配”；该状态只保存筛选指纹与排除键，提交前才逐页物化。
- 当前 Provider 未声明关键词、日期或类型能力时，筛选只作用于已加载内容；UI 明确提示范围，全部匹配提交仍会枚举完整来源并执行客户端残余规则。
- 显式逐项选择在筛选变化后保留；“全部匹配”在筛选变化后失效并要求重新确认。
- 页面缓存最多保留 32 页且仅存在于当前 Document 会话，不进入 Document、SQLite 或日志。

自动化证据：

- `BiliDownloader.Tests` 覆盖 100/500/1000 项筛选性能、规则选择、缓存淘汰、乱序响应、快照变化和提交期排除。
- `MyAvaloniaManagement.UiTests` 覆盖 1000 项来源首屏增量创建与视觉容器虚拟化，以及 1240/760/520 三种宽度和明暗主题布局。

## 11. P1-G4：Document V3 与可复用方案

**时间：W21，2026-09-30～2026-10-06**

**实际状态：已完成（2026-08-07）**

### 11.1 P1-G4 目标与非目标

将完整内容来源和下载规则保存为 Document V3。重新打开后恢复意图和本地任务投影，但不自动访问远端。

本组不保存远端完整页面、临时签名地址和跨页勾选项，不执行更新比较。

### 11.2 P1-G4 前置依赖与现有入口

- 前置依赖：P1-G3。
- 现有入口：DocumentSaveDataV2、DocumentSaveCodec、BiliDownloaderViewModel 的 CreateSaveDocumentMetaData 和 LoadDocumentByMetaData。
- V1/V2 兼容行为和未知版本安全读取必须保留。

### 11.3 P1-G4 接口和数据变化

- 新增 DocumentSaveDataV3 和 EncodeV3。
- SourceDescriptor 采用明确白名单 DTO，不直接序列化 Provider 内部对象。
- IncrementalBaseline 保存最后一次完整检查时间、来源快照 token、边界项目键和基线版本。
- DownloadProfile 扩展 P1 输出、附加资源和单任务限速字段。
- IsModified 覆盖来源、筛选和全部 P1 配置变化。

### 11.4 P1-G4 实施步骤

1. 定义 V3 DTO 和每个字段的安全默认值。
2. 实现 V1、V2 到运行时 V3 模型的单向迁移。
3. 实现 V3 保存、加载、未知字段容忍和未知主版本安全公共字段恢复。
4. 将来源、筛选、输出和附加资源配置接入 IsModified。
5. Document 打开时只恢复本地字段并查询自身任务投影，不调用 Provider。
6. 来源 Provider 不再存在时保留原始稳定描述，显示“不支持的来源类型”，允许用户查看和另存。
7. 为预设增加可复用的 P1 输出字段，但不把来源和增量基线保存到全局预设。

### 11.5 P1-G4 异常、兼容与安全

- V3 JSON 损坏时不部分提交任务；显示加载失败并保留原文件。
- V1/V2 缺失字段使用第 6.3 节默认值。
- 未知版本不能反序列化任意类型，只读取 DocumentId、标题、URL、输出目录等安全公共字段。
- SourceDescriptor 禁止 Cookie、Header、AccessKey、signed URL 等字段名和值。

### 11.6 P1-G4 测试矩阵

- V1→V3、V2→V3、V3 往返和重复保存。
- 未知字段、未知主版本、损坏 JSON、缺失 Provider。
- 全部来源类型与筛选规则序列化。
- 打开 Document 时零网络请求。
- 敏感字段扫描和 SaveData 快照测试。

### 11.7 P1-G4 退出条件

- Document 重新打开后，来源、筛选、预设、命名、目录和输出规则保持一致。
- V1/V2 无损打开并使用兼容默认值，保存后成为 V3。
- 打开和恢复过程不访问 B 站、不启动下载。
- Document 中不存在凭据和临时媒体 URL。

### 11.8 P1-G4 文档同步

- 已新增 [P1-G4 Document V3 与可复用方案](P1-G4-DOCUMENT-V3-REUSABLE-SCHEMES.md)。
- 已更新保存格式、默认值、离线恢复、安全白名单和向前兼容政策。
- 自动化覆盖 V1/V2/V3 迁移、九类来源、完整配置、缺失 Provider、零自动联网、损坏文件和强制另存。

## 12. P1-G5：增量检查与跨来源去重

**时间：W22～W23，2026-10-07～2026-10-20**

### 12.1 P1-G5 目标与非目标

实现用户主动触发的更新检查、五类差异结果、跨来源媒体归一化和提交锁内去重复检。

本组不实现后台轮询、计划任务、自动提交和自动删除失效任务。

### 12.2 P1-G5 前置依赖与现有入口

- 前置依赖：P1-G4。
- 现有入口：IDownloadTaskRepository、SubmissionPreflightService、BiliDownloadCoordinator、输出路径保留。
- 任务事实、成品文件事实和远端来源事实必须分开读取后再分类。

### 12.3 P1-G5 接口和数据变化

- 新增 IIncrementalComparisonService 与 ContentComparisonResult。
- SQLite 增加 media_unit_key 和 rendition_fingerprint，并建立查询索引；不对旧行建立会阻止迁移的唯一约束。
- RenditionFingerprint 包含 MediaUnitKey、视频/音频质量、编码、容器和输出模式，不包含 DocumentId 和来源类型。
- Document Baseline 只保存轻量边界；任务历史仍由 SQLite 提供。

### 12.4 P1-G5 实施步骤

1. 对选中来源执行完整或有界更新扫描，记录本次来源 snapshot token。
2. 将 ContentItemKey 展开为 MediaUnitKey；同一批次先按媒体身份聚合来源信息。
3. 应用 SourceFilterRules，产生 RuleExcluded。
4. 查询所有 Document 的相关任务，产生 Downloaded 或 InProgress。
5. 对没有匹配任务且满足规则的项目产生 New；以前存在但完整扫描确认消失的项目产生 Invalid。
6. 默认只勾选 New，其他分类可查看但不进入默认提交集合。
7. 用户提交后继续经过预检；Coordinator 命令锁内按 RenditionFingerprint 重新查询。
8. 若事实变化，返回 StaleComparison 并刷新分类，不沿用旧确认。
9. 完整检查成功后更新 Baseline；取消、失败或只加载部分页面时不推进完整基线。

### 12.5 P1-G5 异常、兼容与安全

- 检查更新和提交使用不同命令；任何检查结果都不能直接写 download_tasks。
- 本地成品缺失时不再显示为可信 Downloaded，交由 G6 文件检查确认。
- 旧任务无法生成完整指纹时使用兼容候选匹配并显示“身份不完整”，不静默阻止用户。
- 来源 API 部分失败时保留已加载结果，但不产生 Invalid，也不推进完整基线。

### 12.6 P1-G5 测试矩阵

- 五类状态及其优先级。
- 同一视频来自收藏夹、UP、历史和直接链接。
- 同一 MediaUnitKey 的 AVC MP4 与 AV1 MKV 合法共存。
- 检查后另一 Document 抢先提交导致 StaleComparison。
- 取消、部分页失败、重复 token、来源删除和规则变化。
- 检查更新零任务写入、零下载启动。

### 12.7 P1-G5 退出条件

- 检查更新不会自动创建或启动任务。
- 同一输出版本不会因来源不同而重复提交。
- 用户明确选择不同输出版本时不会被内容级去重误伤。
- 只有完整成功检查才更新可用于失效判断的基线。
- Coordinator 锁内复检可以关闭检查与提交之间的竞态。

### 12.8 P1-G5 文档同步

- 更新增量检查状态流、去重语义和用户确认原则。
- 在任务表迁移文档记录旧任务兼容策略。

### 12.9 P1-G5 实施记录

- 已实现平面与层级来源的用户主动完整扫描、部分结果安全语义和最多 100 项轻量基线。
- 已实现五类来源优先状态、跨来源 MediaUnitKey 聚合、版本化 RenditionFingerprint 与旧任务身份不完整警告。
- 已实现 SQLite 身份列及非唯一索引、预检同批去重和 Coordinator 锁内 `StaleComparison` 复检。
- 已实现 Document 增量结果面板、默认仅选择 New、取消、状态筛选和基于内存快照的无网络重新分类。
- 详细设计、迁移与验收证据见 [P1-G5 增量检查与跨来源去重](P1-G5-INCREMENTAL-COMPARISON-DEDUPLICATION.md)。

## 13. P1-G6：历史中心与安全导出

**时间：W24，2026-10-21～2026-10-27**

**状态：已实现并通过 SQLite 迁移、文件异常、重新下载、流式安全导出与 Headless UI 自动化验证（2026-08-07）。**

实现记录见 [P1-G6 历史中心与安全导出](P1-G6-HISTORY-CENTER-SECURE-EXPORT.md)。

### 13.1 P1-G6 目标与非目标

在 Tool 中建立历史中心，支持搜索、筛选、重新下载、文件定位、按需存在性检查和 CSV/JSON 脱敏导出。

本组不执行 P2 的全库完整性校验、哈希扫描、自动修复和孤立文件清理。

### 13.2 P1-G6 前置依赖与现有入口

- 前置依赖：P1-G5。
- 现有入口：SchedulerTaskListViewModel、TaskFilterSortEngine、IDownloadTaskRepository、FileRevealService、SensitiveDataSanitizer。
- 历史中心继续以 download_tasks 为事实，不复制第二套历史数据库。

### 13.3 P1-G6 接口和数据变化

- 新增 ITaskHistoryQueryService、IOutputFileStatusService 和 ITaskHistoryExporter。
- FilePresenceStatus 为 Unknown、Exists、Missing、Inaccessible。
- 导出 DTO 使用字段白名单，只包含稳定媒体标识、标题、状态、质量、实际输出信息、时间、脱敏错误摘要和本地路径。
- 重新下载以旧任务生成新的不可变 Submission，不复用旧 TaskId。

### 13.4 P1-G6 实施步骤

1. 在 Tool 增加“活动任务/历史”视图切换，复用虚拟化列表基础。
2. 支持标题、来源 Document、状态、创建时间、编码、容器和输出模式筛选。
3. 文件存在性默认 Unknown；用户点击检查当前项、选择项或当前筛选结果后才访问磁盘。
4. 存在文件允许定位；缺失文件提供重新下载入口。
5. 重新下载恢复稳定媒体身份和输出配置，重新执行预检、冲突和权限检查。
6. 实现 CSV 与 JSON 流式导出，避免一次性加载全部历史。
7. 导出前执行字段白名单构造和 SensitiveDataSanitizer 二次扫描。

### 13.5 P1-G6 异常、兼容与安全

- 网络路径、离线盘和权限不足映射为 Inaccessible，不等同 Missing。
- 应用启动不主动遍历历史路径，避免慢盘和网络盘阻塞。
- 导出路径由用户选择；失败时不留下看似成功的截断文件，使用临时文件后原子发布。
- CSV 防公式注入：以 =、+、-、@ 开头的文本字段必须安全转义。

### 13.6 P1-G6 测试矩阵

- 100、1000 条历史记录的筛选、虚拟化和流式导出。
- Exists、Missing、Inaccessible 和文件检查取消。
- 重新下载生成新 TaskId，并重新经过预检。
- CSV 逗号、换行、引号、公式注入；JSON 编码和大数据流。
- Cookie、Authorization、完整签名 URL、请求头和日志堆栈不出现在导出中。

### 13.7 P1-G6 退出条件

- 100 条历史记录可流畅搜索、筛选和批量文件检查。
- 文件存在性检查只由用户触发，不阻塞启动。
- 重新下载不会绕过预检，也不会覆盖旧任务事实。
- CSV/JSON 导出通过敏感数据和公式注入测试。

### 13.8 P1-G6 文档同步

- 更新 Tool 信息架构、历史字段和导出安全说明。
- 明确 P1 文件存在性检查与 P2 完整性检查的边界。

### 13.9 P1-G6 实施记录

- Tool 已拆分活动任务与终态历史，历史支持组合筛选、会话选择和虚拟化列表。
- 文件状态默认 Unknown，仅由用户命令以最多 4 路并发检查；网络、权限和离线盘映射为 Inaccessible。
- 新任务保存版本化提交快照；历史重下生成新 TaskId，旧任务事实不变并完整重跑预检。
- CSV/JSON 使用白名单流式写入、二次脱敏、公式防护、同目录临时文件和原子发布。
- 详细契约、迁移、导出 schema、安全边界与测试证据见 [P1-G6 实现记录](P1-G6-HISTORY-CENTER-SECURE-EXPORT.md)。

## 14. P1-G7：编码、容器与输出模式

**时间：W25～W26，2026-10-28～2026-11-10**

**状态：已实现并通过 Release 覆盖率门禁与固定 ffmpeg/ffprobe 离线媒体验收（2026-08-07）。**

实施记录见 [P1-G7-ENCODING-CONTAINER-OUTPUT-MODES.md](P1-G7-ENCODING-CONTAINER-OUTPUT-MODES.md)。

### 14.1 P1-G7 目标与非目标

允许用户选择服务端可用的 AVC、HEVC、AV1 视频流，输出 MP4 或 MKV，并支持音视频、仅视频和仅音频。

本组不转码、不补帧、不做画质增强，也不静默选择用户未授权的替代编码。

### 14.2 P1-G7 前置依赖与现有入口

- 前置依赖：P1-G4；SQLite 身份字段与 P1-G5 一起冻结。
- 现有入口：BiliDashResult、BiliDashStream.Codecid、SubmissionPreflightService、BiliDownloadService、IMediaMuxer。
- 现有固定 AVC 选择逻辑必须替换为可测试策略。

### 14.3 P1-G7 接口和数据变化

- VideoCodecPreference：AutoCompatibility、Avc、Hevc、Av1。
- OutputContainer：Mp4、Mkv、NativeAudio。
- OutputMediaMode：AudioVideo、VideoOnly、AudioOnly。
- 新增 IMediaStreamSelectionPolicy 和 MediaSelectionResult。
- 任务同时保存 selected_video_codec 与 actual_video_codec；Auto 也必须保存最终实际值。

合法组合：

| 输出模式 | 可选容器 | 行为 |
| --- | --- | --- |
| AudioVideo | MP4、MKV | 选择视频与音频并封装 |
| VideoOnly | MP4、MKV | 只下载视频流并封装 |
| AudioOnly | NativeAudio | 不请求视频流，按实际音频编码使用安全扩展名 |

显式选择的编码不可用时阻止该项并列出可用编码；AutoCompatibility 按 AVC、HEVC、AV1 的兼容顺序选择。

### 14.4 P1-G7 实施步骤

1. 完整解析 DASH stream 的 codec、mime type、带宽和实际容器提示。
2. 将流选择提取为纯策略，输入用户偏好和可用流，返回明确选择或结构化失败。
3. 在预检阶段验证输出模式、容器、编码与 ffmpeg 能力组合。
4. DownloadSubmission 和任务记录保存选择快照与最终实际选择。
5. 下载执行器按 OutputMediaMode 跳过不需要的流和进度阶段。
6. IMediaMuxer 支持 MP4、MKV、VideoOnly 和 NativeAudio 发布。
7. 最终路径扩展名由已验证选择确定，并继续经过 G6 路径保留。
8. 历史中心展示用户选择与实际输出，不根据扩展名猜测。

### 14.5 P1-G7 异常、兼容与安全

- 显式编码不可用时不回退；Auto 才允许按固定顺序选择。
- 容器与 codec 不兼容时预检阻止，不启动下载后再失败。
- AudioOnly 不创建空 video.tmp；VideoOnly 不创建空 audio.tmp。
- stream copy 失败保留已验证输入，继续复用 G7 的仅合并重试。
- 旧任务保持 AudioVideo + MP4 兼容读取，实际 codec 未知时不伪造值。

### 14.6 P1-G7 测试矩阵

- AVC/HEVC/AV1 的显式与 Auto 选择。
- 不同分辨率具有不同 codec 可用集。
- 三种输出模式与合法/非法容器组合。
- 仅音频、仅视频的恢复、进度、空间估算和路径扩展名。
- ffmpeg stream copy 成功/失败与仅合并重试。
- 使用可控样本和 ffprobe 校验实际 codec、format 和 stream 数量。

### 14.7 P1-G7 退出条件

- 流选择不再硬编码 AVC。
- 显式偏好不可用时用户得到可行动错误，不发生静默降级。
- 输出文件实际编码、容器和流数量与提交快照一致。
- 三种输出模式均支持暂停、恢复、失败和重新开始。

### 14.8 P1-G7 文档同步

- 更新下载配置、预检矩阵和任务字段说明。
- 验收后更新 PRODUCT.md 的 P1-05 状态。

### 14.9 P1-G7 实施结果

- DASH 完整解析 codec、MIME、源容器提示和普通/杜比/Hi-Res 来源。
- 纯选择策略落实同画质、无静默降级、字段矛盾未知化与普通 AAC 过滤。
- MP4、MKV、`.m4a` 贯通预检、保留路径、自动编号、迁移、恢复、历史和 UI。
- 三种模式只创建必要临时流，按 45/45/10、90/10、100 权重报告进度。
- 新任务使用独立 TaskId，会话项映射显式返回；实际编码在下载前原子落库。
- 660/660 自动化、覆盖率门禁及 4 组成品 ffprobe 验收通过，退出条件全部满足。

## 15. P1-G8：高规格媒体识别与选择

**时间：W27，2026-11-11～2026-11-17**

### 15.1 P1-G8 目标与非目标

识别并选择 HDR、杜比视界、Hi-Res 和杜比全景声能力，让 UI、预检、任务事实和实际输出保持一致。

本组不伪造高规格标签、不执行 HDR/SDR 转换、不绕过设备或账号权限。

### 15.2 P1-G8 前置依赖与现有入口

- 前置依赖：P1-G7。
- 现有入口：DASH 解析、质量选项、流选择策略和历史实际输出字段。
- 能力来源必须是 API/manifest 明确信息或经过媒体探测验证的信息。

### 15.3 P1-G8 接口和数据变化

- MediaFeatureFlags：Hdr、DolbyVision、HiResAudio、DolbyAtmos。
- VideoDynamicRangePreference：Auto、Standard、Hdr、DolbyVision。
- AudioFeaturePreference：Auto、Standard、HiRes、DolbyAtmos。
- MediaSelectionResult 返回 RequestedFeatures、ActualFeatures 和证据摘要。

### 15.4 P1-G8 实施步骤

1. 扩展 DASH DTO，保留动态范围、音频 codec 和高规格能力字段。
2. 建立能力映射表；未知值显示“未知”，不按清晰度或码率推测。
3. UI 只启用当前内容明确可用的选项，并显示登录/会员限制。
4. 将高规格偏好加入流选择与预检。
5. 显式选择不可用时阻止并列出标准流；Auto 只在可用且兼容时选择。
6. 保存请求与实际能力到任务历史。
7. 使用 ffprobe 对验收样本复核 codec、profile、色彩传输和音频流信息。

### 15.5 P1-G8 异常、兼容与安全

- API 标记与 ffprobe 结果冲突时任务进入媒体验证失败，不宣称高规格成功。
- 杜比视界或全景声无权限时显示权限限制，不尝试替代端点。
- 高规格选项不得泄露账号会员信息到导出字段。
- 旧任务的功能标志为 Unknown/None，不回填推断值。

### 15.6 P1-G8 测试矩阵

- 四类能力的存在、不存在、组合和未知字段。
- 显式、Auto、不可用和权限不足。
- API 标记与媒体探测一致/冲突。
- 请求能力与实际能力的持久化和历史展示。
- 不同容器对高规格流的预检兼容性。

### 15.7 P1-G8 退出条件

- UI 只展示有证据的能力，不根据标题或码率猜测。
- 显式选择无法满足时不会静默降级。
- 实际输出通过 ffprobe 与任务记录一致。
- 权限受限内容不进入规避权限的请求路径。

### 15.8 P1-G8 文档同步

- 更新媒体能力说明、限制和验收样本清单。
- 验收后更新 PRODUCT.md 的 P1-06 状态。

### 15.9 P1-G8 实施结果（2026-08-08）

- DASH 证据严格区分 HDR/DV、普通杜比/Atmos 与 FLAC/Hi-Res，不依据码率、标题或 URL 推断。
- 自动模式按“DV → HDR → 标准”和“Atmos → Hi-Res → 标准”选择；显式不可用或容器不兼容时阻止且不降级。
- 批量选择使用 250 ms 防抖、最多四路探测、脱敏会话缓存和全选项交集。
- 新任务使用快照 v2 与 `rf2:`，SQLite 区分旧任务 Unknown 和新任务 None；历史 JSON 升级 schema v2。
- 高规格 staging 在原子发布前经同目录 ffprobe 验证，冲突归类为 `media_validation` 并保留可信输入。
- Release 完整门禁 683/683、0 跳过；总体及 A/B/C 风险组覆盖率均超过现行阈值。
- 设计、限制、迁移和验收说明见 `P1-G8-HIGH-SPEC-MEDIA.md`。

## 16. P1-G9：字幕、软字幕与弹幕增强

**时间：W28～W29，2026-11-18～2026-12-01**

### 16.1 P1-G9 目标与非目标

支持字幕语言选择、SRT/ASS/VTT 外置格式、软字幕封装，以及弹幕 XML/ASS/JSON，并保留附加资源独立失败与重试。

本组不烧录硬字幕、不把弹幕嵌入视频、不下载或分发第三方字体。

### 16.2 P1-G9 前置依赖与现有入口

- 前置依赖：P1-G8。
- 现有入口：SubtitleExtrasHandler、DanmakuExtrasHandler、ProtobufDanmakuDecoder、ExtrasHandlerRegistry、IMediaMuxer。
- 附加资源继续在主媒体成功后执行，失败不反转主媒体结果。

### 16.3 P1-G9 接口和数据变化

- SubtitleSelectionMode：None、All、SelectedLanguages。
- SubtitleOutputFormat：Srt、Ass、Vtt。
- SubtitleDeliveryMode：External、SoftMuxed、ExternalAndSoftMuxed。
- DanmakuOutputFormat：Xml、Ass、Json，可多选。
- SubtitleOptions 保存语言稳定键、格式和交付模式；DanmakuOptions 保存格式集合与 ASS 样式。
- ExtrasResultSummary 记录逐语言、逐格式结果，不记录字幕下载 URL。

软字幕规则：

- MKV 保留 SRT/ASS 字幕轨并写入语言与标题元数据。
- MP4 将文本字幕转换为 mov_text 后封装。
- 不支持的字幕编码或容器组合在预检阶段阻止软封装，但仍可选择外置文件。
- 弹幕只生成外置 XML/ASS/JSON，不作为软字幕轨自动嵌入。

### 16.4 P1-G9 实施步骤

1. 字幕列表模型加入稳定语言键、显示名称和来源类型。
2. 将现有 SRT 获取与转换提取为字幕格式转换服务。
3. 实现 SRT、ASS、VTT 输出与安全文件命名。
4. IMediaMuxer 接受字幕轨描述，在最终原子发布前完成软字幕封装。
5. ProtobufDanmakuDecoder 增加 JSON 和 ASS 输出，XML 行为保持兼容。
6. ASS 转换使用确定性样式、屏幕尺寸和时间轴；不访问外部字体。
7. 附加资源结果按语言/格式持久化，并提供“仅重试失败附加资源”。
8. Document V3、预设和历史中心展示最终字幕/弹幕配置与结果。

### 16.5 P1-G9 异常、兼容与安全

- 某语言字幕失败不阻止其他语言和主媒体。
- 软封装失败保留已验证主媒体 staging 和外置字幕，可只重试封装。
- 字幕正文和弹幕正文不写日志；URL 继续脱敏。
- 文件名中的语言文本经过 FileNameSanitizer，稳定键用于避免同名覆盖。
- 旧 DownloadSubtitle/DownloadDanmaku 布尔字段按第 6.3 节迁移。

### 16.6 P1-G9 测试矩阵

- 多语言、重复语言、无字幕和机器翻译标记。
- SRT/ASS/VTT 转换、Unicode、换行和时间轴边界。
- MP4 mov_text 与 MKV 字幕轨，通过 ffprobe 校验语言、codec 和轨数。
- 弹幕 XML/ASS/JSON 的转义、排序、空集合和大分段。
- 单项失败、部分成功、仅重试失败附加资源。
- Document V2 布尔配置到 V3 结构化配置迁移。

### 16.7 P1-G9 退出条件

- 用户可以按稳定语言键选择字幕，并得到指定外置格式。
- MP4/MKV 软字幕输出通过 ffprobe 验证。
- 弹幕 XML/ASS/JSON 与同一输入产生确定性结果。
- 附加资源失败不破坏主媒体，且可以独立重试。

### 16.8 P1-G9 文档同步

- 更新附加资源格式、软字幕兼容矩阵和失败语义。
- 验收后更新 PRODUCT.md 的 P1-07 状态。

### 16.9 P1-G9 实施结果（2026-08-09）

- 字幕目录、内容获取、cue 规范化、SRT/ASS/VTT、软封装、ffprobe 验证和弹幕 XML/ASS/JSON 已拆分为窄接口并通过 DI 组合。
- 新任务快照升级为 v3；SQLite 使用可重入迁移保存结构化字幕、弹幕配置和版本化逐项结果，旧布尔配置保持兼容。
- 下载配置页支持手动有限并发检测、稳定语言选择、来源/覆盖数量、多格式和即时软字幕兼容提示；Document 恢复不联网。
- 主媒体与附加资源结果解耦，软封装候选在精确轨验证后原子替换；失败保留无字幕主文件，并可只重试失败附加资源。
- 活动任务、历史中心和安全导出已展示最终配置及逐项结果；摘要、日志和导出不保存正文、Cookie、Header 或下载 URL。
- Release 插件测试 701/701、全解决方案 1097/1097、0 跳过，编译 0 错误、0 警告，生产输出敏感数据扫描 0 问题；固定 ffmpeg 8.1.2 的 MP4/MKV 真实软字幕门禁已实现但当前机器缺少运行时，实网门禁同样待人工执行。
- 完整设计、矩阵、迁移、失败语义和验收命令见 `P1-G9-SUBTITLE-DANMAKU-ENHANCEMENT.md`。

## 17. P1-G10：限速、总回归与发布验收

**时间：W30，2026-12-02～2026-12-08**

### 17.1 P1-G10 目标与非目标

实现全局和单任务带宽限制，完成 P1 自动化、真实网络、真实 ffmpeg、性能、安全和兼容总验收。

本组不实现按时间段自动切换限速、网络自适应限速和操作系统级流量整形。

### 17.2 P1-G10 前置依赖与现有入口

- 前置依赖：P1-G5、P1-G6、P1-G9 全部完成。
- 现有入口：MultiConnectionDownloader、SchedulerSettingsViewModel、SettingsStore、Coordinator 运行时上下文。
- 并发任务数、单任务分块连接数和带宽限制是三个独立维度。

### 17.3 P1-G10 接口和数据变化

- 新增 IBandwidthLimiter，提供异步 AcquireAsync(bytes, taskId, cancellationToken)。
- GlobalBandwidthLimiter 使用共享令牌桶和公平任务队列。
- 每个任务具有 PerTaskBandwidthLimiter；实际读取必须同时获得全局与单任务额度。
- 全局 limit 保存到 settings，单任务 limit 保存到 DownloadProfile 快照和 SQLite。
- 0 表示不限速；负数、溢出和低于最小粒度的值在 UI/模型层拒绝。

### 17.4 P1-G10 实施步骤

1. 在 MultiConnectionDownloader 每次网络读取前接入带宽额度，不在写盘后补偿等待。
2. 全局限制按活动任务公平分配，空闲任务额度可被其他任务使用。
3. 单任务限制约束该任务全部分块连接的合计速率，而不是每个连接各自限速。
4. 运行中调整通过线程安全配置快照立即生效，不取消任务或重建断点。
5. 暂停、取消和关闭时唤醒等待中的限速请求，避免退出死锁。
6. UI 展示配置值和观测速率；速率文本不作为限速事实。
7. 执行 P1 全部自动化、性能、安全、兼容和真实链路验收。
8. 验收通过后更新 ROADMAP.md、PRODUCT.md、测试基线和发行说明。

### 17.5 P1-G10 异常、兼容与安全

- 限速器不能持有网络流或文件流，只控制读取许可。
- 系统时间跳变不得产生无限额度；使用单调时钟。
- 设置损坏时回退 0（不限速）并记录脱敏警告。
- 运行时降速不能丢弃已读取字节，升速不能重启任务。
- 总验收发现阻断问题时不更新产品“已实现”声明。

### 17.6 P1-G10 测试矩阵

- 单任务单连接、单任务多连接、多任务共享全局上限。
- 全局与单任务上限同时存在，实际约束取两者共同结果。
- 运行中从不限速切换限速、修改限速、恢复不限速。
- 暂停、取消、应用关闭和异常时无等待泄漏或死锁。
- LoopbackHttpServer 控制数据量和时钟，测试允许小范围调度误差。
- P1 全来源、Document V3、增量、历史、媒体、字幕弹幕的总回归。

### 17.7 P1-G10 退出条件

- 全局限速约束所有任务总和，单任务限速约束该任务所有分块总和。
- 运行中调整不破坏断点、任务状态和其他并发任务。
- P1 自动化测试、真实网络样本、ffprobe 媒体验收和安全扫描全部通过。
- P1 总退出条件逐项具备可复核证据。

### 17.8 P1-G10 文档同步

- 更新 ROADMAP.md 的 P1 状态和实际完成日期。
- 更新 PRODUCT.md 的 P1-01～P1-09 实现状态。
- 更新 BiliDownloader.Tests/TESTING.md、覆盖率基线和已知缺陷。
- 记录真实 API、真实 ffmpeg 和桌面交互验收结果。

### 17.9 P1-G10 实施结果（2026-08-10）

- 已实现全局公平令牌桶、单任务聚合限速、组合读取许可、单调时钟、运行时热更新和可取消等待；主媒体每次网络读取前按最大 8 KiB 申请额度。
- 全局值进入 settings，单任务值进入 Document/预设、提交快照 v4 与 SQLite；旧库和 v0～v3 任务按 0（不限速）兼容。
- Document、Tool 全局设置和活动任务卡片均以 KiB/s 编辑；配置值与既有观测速率分开展示。
- 新增专项离线测试与 `bandwidth` 发布门禁。本机 256 KiB/s、139,264 B 实测约 495 ms，热更新和取消通过。
- Release 插件 723/723、全解决方案 1119/1119、0 跳过；构建 0 错误、0 警告，覆盖率门禁全部通过。
- 用户提供的 2026-08-06 git ffmpeg 已通过 H.264 + AAC MP4 开发烟测，但正式门禁正确拒绝非固定 8.1.2 版本。
- 详细设计、迁移、日志意图、测试与未完成门禁见 [P1-G10 实施记录](P1-G10-BANDWIDTH-REGRESSION-RELEASE-ACCEPTANCE.md)。
- 固定版 ffmpeg 8.1.2、真实 B 站账号链路、桌面交互和最终敏感数据扫描尚未执行，因此本组与整个 P1 暂不标记正式完成。

## 18. 跨组迁移与兼容策略

### 18.1 Document 迁移

| 输入版本 | 加载行为 | 保存行为 |
| --- | --- | --- |
| V1 | 按旧字段加载并补齐 P1 安全默认值 | 保存为 V3 |
| V2 | 完整恢复 P0 配置并补齐 P1 默认值 | 保存为 V3 |
| V3 | 完整恢复来源、筛选、输出与基线 | 保存为 V3 |
| 未知版本 | 只恢复安全公共字段并警告 | 用户明确另存后写 V3 |

任何迁移都不能自动联网、自动解析来源或自动提交任务。

### 18.2 SQLite 迁移

- 所有新增列使用常量默认值，并在读取层兼容缺列。
- 先加列和索引，再由读取时或受控后台批次惰性回填身份，避免启动时长事务。
- 不删除或重命名 P0 字段，不重写旧任务状态。
- 旧任务的实际媒体信息未知时保持未知；只有真实重新下载或 ffprobe 验证后才能补齐。
- 新索引失败必须中止插件任务服务初始化并给出可行动错误，不能运行半迁移数据库。

### 18.3 预设迁移

- 内置“兼容”预设保持 AVC 优先、MP4、音视频、普通规格。
- 内置“质量”预设可以使用 Auto 编码，但不得默认强制会员或高规格能力。
- 内置“归档”预设优先 MKV 和外置附加资源，具体值需在 P1-G7/G9 评审冻结。
- 自定义旧预设缺失 P1 字段时按兼容默认值读取，用户保存后写入完整结构。

### 18.4 任务恢复兼容

- AudioVideo 旧任务沿用视频、音频、合并三阶段。
- VideoOnly、AudioOnly 使用明确阶段集合，恢复服务不能要求不存在的临时流。
- 新增字幕封装阶段必须具有持久化检查点，应用重启后仍由用户确认恢复。
- 限速值不影响断点合法性。

## 19. P1 总测试与验收矩阵

### 19.1 自动化层次

| 层次 | 重点 |
| --- | --- |
| 纯模型测试 | 稳定键、筛选指纹、选择状态、分类、流选择、格式转换 |
| Provider 契约测试 | 分页、token、取消、登录、空页、重复项、字段变化 |
| 持久化测试 | Document V1/V2/V3、SQLite 迁移、预设和任务往返 |
| ViewModel 测试 | 筛选防抖、跨页选择、无自动联网、用户确认 |
| 下载协议测试 | 输出模式、限速、恢复、Range、空间与取消 |
| ffmpeg/ffprobe 测试 | codec、container、stream、字幕轨和高规格标志 |
| 集成测试 | 多 Document、跨来源去重、检查后竞态、历史重新下载 |
| 安全测试 | 凭据、URL、导出、日志、CSV 注入和权限边界 |

### 19.2 必测场景

- 至少一个超过一页的 UP、收藏夹、历史、订阅和课程 fixture。
- 快速切换筛选导致旧请求晚返回。
- 跨页选择后卸载并重新加载页面。
- 同一媒体同时来自收藏夹、UP、历史和直接链接。
- 检查更新后另一 Document 提交同一输出版本。
- Document V1/V2/V3 打开、另存、未知版本和损坏数据。
- AVC/HEVC/AV1 与 MP4/MKV/仅视频/仅音频组合。
- HDR、杜比视界、Hi-Res、杜比全景声存在、缺失和权限不足。
- 多语言字幕、软字幕、弹幕三格式和部分失败。
- 全局/单任务限速、分块下载、运行中调整和取消。
- 100 项来源、100 项任务为正式规模门禁；500/1000 项作为回归压力样本。

### 19.3 真实链路验收

自动化测试不依赖线上 API。P1-G10 另行使用专门测试账号和用户有权访问的样本执行：

- 每类来源至少一次首末页检查。
- 登录失效、重新登录和权限不足。
- 一次跨来源重复内容检查。
- 每种输出模式和容器至少一个样本。
- 可合法取得时验证三种视频编码和高规格能力；无合法样本时记录未覆盖，不伪造通过。
- 使用 ffprobe 记录实际 codec、format、stream、subtitle 和 feature 证据。
- 全过程检查日志、SQLite、Document 和导出无凭据或签名 URL。

## 20. 风险与门禁

| 风险 | 控制措施 | 阻断条件 |
| --- | --- | --- |
| B 站 API 字段或游标变化 | Provider 隔离、fixture、结构化错误、能力声明 | 出现无限分页或错误删除判断 |
| 来源权限语义不一致 | 统一权限状态，不以空列表代替错误 | 无权限内容进入提交 |
| Document V3 膨胀 | 只保存意图和轻量基线，不保存完整页面 | 保存文件包含远端全量缓存 |
| 去重误判 | 内容键与输出指纹分离，锁内复检 | 不同输出版本被错误阻止 |
| 容器/codec 不兼容 | 预检组合矩阵和 ffprobe 验证 | 实际输出与任务快照不一致 |
| 高规格错误宣称 | 只接受明确能力证据 | UI 标签或历史记录无法验证 |
| 限速导致吞吐或死锁 | 单调时钟、公平队列、可取消等待 | 暂停/关闭无法在门限时间内完成 |
| 敏感数据导出 | 白名单 DTO + 二次脱敏 + 快照扫描 | Cookie、Header 或签名 URL 出现 |

任一阻断条件出现时，对应功能组不得标记完成，也不得更新 PRODUCT.md 的实现状态。

## 21. 产品需求追踪

| 产品条目 | 主要功能组 | 验收证据 |
| --- | --- | --- |
| P1-01 多内容源 | P1-G0、G1、G2 | Provider 契约、分页和权限测试 |
| P1-02 大列表筛选 | P1-G3 | 选择状态、虚拟化和规模测试 |
| P1-03 可复用方案 | P1-G4 | Document V1/V2/V3 迁移与往返 |
| P1-04 增量检查 | P1-G5 | 五类状态、零自动提交、跨来源去重 |
| P1-05 输出控制 | P1-G7 | ffprobe 编码、容器和流数量 |
| P1-06 高规格媒体 | P1-G8 | 能力证据、权限和实际输出验证 |
| P1-07 字幕弹幕增强 | P1-G9 | 多语言、多格式、软字幕和独立重试 |
| P1-08 历史中心 | P1-G6 | 100 项历史、文件检查和脱敏导出 |
| P1-09 速度控制 | P1-G10 | 多任务、分块和动态限速测试 |

## 22. P1 总退出条件

P1 只有同时满足以下条件才算完成：

- P0-G8 已完成，且 P1 没有破坏 P0 的启动安全、恢复和任务控制约束。
- 大型内容源可以稳定分页、筛选并保留当前会话的跨页选择。
- 打开 Document 不联网；检查更新不创建任务；新增内容经用户确认后才提交。
- 同一媒体不会因为来自不同来源而生成重复输出任务。
- 用户可以明确生成同一媒体的不同编码、容器或输出模式版本。
- Document V3 重新打开后，来源、筛选、预设、命名、目录、输出和增量基线保持一致。
- V1/V2 Document、旧预设和旧任务继续安全可读。
- 实际编码、容器、流数量、高规格标志和字幕轨与持久化任务快照一致。
- 历史导出不包含 Cookie、Authorization、请求头、签名 URL 或其他敏感数据。
- 全局和单任务限速在多任务、多分块和运行时调整场景下符合配置。
- 100 项来源和 100 项任务的正式规模验收通过。
- Release 构建 0 错误、0 警告，插件与解决方案全量测试通过。
- ROADMAP.md、PRODUCT.md、测试基线和发行说明在功能验收后完成同步。

## 23. 实施记录规则

每个 P1-Gk 开始前应从本文件拆出对应的详细设计与验收记录。实施过程中：

1. 先冻结目标、接口和迁移方案，再修改生产代码。
2. 数据库与 Document 迁移测试必须早于 UI 接入。
3. 每组完成后记录实际日期、提交基线、测试数量和遗留限制。
4. 与本文计划不一致的实现必须先更新并评审本文，不能让代码与路线图长期分叉。
5. 只有自动化、真实链路和安全门禁均满足时，才将对应产品条目标记为“已实现”。
