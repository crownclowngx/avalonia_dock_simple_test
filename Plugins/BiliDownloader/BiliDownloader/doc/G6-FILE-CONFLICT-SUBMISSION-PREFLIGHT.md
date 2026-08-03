# G6：文件冲突与提交预检

> 实施日期：2026-08-03  
> 状态：已完成  
> 平台：Windows、Linux  
> 验收基线：BiliDownloader 插件测试 350/350 通过

## 1. 目标与非目标

G6 将原来分散在 Document、Coordinator 和下载服务中的提交判断收敛为一条可等待、可确认、可复检的流程。完成后，最终 MP4 路径在任务入库前确定，后台执行只能使用持久化路径，不再在开始下载时临时猜测文件名。

本阶段实现：

- 跳过、覆盖、校验续传、自动序号四种文件冲突策略。
- 登录态、ffmpeg、输出目录、媒体大小、磁盘容量、文件及活动任务路径的统一预检。
- 批量预检的可提交、跳过、警告、阻止汇总。
- 用户确认后的提交锁内复检、SQLite 原子路径保留和批量任务写入。
- 写入阶段的空间硬检查、staging 合并和安全终态处理。

本阶段不安装 ffmpeg、不增加内容源、不新增输出容器、不处理 macOS，也不将 DASH 签名 URL 持久化。

## 2. 基线问题

G5 完成后仍有四个结构性风险：

1. `VideoListViewModel` 通过无返回值消息提交，无法知道后台是否真正入库。
2. `BiliDownloadService` 在执行阶段调用 `GetUniqueFilePath`，两个并发任务可能同时选中同一路径。
3. SQLite 使用 `INSERT OR REPLACE`，重复任务 ID 会静默覆盖既有事实。
4. ffmpeg 直接面向最终路径输出，覆盖失败可能损坏用户已有文件。

因此 G6 不能只增加一个 `File.Exists` 判断，而必须同时调整提交边界、持久化事务和最终文件发布方式。

## 3. 设计与 SOLID 边界

### 3.1 不可变提交与结构化报告

`DownloadSubmission` 继续作为 Document 到调度边界的不可变意图。`ISubmissionPreflightService` 返回 `SubmissionPreflightReport`，其中包含逐项计划路径、路径键、估算空间、续传任务 ID 和结构化问题码。

设计理由：预期业务失败使用结果对象表达，不用异常控制正常分支；UI、测试和 Coordinator 可以依据同一问题码处理，而不解析中文字符串。

### 3.2 Facade + 小接口

`SubmissionPreflightService` 是应用层 Facade，负责编排检查；DASH 大小估算和磁盘容量分别位于 `IMediaSizeEstimator`、`IStorageCapacityProvider` 之后。

设计理由：预检顺序需要集中管理，但网络和平台 I/O 不应泄漏到领域判断。接口保持很小，测试可使用固定结果替身，符合 ISP 与 DIP。

### 3.3 务实的策略分支

四种策略共享路径规范化、附加资源冲突探测、活动任务路径集合和序号分配。策略语义由持久化枚举决定，没有引入规则 DSL 或工作流引擎。

设计理由：冲突策略确实是可独立变化的业务维度，但当前只有四个稳定分支；共享无状态算法比建立复杂插件系统更容易审计。

### 3.4 Coordinator 仍是唯一提交者

`IDownloadSubmissionService` 只向 Document 暴露预检和提交。真正提交仍由单例 Coordinator 在 `_commandLock` 内执行：重新预检、比较事实指纹、建立任务记录、保留输出路径、批量入库并启动队列。

设计理由：预检和用户确认之间存在时间窗口。只有在唯一命令入口中再次读取事实，才能避免多个 Document 争抢同一路径。

## 4. 数据和兼容迁移

### 4.1 Document 与预设

- `FileConflictPolicy` 包含 `Skip`、`Overwrite`、`ResumeVerified`、`AutoNumber`。
- `DownloadProfile`、预设、不可变提交快照和 Document V2 均保存该字段。
- V1、旧 V2 和旧预设缺失字段时默认 `AutoNumber`，保持历史行为且不允许静默覆盖。
- Document 主版本保持 2.0；新增字段有安全默认值，不需要破坏性升级。

### 4.2 SQLite

`download_tasks` 原地增加：

- `output_path_key`：按当前平台大小写规则规范化的路径键。
- `conflict_policy`：任务提交时的策略快照。
- `estimated_required_bytes`：执行阶段空间复检依据，0 表示未知。
- `overwrite_confirmed`：覆盖任务的本批确认事实。

