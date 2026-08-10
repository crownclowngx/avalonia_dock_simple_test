# P1-G10 限速、总回归与发布验收实施记录

> 实施日期：2026-08-10
> 当前结论：限速代码、离线测试和本机门禁已完成；候选版本不可正式发布，固定 ffmpeg 8.1.2 与真实 B 站账号链路仍待执行。

## 1. 已实现范围

- 全局主媒体上限保存到 settings，所有活动任务共享一个公平令牌桶。
- 单任务上限随 Document/预设进入提交快照 v4，并保存到 SQLite；同一任务的视频、音频和全部分块连接共享该额度。
- `MultiConnectionDownloader` 在每次最多 8 KiB 的网络读取之前申请额度，不在写盘后补偿等待。
- 运行中可修改全局或单任务上限；顺序固定为先持久化、后热更新，不取消任务、不清空断点。
- 0 表示不限速；非零最小值为 64 KiB/s。UI 使用 KiB/s，持久化使用 B/s。
- 限速范围只包括视频/音频主媒体读取，不包括 API、封面、字幕、弹幕和 ffmpeg 本地 I/O。

## 2. 设计与 SOLID

传输层只依赖 `IBandwidthLimiter`；全局设置使用 `IGlobalBandwidthLimitService`，运行时任务表使用 `ITaskBandwidthLimitManager`，时钟使用 `IBandwidthClock`。读取许可、配置控制、持久化和 UI 编辑分别通过窄接口隔离。组合器把任务桶与全局桶串联，下载器不认识 token bucket、settings 或 SQLite。

使用的模式均服务于明确问题：

- Token Bucket：允许短量子突发并约束长期吞吐。
- Round Robin：全局等待队列按 taskId 轮询，避免多分块任务淹没其他任务。
- Composite：同一次读取同时满足任务上限与全局上限。
- Application Service：设置先落盘再应用，集中处理损坏值回退和日志。
- Lease：任务执行期激活 limiter，退出时聚合统计并释放。

限速器不持有 HTTP 流或文件流。单调时钟避免系统时间回拨产生额度；每次补充最多按一秒计算，避免异常时钟跳变积累无限 burst。内部延时与唤醒竞争后会取消未胜出的等待，暂停、取消和关闭不会遗留信号等待。

## 3. 持久化与兼容

- settings key：`global_media_rate_limit_bytes_per_second`。
- SQLite 列：`task_rate_limit_bytes_per_second INTEGER NOT NULL DEFAULT 0`。
- 新任务 `submission_snapshot_version=4`；v0～v3 历史重下按不限速兼容，不能把迁移默认值伪装为旧用户选择。
- 旧数据库通过幂等 `ALTER TABLE` 加列，默认行为不变。
- 损坏的全局设置只在内存回退为 0 并写脱敏警告，不覆盖原值，便于诊断。
- 已完成任务的限速快照属于历史事实，运行时编辑被拒绝。

## 4. 日志意图

日志记录配置状态转换与聚合结果，不逐次记录 8 KiB 读取：

- 全局初始化：原始持久化值、生效值和作用范围。
- 全局/任务热更新：原因、旧值、新值、是否命中活动 limiter，说明断点不变。
- 任务激活/释放：配置值、生存期、许可字节、许可次数、取消等待、累计/最大等待。
- 损坏设置：合法边界和安全回退，但不包含 Cookie、Header 或媒体 URL。

这样既能解释“为什么正在变慢/为什么已恢复”，也避免高频日志反过来损害吞吐。

## 5. 自动化与本机证据

专项覆盖以下行为：边界与溢出、单任务多连接合计、多任务公平、全局和任务组合、运行时解除限制、取消等待、读取量子与 taskId、settings 损坏、SQLite 往返和完成事实保护。

Release 最终自动化结果：插件 723/723、全解决方案 1119/1119、0 失败、0 跳过；全解决方案构建 0 错误、0 警告。覆盖率为总体行 83.55% / 分支 67.50%，A 组 89.44% / 77.49%，B 组 85.23% / 69.44%，C 组 74.91% / 56.35%，所有既有门槛均通过。

对候选生产输出执行敏感证据扫描，共扫描 5 个文件，Cookie、Authorization、签名 URL 等规则命中 0 项。真实账号运行后产生的日志、数据库、Document 与导出仍必须在正式实网门禁中再次扫描。

本机 `bandwidth` 发布门禁结果：

| 项目 | 结果 |
|---|---|
| 配置 | 256 KiB/s |
| 计量字节 | 139,264 B |
| 实测时间 | 约 495 ms |
| 热更新解除等待 | 通过 |
| 取消等待 | 通过 |

用户提供的 `D:\soft\ffmpeg-2026-08-06-git-95c43d7df7-full_build\bin` 已完成开发态 MP4 烟测：生成 1 秒 H.264 + AAC 文件，ffprobe 识别 1 路视频、1 路音频和 MP4 容器。正式 `media-output` 门禁按设计拒绝该版本，因为冻结供应链只接受 8.1.2；因此不能把本次烟测记为正式 ffmpeg 发布通过。

## 6. 尚未满足的正式门禁

- 使用固定 ffmpeg/ffprobe 8.1.2 完成全部容器、输出模式、字幕轨与高规格媒体门禁。
- 使用专门测试账号和有权访问的真实样本完成来源首末页、登录失效、跨来源重复和媒体下载。
- 完成桌面交互检查以及对最终日志、SQLite、Document 和导出的敏感数据扫描。

在这些外部验收完成前，ROADMAP 与 PRODUCT 只能标记“实现完成、待发布验收”，不得宣称 P1 正式完成。

## 7. 复核命令

```powershell
dotnet run --project .\Plugins\BiliDownloader\BiliDownloader.ReleaseAcceptance\BiliDownloader.ReleaseAcceptance.csproj -c Release -p:SkipPluginDeploy=true -- bandwidth --sandbox <临时目录> --report <报告.json>

dotnet run --project .\Plugins\BiliDownloader\BiliDownloader.ReleaseAcceptance\BiliDownloader.ReleaseAcceptance.csproj -c Release -p:SkipPluginDeploy=true -- media-output --ffmpeg <8.1.2 ffmpeg.exe> --ffprobe <8.1.2 ffprobe.exe> --sandbox <临时目录> --report <报告.json>

.\Plugins\BiliDownloader\BiliDownloader.Tests\Run-Tests.ps1 -NoRestore -KeepResults
```
