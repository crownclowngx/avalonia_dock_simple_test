# 测试过程中确认的现有缺陷与风险

本文件只记录生产代码现状。本轮按约定不修改 `BiliDownloader` 生产代码，也不把缺陷行为固化为通过测试。

## 1. 活动队列期间新 Ready 任务不会立即进入调度

- 复现：一个 Ready 任务已进入执行器并长期阻塞，此时把另一个 Failed/Interrupted 任务恢复为 Ready。
- 预期：只要并发槽位可用，新 Ready 任务应被处理。
- 实际：`ProcessQueueAsync` 等待现有活动任务完成后才重新读取仓储；恢复命令只调用幂等的启动方法，因此新任务可能一直等待第一个任务结束。
- 风险：长任务执行期间，用户恢复的其他任务无法及时使用空闲并发槽。
- 后续修复建议：为队列增加唤醒信号，或让命令在 Ready 集合变化后唤醒处理循环；修复后增加“阻塞任务 + 恢复任务立即占用第二槽”的回归测试。

## 2. 新增完整性字段没有在 InsertBatchAsync 中写入

- 涉及字段：`ExpectedVideoBytes`、`ExpectedAudioBytes`、`VideoIntegrityPassed`、`AudioIntegrityPassed`、`OutputFilePath`、`LastUpdatedAt`、`ErrorType`、`IsRetryable`。
- 预期：任务模型中已公开且表结构已创建的事实字段应能插入并读取往返。
- 实际：`InsertBatchAsync` 的列清单未包含这些字段，新记录会得到数据库默认值。
- 风险：未来恢复、错误分类和完整性校验可能读取到错误事实。
- 后续修复建议：补齐插入/更新列并增加全部字段往返测试。

## 3. 分块下载未验证 Content-Range 与实际块长度

- 复现：服务器返回 HTTP 206，但 `Content-Range` 起止位置错误，或响应体比声明区间短。
- 预期：拒绝合并不匹配的块，不生成最终文件。
- 实际：当前只检查状态码是否为 206，随后直接写入并合并。
- 风险：CDN、代理或恢复断点异常时可能生成静默损坏的媒体文件。
- 后续修复建议：校验 `Content-Range`、总长度、实际写入长度以及续传起点；修复后启用错误区间、短响应和总长度变化的回归测试。

## 4. 静态 ffmpeg 与直接 HttpClient 限制主链路隔离

- `BiliDownloadService.MergeAsync` 静态调用 `FfmpegService`。
- `CoverExtrasHandler` 和下载服务直接创建 `HttpClient`。
- 风险：成功主链路、进程参数、取消清理和 HTTPS 封面成功分支难以做快速确定的单元测试。
- 后续修复建议：注入 `IFfmpegService`、HTTP handler/client 与延迟/时钟抽象，再补全端到端离线回归。
