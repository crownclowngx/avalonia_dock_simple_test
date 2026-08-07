# BiliDownloader 测试说明

## 测试分层

- A 级核心组：凭据、登录状态、SQLite、任务协调器、进度和敏感数据边界。
- B 级协议组：Bilibili API 响应契约、WBI、CDN、Range 下载和附加资源。
- P1-G0 内容源协议组：稳定键、Provider 注册、分页游标保护、DirectLink 适配、取消与敏感数据边界。
- P1-G1～G3 来源产品化组：个人/订阅来源、跨页筛选、规则选择、缓存淘汰与虚拟化状态。
- P1-G4 Document V3 组：V1/V2/V3 迁移、离线恢复、缺失 Provider、可复用预设、强制另存和敏感字段快照。
- C 级界面逻辑组：ViewModel、消息路由、Document 保存恢复、转换器和创建策略。
- G6 冲突预检组：四种策略、目录与磁盘检查、续传事实、路径保留、预检过期和 Document 兼容迁移。
- G7 依赖恢复组：固定 ffmpeg 供应链、安全安装与回滚、运行时探测、媒体检查点、仅合并重试、十类错误行动和目录事务。
- XAML 像素、真实窗口、真实扫码及真实 Bilibili 网络不属于默认自动化测试。

测试只允许两种 HTTP 目标：

1. Flurl `HttpTest` 拦截的固定 Bilibili URL，用于验证请求与响应契约；
2. `127.0.0.1` 随机端口的 `LoopbackHttpServer`，用于验证直接 `HttpClient`、HEAD、Range 和文件写入。

禁止在测试中使用真实账号、Cookie 或线上媒体资源。

## 运行

快速运行：

```powershell
dotnet test .\BiliDownloader.Tests.csproj -c Release -p:SkipPluginDeploy=true
```

完整门禁（测试、0 跳过、Cobertura、风险分层覆盖率）：

```powershell
.\Run-Tests.ps1 -NoRestore
```

需要保留 TRX 和 Cobertura 报告时：

```powershell
.\Run-Tests.ps1 -NoRestore -KeepResults
```

整个解决方案的兼容性回归：

```powershell
dotnet test ..\..\..\MyAvaloniaManagement.sln -c Release -p:SkipPluginDeploy=true
```

G7 完成时的证据（2026-08-04）：解决方案 Release 构建 0 错误、0 警告；插件 384/384、全解决方案 769/769 测试通过，0 跳过；`git diff --check` 通过。

P1-G4 自动化基线（2026-08-07）：BiliDownloader Release 完整门禁 594/594 通过、0 跳过；其中 Document V3 专项 58/58。覆盖率为总体行 84.32% / 分支 67.53%，A 组 88.66% / 76.89%，B 组 83.05% / 65.71%，C 组 78.18% / 60.11%，全部超过现行门禁。

## 稳定性约束

- 涉及 Flurl、WBI 缓存和 PATH 的测试串行运行并恢复静态状态；ffmpeg 路径已经是实例状态。
- 并发测试使用 `TaskCompletionSource` 和显式超时，不依赖任务碰巧完成的顺序。
- SQLite、下载文件和密钥全部位于按测试创建的临时目录，测试结束后清理。
- G6 测试只在独享临时目录创建零字节冲突文件；媒体大小和磁盘容量使用确定性接口替身，不依赖开发机剩余空间。
- G7 安装测试使用内存下载器和测试 ZIP，不访问 Gyan 或其他公网地址；平台、进程探测、取消和并发顺序均使用确定性替身。
- `coverage.runsettings` 排除测试程序集、生成代码、XAML、纯 View、真实 Avalonia 模态/文件选择器、系统文件定位适配器，以及含 Bitmap 与两秒轮询的登录窗口 ViewModel；这些交互边界分别由接口替身、API/状态服务测试和 Headless UI 测试覆盖。门禁定义见 `coverage-baseline.json`。
- `Services/ContentSources` 计入 B 级协议覆盖率；DirectLinkProvider、Provider Registry 与分页累加器执行关键文件最低覆盖率门禁。
- Document 恢复测试必须使用计数 Provider 证明打开和初始化调用次数为零；测试不得用缓存命中伪装“零网络”。

## 当前测试边界

- 下载—完整性验证—ffmpeg 合并主链路通过注入 HTTP 与 ffmpeg 假实现离线覆盖。
- `FfmpegService` 的参数、退出码、取消和清理通过进程句柄假实现验证，默认测试不执行真实 ffmpeg。
- `FfmpegPackageInstaller` 覆盖固定摘要、大小上限、ZIP 越界与重复条目、缺失可执行文件、并发互斥、旧指针回滚和临时目录清理；真实发行包下载属于 G8 人工/集成验收。
- 合并恢复测试验证检查点先于 ffmpeg 落库、无效临时媒体被拒绝，以及仅合并执行器不会调用完整下载入口。
- `CoverExtrasHandler` 通过注入的 `HttpMessageHandler` 覆盖 HTTPS 规范化、请求头和成功写入。
- `LoginWindowViewModel` 内置两秒轮询与 Avalonia Bitmap，不做窗口/计时自动化；登录 API 与登录状态服务已分别离线覆盖。

剩余 UI/真实环境边界不得通过访问真实网络或引入真实凭据来绕过。
