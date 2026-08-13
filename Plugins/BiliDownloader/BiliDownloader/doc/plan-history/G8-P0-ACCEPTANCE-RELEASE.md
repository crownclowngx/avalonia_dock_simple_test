# G8：P0 验收与 Windows 插件发布

> 实施日期：2026-08-04
>
> 发布目标：MyAvaloniaManagement 的 BiliDownloader 插件，Windows x64、.NET 10
>
> 当前结论：实现与离线门禁通过；正式联网门禁未执行，因此 P0 尚未标记完成
>
> 本地证据：[TestResults/BiliDownloader/G8](../../../../../TestResults/BiliDownloader/G8/README.md)

## 1. 范围与发布判定

G8 只发布插件 ZIP，不发布完整宿主，也不上传任何外部平台。正式结果必须同时满足以下条件：

- 在 Windows x64、.NET 10 环境运行。
- 工作树干净，未使用 `-AllowDirty`。
- 未使用 `-SkipLiveAcceptance`，且真实 Bilibili、ffmpeg 和 Range 恢复门禁通过。
- Release 构建 0 警告、0 错误；插件与全解决方案测试均为 0 失败、0 跳过。
- 候选目录能由宿主的 `PluginLoadContext` 加载。
- ZIP 文件集、长度、SHA-256、RID 和宿主共享程序集边界全部通过复验。
- 数据库、日志、报告、文本、二进制和解压包未发现可复用敏感信息。
- `git diff --check` 通过。

`-AllowDirty` 和 `-SkipLiveAcceptance` 只用于开发者本地复核。任一参数出现，清单和验收报告都必须写入 `publishable: false`，不能把候选改名后当作正式包。

## 2. 授权缺陷修复与职责边界

验收发现旧流程会在登录成功消息到达时自动恢复所有 `WaitingForLogin` 任务。这违反“凭据恢复不等于用户授权下载”的产品约束，也可能在用户只想登录时产生网络和磁盘副作用。

修复后的状态顺序为：

1. 登录消息只更新登录态和界面，不触发任务调度。
2. 用户从单项或批量命令显式调用 `ResumeTaskAsync`。
3. Coordinator 对 `Paused` 和 `WaitingForLogin` 再次校验当前会话凭据；等待登录且无凭据时保持原状态，不访问下载执行器。
4. 状态先持久化为 `Ready`，再通知 UI，最后释放暂停上下文或进入调度。

这个顺序保证 SQLite 始终先于消息成为事实；即使进程在通知或调度阶段退出，重新加载也能得到可解释状态。生产数据结构、Document V2 和 `ResumeTaskAsync` 签名均未改变。

系统仍保持以下单一职责：

- Coordinator 是唯一任务命令和调度入口，不监听登录消息启动下载。
- 凭据服务只提供当前会话，不拥有任务恢复策略。
- ViewModel 只表达用户意图，不直接改任务事实或启动下载器。
- SQLite 是唯一任务事实源，消息仅负责投影刷新。
- 文件名清理由 `FileNameSanitizer` 唯一负责；已删除无人调用的过时兼容 API。

## 3. 门禁设计

`BiliDownloader.ReleaseAcceptance` 是独立控制台项目。它采用窄接口 `IReleaseGate`，并由顺序 Pipeline 组合门禁：

- `LiveFfmpegInstallationGate`：从固定清单下载 ffmpeg，验证 SHA-256、解压、原子激活和版本探测。
- `LiveBilibiliDownloadGate`：验证临时登录态，解析公开测试 BVID，下载最低可用 DASH 音视频并真实合并。
- `LiveRangeRecoveryGate`：执行 20 次真实 Range 中断恢复，至少 19 次字节完全一致才通过。
- `LivePersistenceEvidenceGate`：经生产 AES-GCM 与 SQLite 边界生成凭据库、任务库、Document 和日志扫描证据。
- `SensitiveEvidenceGate`：扫描精确 Cookie 字节、常见 Cookie 键、Authorization、签名查询参数、完整敏感 URL 和 SQLite 文本事实。
- `PackageVerificationGate`：独立解压 ZIP，检查路径穿越、封闭文件集、RID、长度、哈希和宿主共享程序集。

这里只使用了组合式门禁、适配器和依赖倒置三个直接解决问题的模式。每个门禁只有一种失败理由域，可以单独测试和替换；PowerShell 不复制业务判断，避免为了“模式完整”引入额外抽象层。

## 4. 自动化验收矩阵

