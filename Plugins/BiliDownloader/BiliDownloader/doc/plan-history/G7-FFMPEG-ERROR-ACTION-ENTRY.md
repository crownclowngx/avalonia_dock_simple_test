# G7：ffmpeg 安装修复与错误行动入口

> 状态：已实现（2026-08-04）
> 产品条目：P0-08、P0-10
> 自动化证据：BiliDownloader Release 构建 0 错误、0 警告，384/384 测试通过

## 1. 目标与非目标

G7 解决两个相互关联的问题：让缺少或损坏 ffmpeg 的用户能够主动修复依赖；让下载失败不再停留在技术异常文本，而是始终提供与错误类型匹配的下一步行动。

本阶段目标：

- 不修改系统 `PATH`，为 Windows x64 提供用户主动触发的固定版本安装与修复。
- 保留重新检测和自定义 `ffmpeg.exe` 路径，并显示实际来源、版本和路径。
- 在媒体下载并校验后持久化可信检查点，使 ffmpeg 修复后可以仅重试合并。
- 将持久化错误统一映射为登录、ffmpeg、目录、磁盘、网络、CDN、资源、合并、冲突和未知十类，并提供有限、可测试的行动。
- 保证安装失败、取消或合并失败不会破坏旧可用版本、旧成品和可信临时媒体。

本阶段明确不做：

- 插件启动时不联网、不下载依赖，也不静默重试历史任务。
- 不信任远端“最新版本”或远端动态摘要，不建立自动更新渠道。
- 不修改系统或用户 `PATH`，不要求管理员权限。
- 非 Windows x64 不执行内置安装；仍可重新检测或选择适合当前平台的自定义路径。
- 不为每个错误创建空壳策略类，不引入通用工作流框架。

## 2. 当前用户入口

- 设置页提供“重新检测”“安装内置版本/修复”“选择自定义路径”，并显示来源、版本、实际路径、安装进度和结果建议。
- 任务中心失败卡片展示简洁摘要、主行动、次行动；紧凑布局通过“更多”菜单承载相同行动。
- Document 提交预检继续使用结构化问题码；登录、ffmpeg、目录和磁盘阻止项可以直接执行对应操作。
- “查看日志”打开 `IBiliDataPaths.LogDirectory`，界面不直接展示长堆栈或完整 ffmpeg stderr。

设置页单独修复 ffmpeg 不会擅自重试任务。只有从某个失败任务的“安装/修复并继续合并”入口进入时，修复成功后才调用该任务的合并重试。

## 3. SOLID 边界与务实设计

原有宽泛边界被拆为三个单一职责接口：

| 接口 | 唯一职责 | 设计理由 |
| --- | --- | --- |
| `IFfmpegRuntimeLocator` | 加载自定义路径、按优先级重新探测并返回结构化状态 | 探测不应依赖安装或媒体合并，启动阶段可以安全地只做本地检查 |
| `IFfmpegPackageInstaller` | 串行编排安装/修复、进度、取消和失败回滚 | 将高风险供应链状态流集中为一个 Facade，同时保持下载器和平台判断可替换 |
| `IMediaMuxer` | 调用已验证的 ffmpeg 合并媒体 | 下载执行器只依赖合并能力，避免获得修改运行时或安装依赖的权限 |

为了兼容已有调用，`IFfmpegService` 暂时继承定位与合并两个窄接口；新代码直接依赖所需的窄接口。HTTP 下载位于 `IFfmpegPackageDownloader` 后，平台能力位于 `IFfmpegInstallPlatform` 后，因此测试不访问真实网络，也不依赖开发机平台。

安装流程采用 Facade，而没有创建多层工作流对象。理由是步骤顺序、安全不变量和回滚边界必须集中可见；哈希、ZIP 校验、进程探测和指针切换仍是小型基础设施方法，避免为一次线性事务引入抽象膨胀。

## 4. 固定供应链信任清单

| 字段 | 固定值 |
| --- | --- |
| 发行方 | Gyan Windows builds（FFmpeg 官方下载页列出的 Windows 构建来源） |
| 版本 | `8.1.2` |
| 包 | `ffmpeg-8.1.2-essentials_build.zip` |
| URL | `https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip` |
| SHA-256 | `db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec` |
| 压缩包上限 | 256 MiB |

