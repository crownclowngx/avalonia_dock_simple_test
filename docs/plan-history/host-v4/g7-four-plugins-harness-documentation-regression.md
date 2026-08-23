# Host V4 G7：四插件、Harness 与文档回归

> 状态：已完成（2026-08-23）。输入提交为 G6 `e2b4b74`；本阶段只建立本地开发期回归证据，
> 不签署 V4 发布资格。G8 仍待实施，V4 尚未封板、尚不可发布。

## 1. 结论与实施范围

G7 证明 G1–G6 的 Host internal 收口没有破坏 V3 Plugin SDK、四插件组合、Document/Layout V2、
诊断脱敏和 MySmallTools 原生资源所有权。集成门禁连续暴露出 MySmallTools 关闭 Surface 时同步调用
LibVLC Stop、进而阻塞 Avalonia UI 线程的真实回归；按照任务书允许的例外，本阶段只在既有
`SecureVideoPlayer` 职责所有者中完成最小修复，并增加一个对应单元测试。SDK public API、插件业务契约、
manifest 和磁盘格式均未修改。

唯一入口为：

```powershell
pwsh -NoProfile -File .\scripts\Test-HostV4DevelopmentGate.ps1 -Stage G7
```

该入口先执行锁定还原、Release `-warnaserror`、Host 三层测试与结构检查，再串行复用 SDK、诊断和
四插件既有 V3 专项入口。子门禁各自负责测试、覆盖率、两次隔离测试包和真实 Host Loader；G7 只验证
子摘要的稳定字段并聚合证据，不复制插件规则。

## 2. SOLID 与朴素设计

| 原则 | G7 落点 |
| --- | --- |
| SRP | V4 门禁只编排和汇总；SDK、诊断、插件及 Harness 仍由各自专项入口负责。 |
| OCP | 通过新增 `G7` 阶段复用已验证入口，不修改四插件验收算法、SDK 或生产组合根。 |
| LSP | 四插件继续以相同 manifest、Provider、Registry、Workspace 与 Loader 契约运行。 |
| ISP | 聚合层只读取测试摘要所需字段，没有建立通用发布模型或 Host Facade。 |
| DIP | G7 依赖既有机器摘要和命令退出码；回归修复继续依赖已有原生调度端口，没有引入新的具体 LibVLC 依赖。 |

实现只使用顺序执行、精确清单、构造期数据和显式断言。没有增加 Gate Framework、事件总线、Manager、
服务定位器或新的生产接口。独立 `pwsh` 进程用于隔离 PowerShell 全局状态、Avalonia 资源、部署输出和
LibVLC 原生资源；这是测试所有权边界，不是新架构层。

脚本拒绝以下伪绿色：缺失套件、零测试、失败测试、非两次确定性构建、错误 manifest、空包、非规范
SHA-256、Harness 非零资源，或 `aiflow/windowsCi/windowsSmoke/releaseAcceptance/releaseGate/publishable`
任一标记不为 `false`。

## 3. 实际自动化证据

### 3.1 Host、SDK 与诊断

| 验证面 | 本轮结果 |
| --- | ---: |
| Host Unit | 210/210 |
| Host Headless UI | 63/63 |
| Host Plugin / Dock | 205/205 |
| Host 合计 | **478/478** |
| Host 行 / 分支覆盖率 | **85.06% / 71.41%** |
| Plugin SDK Tests | 37/37 |
| Core/UI V3 Shipped | 127 / 45 |
| Core/UI V3 Unshipped | 0 / 0 |

SDK API 门禁通过 7 个破坏性负例和兼容新增审阅流程；独立 NuGet 消费门禁通过 Core/UI 两个正例及
14 个反向消费夹具。诊断源码门禁扫描 108 个 Host 生产 C# 文件，默认路径未发现异常正文、自由技术
详情或完整路径输出。

Document V2 回归继续覆盖原生 JSON 往返、打开恢复、再次保存、并发同路径、V1 严格拒绝和失败不提交；
Layout V2 回归继续覆盖读取、运行时应用、Pinned/Hidden/Active、原子覆盖保存、非法结构拒绝和
`layout-v2.json` 唯一文件名。没有生成 v4 schema 或 v4 数据根。

### 3.2 四插件、真实包与 Host Loader