| 域 | 关键场景 | 证据 |
| --- | --- | --- |
| 授权与调度 | 登录消息不自动恢复；显式单项/批量恢复；无凭据拒绝；三阶段恢复；活动任务删除；并发槽位；进度落库顺序 | Coordinator 单元/集成测试 |
| SQLite | 安全纪元初始化；历史任务迁移；幂等初始化；字段保留；敏感旧状态清理 | 存储迁移测试 |
| HTTP | Range、断流、错误 Content-Range、CDN 回退、恢复和完整性 | Loopback 集成测试 |
| 离线恢复 | 固定种子的 100 个中断点、分块形状和本地长度组合，要求 100/100 | G8 ReleaseAcceptance 测试 |
| UI | 三个真实 Document 独立投影；单 Tool 汇总与隐藏重建；100 任务投影；等待登录入口和确认语义 | Avalonia Headless 测试 |
| 安全 | 文本、二进制、SQLite、报告和 ZIP；精确 secret 与结构化规则的正反例 | 敏感扫描测试 |
| 包 | 正确 win-x64 包通过；额外 Linux RID、清单外文件和哈希变化拒绝 | 包复验测试 |
| 宿主 | 从候选目录创建 `PluginLoadContext`、发现模块并加载私有 SQLite 依赖 | Host PluginTests |

## 5. 发布入口与敏感信息约束

本地离线候选：

```powershell
.\scripts\Release-BiliDownloaderP0.ps1 `
  -AllowDirty `
  -SkipLiveAcceptance `
  -EvidenceRoot 'TestResults\BiliDownloader\G8'
```

正式发布在干净工作树中执行：

```powershell
$env:BILIDOWNLOADER_G8_TEST_BVID = 'BV...'
$env:BILIDOWNLOADER_G8_COOKIE = '<temporary-cookie>'
.\scripts\Release-BiliDownloaderP0.ps1 `
  -EvidenceRoot 'TestResults\BiliDownloader\G8'
```

Cookie 只通过环境变量进入验收进程内存，不允许作为命令行参数。脚本读取后会立即清除父 PowerShell 中的两个变量，build/test 子进程不会继承；仅 live 和最终 scan 门禁通过临时子进程环境获得 Cookie。报告只记录门禁名称、脱敏摘要和数值指标；外部请求异常只记录异常类型，避免签名 URL 进入证据。

## 6. Windows x64 插件包边界

ZIP 根目录包含：

- `BiliDownloader.dll`、PDB 和 deps 文件。
- Flurl、SQLitePCLRaw、Microsoft.Data.Sqlite、protobuf-net、QRCoder 等插件私有托管依赖。
- 仅 `runtimes/win-x64/native/e_sqlite3.dll` 的 SQLite 原生资产。
- `bilidownloader.release.json`，记录插件版本、目标框架、RID、源提交、可发布标志，以及每个 payload 文件的长度和 SHA-256。

ZIP 不包含 Avalonia、`MyAvaloniaManagementCommon` 等宿主共享程序集，不包含 Linux、macOS、win-x86 或 win-arm64 RID，也不包含完整宿主程序。

## 7. 2026-08-04 本地候选结果

本次在 dirty worktree 上显式跳过真实联网门禁，结果只能用于复核实现：

| 项目 | 结果 |
| --- | --- |
| BiliDownloader Release 构建 | 0 警告、0 错误 |
| BiliDownloader 测试 | 396/396，通过；0 失败、0 跳过 |
| 全解决方案 Release 构建 | 0 警告、0 错误 |
| 全解决方案测试 | 783/783，通过；0 失败、0 跳过 |
| 宿主候选加载 | 1/1，通过 |
| ZIP 文件集与 SHA-256 复验 | 通过，13 个 payload 文件 |
| 敏感信息扫描 | 通过，24 个文件、0 个问题 |
| 真实网络、ffmpeg、20 次 Range 恢复 | 未执行 |
| 发布资格 | `publishable: false` |

候选文件为 `BiliDownloader-p0-win-x64-0b5dcf6a1ec4.zip`，长度 2,068,086 字节，SHA-256 为 `94AA31A49984205A9000FA49AD818E42F8A732F7BBC33BCF80FC4E65C100DAD0`。该摘要只对应本地 dirty 候选，不能作为正式发布摘要。

## 8. 正式完成后的文档动作

只有在 clean worktree 上完成真实联网门禁并生成 `publishable: true` 后，才能执行以下动作：

1. 在本文件追加正式源提交、真实恢复成功率、包长度和 SHA-256。
2. 将 `ROADMAP.md` 的 G8 与 P0 标记为完成。
3. 按代码事实更新 `PRODUCT.md` 的当前阶段、Windows x64 发布范围及 P0-01～P0-10 状态。
4. 保留忽略目录 `artifacts/BiliDownloader/p0-win-x64` 中的机器产物，并把脱敏证据复制到独立的 `TestResults/BiliDownloader/G8`。

在此之前，P1 日期虽已重排为 2026-08-05～2026-12-08，但仍受 G8 正式门禁约束，不能绕过发布验收直接宣称进入 P1。