版本、URL 和摘要作为同一个编译期清单进入代码评审。运行时不会抓取远端“最新”元数据；摘要使用固定时间比较，避免把被篡改的包激活为可信运行时。来源参考：[FFmpeg 下载页](https://ffmpeg.org/download.html)、[Gyan 构建页](https://www.gyan.dev/ffmpeg/builds/)。

## 5. 安装状态流与失败回滚

安装或修复只由用户按钮触发，且同一进程内通过信号量串行化：

1. 在插件临时目录创建本次操作独有目录和 `.part` 文件，下载过程限制声明大小与实际大小。
2. 计算本地 SHA-256，与编译期摘要固定时间比较；不匹配立即拒绝。
3. 解压到唯一 staging 目录。拒绝绝对路径、`..` 越界、链接条目、重复目标、展开体积超限，以及缺少或重复的 `bin/ffmpeg.exe`。
4. 执行候选文件的 `ffmpeg -version`，只有正常退出且输出有效才继续。
5. 将 staging 移到 `DataDirectory/dependencies/ffmpeg/versions/<version>-<installId>`。
6. 先写入同卷临时 JSON，再覆盖移动为 `current.json`。只有活动指针切换成功，新版本才对定位器可见。
7. 强制重新探测活动版本；成功后尽力清理非活动旧版本。

在每次操作开始前保存旧指针和旧自定义路径。下载、摘要、ZIP、移动、指针写入、重新探测或取消任一步失败时，清理本次 `.part`、staging 和未激活版本，并恢复旧指针与旧定位状态。清理旧版本始终是成功后的尽力操作，清理失败不能反向破坏已经验证并激活的新版本。

## 6. 运行时定位与启动约束

探测优先级固定为：

1. 已通过进程验证的自定义路径；
2. `current.json` 指向的托管版本；
3. 插件目录中的 `ffmpeg.exe`；
4. 系统 `PATH`。

无效自定义路径不会遮蔽可用托管版本。`IsReady` 只反映最近一次真实探测结果，而不是“路径字符串存在”。插件生命周期启动时仅加载 `ffmpeg_custom_path` 设置并执行本地 `-version` 探测，不发起任何 HTTP 请求。

内置安装只支持 Windows x64。其他平台会返回明确的不支持结果，但定位器、自定义路径选择和合并接口保持跨平台边界。

## 7. 媒体检查点与仅重试合并

`DownloadExecutionCallbacks` 统一承载进度、字节事实和 `MediaReadyCheckpoint`，避免继续扩张执行器参数列表。视频与音频完成长度和完整性校验后、启动 ffmpeg 前，执行器等待 Coordinator 将下列事实写入 SQLite：

- `ExpectedVideoBytes`、`ExpectedAudioBytes`；
- 视频和音频完整性标志；
- 当前阶段进度；
- 任务临时目录、最终路径和输出路径保留。

`IMediaMergeRetryExecutor` 是独立的窄能力：只读取已有 `video.tmp`、`audio.tmp` 执行合并，并在成功后继续尚未执行的附加资源步骤，不请求 DASH，也不下载主媒体。

`RetryMergeAsync(taskId)` 只接受未处于活动执行中、错误类型为 `ffmpeg` 或 `merge` 的失败任务。开始前必须同时满足：

- 两个临时文件存在，长度与可信检查点完全一致，且预期长度大于零；
- 视频和音频完整性标志均为真；
- 最终输出路径仍由该任务持有；
- 重新检查后的磁盘空间足够。

任一条件失败都保持任务失败，并提示完整重试或重新开始。合并失败保留输入临时文件和已有成品；普通网络/CDN 重试保留合法断点及字节事实，只有“重新开始”才清零并删除临时下载事实。

## 8. 错误行动矩阵

`IDownloadFailurePresentationPolicy` 将持久化错误类型映射为用户摘要和结构化行动；`IDownloadFailureActionService` 只路由有限动作，状态变更仍统一交给 Coordinator。

| 错误类型 | 用户摘要 | 主行动 | 次行动 |
| --- | --- | --- | --- |
| `auth` | 登录状态已失效 | 重新登录并继续 | 查看日志 |
| `ffmpeg` | ffmpeg 缺失或损坏 | 安装/修复并继续合并 | 选择自定义路径 |
| `directory` | 输出目录不可写 | 更换目录并继续 | 查看日志 |
| `disk` | 磁盘空间不足 | 重新检查并继续 | 更换目录 |
| `network` | 网络连接失败 | 重试 | 查看日志 |
| `cdn` | 下载节点响应异常 | 更换节点重试 | 查看日志 |
| `resource` | 媒体资源已失效或不可用 | 重新解析并重试 | 查看日志 |
| `merge` | 音视频合并失败，临时媒体已保留 | 仅重试合并 | 查看日志 |
| `conflict` | 输出位置发生新冲突 | 更换输出位置 | 查看日志 |
| `unknown` | 任务发生未识别错误 | 查看日志 | 完整重试 |

分类优先使用 `FfmpegUnavailableException`、`MediaMergeException`、`OutputDirectoryException`、`ResourceUnavailableException` 等明确异常。只为旧记录保留最小的 ffmpeg 文本兼容，不再让新代码依赖中文异常消息判断。技术异常经敏感数据脱敏后写日志；任务卡片只展示策略生成的简短消息。

登录弹窗通过 `ILoginDialogService` 在工作台和任务中心复用。目录迁移通过 Coordinator 校验并调用仓储事务，原子完成释放旧路径保留、申请新路径、更新目录和任务状态；同名文件统一自动编号，绝不继承旧路径的覆盖确认。

## 9. 兼容策略

- 不新增 SQLite 列，复用已有错误类型、预期长度、完整性标志、临时目录和路径保留字段。
- 旧 `ffmpeg`、`disk` 和未知错误记录由展示策略继续映射；缺少可信媒体检查点的旧任务安全拒绝仅合并重试。
- 旧自定义路径设置键 `ffmpeg_custom_path` 保持有效；选择无效路径后仍可回退到托管、插件目录或 `PATH`。
- 兼容接口 `IFfmpegService` 暂时保留，便于已有扩展逐步迁移到单一职责接口。

## 10. 自动化覆盖与验收结果

独立 G7 测试集覆盖：正确安装、固定摘要、版本探测、路径优先级、强制探测、哈希不匹配、截断或损坏 ZIP、路径穿越、重复条目、缺少 ffmpeg、进程失败、下载失败、取消、平台拒绝、并发互斥、旧指针回滚、临时内容清理、十类错误展示与行动、检查点持久化、仅合并重试、无效检查点拒绝和目录事务迁移。

2026-08-04 已验证：

```text
dotnet build Plugins/BiliDownloader/BiliDownloader.Tests/BiliDownloader.Tests.csproj -c Release -p:SkipPluginDeploy=true --no-restore
结果：0 错误，0 警告

dotnet test Plugins/BiliDownloader/BiliDownloader.Tests/BiliDownloader.Tests.csproj -c Release -p:SkipPluginDeploy=true --no-build --no-restore
结果：384/384 通过，0 失败，0 跳过

dotnet build MyAvaloniaManagement.sln -c Release -p:SkipPluginDeploy=true --no-restore
结果：0 错误，0 警告

dotnet test MyAvaloniaManagement.sln -c Release -p:SkipPluginDeploy=true --no-build --no-restore
结果：769/769 通过，0 失败，0 跳过

git diff --check
结果：通过，无空白错误（仅 Git 行尾格式提示）
```

G8 仍负责真实网络、真实 ffmpeg 发行包和完整桌面交互验收；G7 自动化测试不会访问下载站或启动开发机上的真实 ffmpeg。

## 11. 关键不变量

- 没有用户操作就没有 ffmpeg 网络下载。
- 没有通过固定摘要、安全解压和进程验证就不能激活安装包。
- 新指针生效失败必须恢复旧指针；旧可用版本不因修复失败而丢失。
- 合并失败不删除已验证输入；仅合并重试不访问 DASH 或重新下载主媒体。
- 普通重试不清零可信断点；只有“重新开始”清零。
- UI 行动不直接改写任务事实，Coordinator 和仓储事务仍是唯一状态写入边界。
- 用户消息简洁，技术细节进入脱敏日志，任何失败卡片都具有可执行的下一步。