| 插件 | 专项通过 | 插件覆盖率 | ZIP 文件 | 本轮测试 ZIP SHA-256 |
| --- | ---: | ---: | ---: | --- |
| MyPlugTest | **527/527** | 消息器 98.15%，Codec 100% | 11 | `36148B03B04B3B3D2DA4368B81BDEE6D96E1E6972EE3F4FA3489E40B66945850` |
| DaTangAccountingHelpPlug | **578/578** | 70.09% / 49.31% | 9 | `A96116C2A4CA5E68305D0F721ABA36E6112C79FBB4E7CCF5B82EC520F5E2D010` |
| MySmallTools | **709/709** | 73.09% / 49.08% | 431 | `E96E53E10192A88D16CF68D67F58BA81574F93F9331F3142E5F91B97DB5D8E84` |
| BiliDownloader | **1245/1245** | 83.87% / 67.79% | 14 | `7E19588100DD34280F54CABB3B68FF05AB6EDD90C717C9EAC867D16B96698D8A` |

各专项总数包含各自重复执行的 SDK 与 Host 保护面，不能相加后冒充唯一测试数量。四个 ZIP 都经过两次
隔离构建和逐文件比较；manifest 均为 schema 2、插件版本 3.0.0、SDK `[3.0.0,4.0.0)`，并从解压后的
真实 `Controls` 目录进入 Host 发现、预检、独立 Provider、Registry 和 Workspace。它们是测试包，
不是 ReleaseAcceptance 或待上传发布包。

### 3.3 MySmallTools 资源 Harness

MySmallTools 使用 `g3` 套件完成 **20 轮** Windows x64 本地真实媒体循环。每轮覆盖加载与真实读取、
进入内容区全屏、全屏仍有效时直接关闭 Document、视觉树与 HWND 恢复、后续实例重新取得租约，以及
最终 Runtime 退出。机器报告满足：

- `success=true`、`allFinalResourcesZero=true`；
- `aliveClosedDocuments=0`、`aliveClosedViews=0`；
- `aliveDisposedEncryptedStreams=0`；
- Player、媒体输入、加密流、Surface Restore、Native Dispatcher、缓存及意外顶层窗口归零。

本轮机器报告耗时 **58,207 ms**，UI Heartbeat 最大间隔 **21 ms**，`Failures` 为空。

Harness 是资源所有权的本地集成测试，不是 Windows Smoke。运行中的 LibVLC 硬件解码与缩略图诊断
不构成失败；最终判断只取受控断言和机器报告。

### 3.4 门禁暴露的回归与最小修复

在最终集成过程中，MySmallTools Harness 连续停在“全屏仍有效时关闭 Document”。进程转储显示 UI
线程位于 `ManagedDockableViewLease.Release → VideoPlayerControl.OnDataContextChanged →
SecureVideoPlayer.DetachSurface → LibVLCMediaPlayerStop`。根因不是 Harness 超时阈值，而是 Surface
分离从 UI 回调同步进入原生 Stop，违反了播放器原有“所有 MediaPlayer 控制都经单消费者原生调度器”
的串行所有权。

修复保持职责朴素：`DetachSurface` 仍同步取得恢复快照并发出停止请求，但把可能阻塞的 Stop 排入已有
原生调度器后立即归还 UI 线程；下一次 Attach 必须等待前一次 Stop 完成，再恢复 Surface 和播放位置；
Dispose 也在同一调度器队列中完成最终 Stop。没有增加新接口、Manager 或并行播放器状态机。

新增单元测试以一个受控阻塞的假播放器证明两个次序事实：旧 Surface 分离不会等待原生 Stop 而阻塞
调用线程；新 Surface 恢复不会越过尚未完成的 Stop。修复后 MySmallTools 完整专项为 **709/709**，
20 轮真实媒体 Harness 继续满足全部资源和弱引用归零。

## 4. 文档、兼容与非发布边界

根 README、docs/Host 导航、架构评审、测试说明、兼容约束和 V4 任务书已同步到 G0–G7。文档门禁把
本记录、G7 当前状态、实际数量和非发布声明设为正向哨兵，并继续动态检查链接、脚本/项目路径、SDK
版本、Shipped/Unshipped 与四插件版本区间。

本阶段没有读取、初始化或修改 AIFLOW；没有运行 Windows CI、Windows Smoke、ReleaseAcceptance、
Host Release Gate、签名、上传、标签或发布。Release 只表示编译配置：

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
```

Windows Smoke、两轮无硬链接隔离和发布资格仍属于 G8/正式发布阶段。V4 最终签署清单中的对应项目保持
未完成，不能用 G7 测试包或真实媒体 Harness 替代。

## 5. 回滚

G7 的回滚单位是 `Test-HostV4DevelopmentGate.ps1` 的 G7 分支、四插件摘要字段、MySmallTools Surface
停止修复及其单元测试、文档门禁哨兵和全部 G7 当前文档。整体回滚后回到 G6 `e2b4b74`。不得只删除
摘要校验而保留“G7 已完成”表述，也不得只回滚异步 Stop 而保留依赖其时序的测试；更不得通过降低
覆盖率、放宽严格 reader、减少 Harness 轮数或跳过插件获得绿色。
