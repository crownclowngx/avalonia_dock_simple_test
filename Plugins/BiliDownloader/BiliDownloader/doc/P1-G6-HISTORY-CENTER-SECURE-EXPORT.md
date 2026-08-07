# P1-G6 历史中心与安全导出实现记录

> 完成日期：2026-08-07
> 事实源：`download_tasks`，不建立第二套历史数据库
> 边界：P1 只做用户主动的文件存在性检查；哈希扫描、自动修复和孤立文件清理仍属于 P2

## 1. 目标与信息架构

调度 Tool 现在分为“活动任务”和“历史”两个互斥视图：

- 活动任务：排队、运行、暂停、中断和等待登录，继续使用已有任务控制与虚拟化列表。
- 历史：完成、失败和取消，提供组合筛选、按需文件检查、文件定位、重试原任务、重新下载和安全导出。
- 可重试失败保留原任务恢复入口；恢复成功后状态变为排队并回到活动任务。
- 完成任务只有在用户明确检查为 `Missing` 后才显示重新下载；`Inaccessible` 不会被当成缺失。

Tool 激活和历史查询只读取 SQLite，不调用 `File.Exists`、`GetAttributes` 或目录枚举。文件状态在每次进程会话中从 `Unknown` 开始，也不会被写回数据库。

## 2. SOLID 职责边界

| 边界 | 职责 | 明确不负责 |
| --- | --- | --- |
| `ITaskHistoryQueryService` | 历史分页、流式查询、Document 选项、按 ID 读取 | 文件访问、导出编码、任务写入 |
| `IOutputFileStatusService` | 单项/有界批量文件状态检查和异常分类 | 自动扫描、哈希校验、修复 |
| `ITaskHistoryExporter` | 白名单投影、CSV/JSON 编码、原子发布 | UI 文件选择、磁盘存在性检查 |
| `ITaskHistoryRedownloadService` | 历史事实到新 `DownloadSubmission` 的映射 | 预检、用户确认、数据库提交 |
| `TaskHistoryViewModel` | 会话选择、命令快照和服务编排 | SQL、路径异常分类、CSV/JSON 细节 |

格式写入采用务实的策略分支，共享同一白名单投影与原子发布流程，没有为两个固定格式引入可变插件体系。UI、文件系统和保存对话框均通过窄接口注入，Headless 测试不依赖真实桌面。

## 3. SQLite 迁移与查询

`download_tasks` 增加以下可重入列：

- `submission_snapshot_version`、`duration_seconds`。
- `use_group_folder`、`add_index_to_title`、`naming_template`、`preset_id`。
- `selected_video_codec`、`actual_video_codec`、`output_container`、`output_media_mode`。
- `redownloaded_from_task_id`。

新任务写入快照版本 1。旧行保持版本 0，编码、容器和输出模式为空；读取层不按扩展名推断，也不修改旧状态、断点、输出路径或路径保留。

历史查询固定限定 `done / failed / canceled`，支持标题、Document、创建时间、选择编码、容器和输出模式组合过滤。标题 LIKE 参数会转义反斜杠、`%` 和 `_`。分页按 `created_at DESC, task_id DESC` 使用不透明游标，避免相同时间任务跨页重复。流式导出在同一个 WAL 只读事务中完成，使导出期间的后台状态变化不会产生内部不一致。

## 4. 文件状态语义

| 状态 | 判定 |
| --- | --- |
| `Unknown` | 尚未由用户触发检查 |
| `Exists` | `GetAttributes` 成功且目标是普通文件 |
| `Missing` | 路径为空、文件不存在、目录不存在或目标是目录 |
| `Inaccessible` | 权限不足、离线盘、网络 I/O、非法路径、安全限制等不确定失败 |

批量检查固定最多 4 路并发，操作范围在命令开始时固化。取消会停止未开始/未完成检查，已经交付的结果继续保留。`OperationCanceledException` 不会被转换成某个文件状态。

## 5. 重新下载

重新下载始终生成新的 GUID `TaskId`，并在新行记录 `redownloaded_from_task_id`。Aid/Cid、Bvid、EpId/SeasonId、Document、标题和安全 Cover URL 等稳定事实继续保留；旧行不会被覆盖或重排队。

版本 1 任务精确恢复质量、目录、分组、命名、附加资源、冲突策略、编码、容器和输出模式。版本 0 任务仅恢复可证明字段，缺失部分使用：

- `AutoCompatibility`
- `MP4`
- `AudioVideo`
- 默认命名模板

旧记录在预检前显示兼容警告；缺少 Aid/Cid、Bvid 和 EpId 的记录被拒绝。产生的新 Submission 继续经过统一预检、用户确认和 Coordinator 锁内复检，绝不复用旧覆盖确认、旧路径保留、断点或增量 token。

## 6. 导出格式与安全

JSON 为 UTF-8 无 BOM 的版本 1 对象：

```json
{
  "schemaVersion": 1,
  "exportedAt": "2026-08-07T20:00:00+08:00",
  "items": []
}
```

CSV 使用 UTF-8 BOM、稳定英文表头和 RFC 4180 双引号转义。首个非空白字符为 `= + - @` 的文本会增加单引号安全前缀，阻止电子表格公式执行。

导出白名单只包含任务/来源任务 ID、稳定媒体标识、Aid/Bvid/Cid/EpId/SeasonId、媒体类型、Document 信息、标题、状态、质量、选择/实际编码、容器、输出模式、最终路径、当前已知文件状态、时间、错误类型和脱敏错误摘要。

以下内容明确排除：Cover URL、临时目录、输出路径保留键、Extras 原文、请求头、日志和堆栈。所有字符串在构造白名单 DTO 后再次执行 `SensitiveDataSanitizer`；错误摘要只保留首个有效消息段并限制为 500 字符。

导出先在目标目录创建随机同级临时文件，写入、异步 Flush 和落盘 Flush 全部成功后才覆盖目标。失败或取消会尽力删除临时文件，原目标保持不变。

## 7. 自动化验证

2026-08-07 实施门禁证据：BiliDownloader Release 完整门禁 628/628 通过、0 跳过；总体行覆盖率 84.46%、分支覆盖率 67.56%，A/B/C 风险组均超过现行阈值。全解决方案 Release 回归 1024/1024 通过、0 跳过。

- 历史终态边界、组合筛选、LIKE 字面量、稳定分页、取消、输出规格索引。
- 新旧 SQLite 行、完整快照往返、未知值不伪造、重下来源链路。
- Exists、Missing、Inaccessible、权限/网络/安全异常、批量并发和取消。
- 完整/旧版重建、新 TaskId、真实预检与 Coordinator 提交、旧任务不变。
- 1000 条 CSV/JSON 流式导出、选中范围、Unicode、逗号、换行、双引号和公式注入。
- Cookie、Authorization、签名参数、Cover URL、临时目录、路径保留和堆栈扫描。
- 取消时旧目标保留和临时文件清理。
- Headless 宽窄布局及虚拟化列表验证。

## 8. P1 与 P2 边界

P1-G6 不启动定时扫描，不计算哈希，不判断媒体内容是否损坏，不自动修复任务，不扫描未知目录，也不清理孤立文件。上述能力需要独立的资源预算、用户授权和恢复策略，继续由 P2 专业归档功能组设计。
