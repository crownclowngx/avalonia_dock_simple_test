# 插件日志抽象加载回归

> 用途：排查并验证真实 Host 缺少日志抽象导致的类型预检失败。核对日期：2026-10-04。事实源：Host 项目包引用、RuntimeProfile、`PluginLoggingDependencyTests`；本次交付见[修复记录](../archive/records/host/logging-dependency-fix-20261004.md)。

## 现象与依赖所有权

网络插件的 Titanium 适配器引用 `Microsoft.Extensions.Logging.Abstractions, Version=10.0.0.0`。Host 预检插件引用程序集时，如果实际交付未带此依赖，会产生 `FileNotFoundException`，看板显示 `PLUGIN_ASSEMBLY_LOAD_FAILED`、加载失败和已隔离。

当前 Build 协议禁止插件私带日志抽象。Host 应直接引用 `Microsoft.Extensions.Logging.Abstractions`，同时把它登记为 RuntimeProfile 的精确共享根。继续使用既有默认加载上下文和版本、公钥身份校验；不扩大为整个 `Microsoft.Extensions.*` 家族承诺，也不修改插件打包限制。

此修复只补齐 Host 的依赖所有权和共享声明，不新增加载策略类、服务定位器或业务层适配。保持 SOLID 的单一职责与依赖方向：业务依赖日志抽象，Host 负责实际共享程序集的提供，加载器负责验证身份。

## 本地回归

在 Host 仓库执行：

```powershell
dotnet test Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj `
  -c Debug -p:SkipPluginDeploy=true -warnaserror `
  --filter 'FullyQualifiedName~PluginLoggingDependencyTests|FullyQualifiedName~PluginSdkDependencyBoundaryTests|FullyQualifiedName~PluginDependencyIsolationTests' `
  --logger 'trx;LogFileName=host-logging-fix.trx' --results-directory TestResults/HostLoggingFix
```

必须同时保护：Host 直接包引用、精确共享根、默认上下文实例一致、Titanium 所需接口存在、高版本请求拒绝、错误签名拒绝，以及原有私有依赖隔离。测试进程可能带入附加框架程序集，因此单元测试通过不能替代实际 EXE 验证。

部署验证沿用[本机部署流程](local-deployment.md)，本轮只做本地 Debug 检查。使用安装版同一源码基线加最小补丁，保留固定 Avalonia/Dock 补丁；自包含单文件要重新构建 Host EXE，在旁边放一个 DLL 并不能证明 bundle 提供了正确依赖。

1. 用空的 `MYAVALONIA_DATA_DIRECTORY`、独立的 `DOTNET_BUNDLE_EXTRACT_BASE_DIR` 和 `MYAVALONIA_SMOKE_TEST=1` 启动实际 EXE，保存本轮标准输出、错误输出及诊断。只为子进程设置环境变量，不改系统环境。
2. 检查启动完成和正常关闭，同时检查插件诊断。**退出码为 0 仍可能存在被隔离插件**，不能单独作为通过条件。需要定位异常原文时，可在隔离子进程临时开启敏感诊断，不提交原始敏感日志。
3. 正常启动实际安装版，在看板确认目标插件可用且生命周期初始化成功；“检查安装产物”应核对到当前会话已加载程序集。
4. 从功能中心创建 HTTP 工作台，确认 Document 与生产 ViewModel 能正常创建。实际代理转发、TLS、客户端和长时间负载另按业务矩阵验证。

“尚无验收报告”只表示未导入匹配当前产物的离线报告，与当前会话是否加载成功分开判断。本轮不生成正式发布报告，不运行 Windows CI 或发布门禁。
