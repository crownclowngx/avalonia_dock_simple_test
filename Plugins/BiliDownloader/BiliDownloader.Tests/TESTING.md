# BiliDownloader 测试说明

## 测试分层

- A 级核心组：凭据、登录状态、SQLite、任务协调器、进度和敏感数据边界。
- B 级协议组：Bilibili API 响应契约、WBI、CDN、Range 下载和附加资源。
- C 级界面逻辑组：ViewModel、消息路由、Document 保存恢复、转换器和创建策略。
- G6 冲突预检组：四种策略、目录与磁盘检查、续传事实、路径保留、预检过期和 Document 兼容迁移。
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

## 稳定性约束

- 涉及 Flurl、WBI 缓存和 PATH 的测试串行运行并恢复静态状态；ffmpeg 路径已经是实例状态。
- 并发测试使用 `TaskCompletionSource` 和显式超时，不依赖任务碰巧完成的顺序。
- SQLite、下载文件和密钥全部位于按测试创建的临时目录，测试结束后清理。
- G6 测试只在独享临时目录创建零字节冲突文件；媒体大小和磁盘容量使用确定性接口替身，不依赖开发机剩余空间。
- `coverage.runsettings` 排除测试程序集、生成代码、XAML 和纯 View；门禁定义见 `coverage-baseline.json`。

## 当前测试边界

- 下载—完整性验证—ffmpeg 合并主链路通过注入 HTTP 与 ffmpeg 假实现离线覆盖。
- `FfmpegService` 的参数、退出码、取消和清理通过进程句柄假实现验证，默认测试不执行真实 ffmpeg。
- `CoverExtrasHandler` 通过注入的 `HttpMessageHandler` 覆盖 HTTPS 规范化、请求头和成功写入。
- `LoginWindowViewModel` 内置两秒轮询与 Avalonia Bitmap，不做窗口/计时自动化；登录 API 与登录状态服务已分别离线覆盖。

剩余 UI/真实环境边界不得通过访问真实网络或引入真实凭据来绕过。