新增 `output_path_reservations`，以 `output_path_key` 为主键、`task_id` 为唯一键。路径保留和任务插入在同一 SQLite 事务中完成。完成、取消或删除释放保留；暂停、中断、等待登录和失败任务继续持有路径。

原有 `INSERT OR REPLACE` 改为 `INSERT`。重复任务 ID 或路径键返回唯一约束冲突，由 Coordinator 转换为“事实已变化，请重新预检”，既有任务不会被替换。

## 5. 预检与提交状态流

```text
用户点击提交
  -> Document 生成不可变 DownloadSubmission
  -> 预检登录 / ffmpeg / 目录 / DASH大小 / 磁盘 / 冲突 / 续传
  -> 有阻止项：整批停止并展开设置
  -> 有警告或冲突：展示数量与中文原因，等待明确确认
  -> Coordinator 命令锁内重新预检
       -> 指纹变化：返回 Stale，最多重新检查与确认 3 次
       -> 事实稳定：路径保留 + 任务批量 INSERT 同事务提交
  -> 后台执行只使用持久化 OutputFilePath
```

干净批次不增加额外确认步骤；跳过项不创建任务。覆盖、合法续传以及磁盘/大小未知必须确认。无窗口 owner 或用户取消均返回安全取消。

## 6. 冲突策略

### 6.1 跳过

主 MP4、同基础名 XML、封面、字幕或活动任务路径任一冲突时，该项记为跳过，不写任务库。

### 6.2 覆盖

预检列出冲突并使用“确认覆盖 N 项”按钮。任务只有持久化 `overwrite_confirmed=true` 后才能替换。ffmpeg 始终写入任务临时目录中的 staging 文件，成功后才原子替换最终文件；合并失败不会删除旧成品。

### 6.3 校验续传

续传匹配 Document、Aid、Cid、EpId、视频画质和音频质量，并要求任务处于暂停、中断或失败状态。临时目录必须存在，至少一个流具有可信预期长度，实际单文件或分块总长度不得超过预期。已有成品、孤立临时文件或身份不匹配均不视为可续传。

### 6.4 自动序号

按 `标题.mp4`、`标题 (1).mp4` 至 `标题 (9999).mp4` 分配。候选同时避开磁盘主文件、附加资源、本批计划和活动任务路径。Windows 路径键不区分大小写，Linux 保留大小写语义。

## 7. 磁盘和执行阶段安全

DASH 估算仅保存字节数，不保存 URL。峰值按音视频估算总量的 2.2 倍计算，用于覆盖临时流、最终 MP4 和安全余量。估算或容量未知时产生可确认警告；已知容量小于批次估算时阻止提交。

下载服务在开始、视频完成、音频完成和合并前重新读取容量，并扣除已下载断点字节。空间不足或目标被外部程序抢占时任务转为暂停，保留临时文件，错误类型分别为 `disk` 或 `conflict`。

附加资源从持久化 MP4 路径派生基础名，确保自动序号同时作用于字幕、弹幕和封面。

## 8. 自动化测试矩阵

- 四策略：主文件、XML、封面、字幕、本批重复和自动序号。
- 全局预检：未登录、ffmpeg 未就绪、目录创建/写入、磁盘不足和容量未知。
- 续传：匹配任务、合法长度、缺失预期长度和错误成品语义。
- 并发事实：SQLite 路径唯一保留、完成释放、预检后外部文件变化返回 Stale。
- 兼容迁移：旧 Document 默认自动序号、预设和 V2 往返。
- 回归：G0–G5 全部测试继续通过。

实施时插件测试由 334 项增加到 350 项，Release 配置下 350/350 通过。

最终门禁结果：

- `dotnet build MyAvaloniaManagement.sln -c Release -p:SkipPluginDeploy=true --no-restore`：0 错误、0 警告。
- `dotnet test MyAvaloniaManagement.sln -c Release -p:SkipPluginDeploy=true --no-build --no-restore`：735/735 通过，其中 BiliDownloader 350 项、宿主 UI 24 项。

## 9. 验收标准

- 未确认覆盖不可能到达最终替换调用。
- 自动序号在同批、跨 Document 和 SQLite 并发写入下保持唯一。
- 最终路径在任务入库时已经存在，执行阶段不重新编号。
- 续传只恢复匹配且长度合法的既有任务。
- 预检事实变化会重新确认，不沿用旧报告。
- 空间不足暂停任务并保留断点。
- 文档、预设和旧数据迁移均有自动化测试。
