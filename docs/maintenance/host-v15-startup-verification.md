# V15 启动引导：专用开发验证

> 当前状态（2026-09-21）：Host 整体人工验收由项目所有者手工使用后确认通过，见[人工验收确认](../archive/records/host/manual-acceptance-20260921.md)。本文保留专项命令和后续回归矩阵；原阶段自动化结果以关联记录/JSON 为准，当前交付见[本机部署](local-deployment.md)。

> 用途：定义本地自动化、渲染检查和独立原生桌面验收范围。日期：2026-09-20。
> 实现已接入，实际执行结果以[开发记录和 JSON 证据](../archive/records/host-v15/development-acceptance.md)为准。
> 本轮不使用 AIFLOW、Windows CI、seal 或发布 Smoke、覆盖率、重复性门禁。不部署安装目录、不发布包。

## 1. 开发验证入口

在仓库根目录按顺序运行，`-m:1` 避免共享项目中间目录被不同构建属性并发改写：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release -m:1 --filter 'FullyQualifiedName~StartupCoordinatorTests'
dotnet test Host/MyAvaloniaManagement.PluginTests -c Release -m:1 --filter 'FullyQualifiedName~StartupPluginProgressTests|FullyQualifiedName~HostLifecycleOwnershipTests|FullyQualifiedName~PluginLifecycleCoordinatorTests|FullyQualifiedName~HostRestartProcessTests'
dotnet test Host/MyAvaloniaManagement.UiTests -c Release -m:1 --filter 'FullyQualifiedName~StartupSplashUiTests'
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

`verify` 包括固定补丁、locked restore、Release 零警告构建、SDK/Host 单元/插件集成/Headless UI/MyPlugTest、契约及已发布 API 比较、插件打包和真实 ZIP 验收。它是既有本地开发门禁，不授予发布资格；Gate 实现及发布阈值未因 V15 调整。

专项 TRX 保存在 `artifacts/v15/tests`；完整验证保存在 `artifacts/gate/<run-id>`。阶段用例变化后以最后完整验证的计数为准，不能叠加多次专项数量制造覆盖规模。

## 2. 测试映射

| 范围 | 实际测试 | 行为证据 |
| --- | --- | --- |
| U：启动协调与展示规则 | `StartupCoordinatorTests` | 单次任务、准备与就绪分离、首帧前取消、工厂成功与取消竞争、错误终态、工作线程同步阻塞隔离、Windows STA、进度有界及终态封闭、阶段计数与空总数 |
| P：真实插件进度 | `StartupPluginProgressTests` | DLL 实际加载/失败、禁用项不加载、缓存读取不重放、扫描取消、Provider 注册失败隔离、初始化结果计数、超时迟到不回写、空生命周期 |
| L：生命周期与回滚 | `HostLifecycleOwnershipTests`、`PluginLifecycleCoordinatorTests` | 原有释放顺序、启动回滚、幂等关闭、取消宽限、未排空任务保留、失败清理；异步 Runtime 复用同一内核 |
| UI：真实 XAML | `StartupSplashUiTests` | 浅深主题、长 PluginId、绑定值、关闭意图、动画停止与静态模式；保存软件渲染截图 |
| X：真实 OS 子进程 | `HostRestartProcessTests` 的 V15 场景 | 首屏取消、加载中取消、致命重复清单、空插件、全部禁用、DLL 缺失提示、同步慢生命周期期间 UI 仍能取得下一帧 |
| R：重启回归 | `HostRestartProcessTests` 原有场景 | apphost/dotnet 启动、两轮启禁往返、普通退出、取消、强杀、异常退出及延迟退出；新增首帧/就绪时间和 Splash 已关闭断言 |
| G：门禁本身 | `MyAvaloniaManagement.Gate.Tests` | 原工具自测保持通过，未新增发布条件或放宽既有门禁 |

`StartupProbe` 仅属于测试资产：真实 DLL 在生命周期返回 Task 之前同步等待 UI 的放行文件。Harness 必须在这段等待期间取得下一帧，然后放行或请求取消。固定期限仅用于避免测试永久挂起，成功由明确事件和实际窗口状态证明，不通过 sleep 推断启动完成。

子进程隔离目录由测试创建，包含复制的 Harness、MyPlugTest 和按需加入的 StartupProbe；只修改该副本以注入缺 DLL、重复身份及禁用配置。该流程不读取用户安装目录，不启动外部下载、账号或数据库任务。正常助手仍不创建 Splash。

## 3. 截图与证据

UI 测试在 `Host/MyAvaloniaManagement.UiTests/bin/Release/net10.0/TestResults/v15-ui` 生成 `splash-light.png` 和 `splash-dark.png`。软件渲染用于检查羽毛轮廓、布局、文字省略、警告与主题；它不能证明实际桌面动画帧率。

进程收据继续由重启夹具统一存入 `Host/MyAvaloniaManagement.PluginTests/bin/Release/net10.0/TestResults/v14-restart/<id>`，V15 使用同一测试所有权目录而不再复制一套进程管理器。新增 `startup-<stage>.json` 保存首帧及就绪时间；慢插件场景保存 `startup-entered.json`、`startup-responsive.json`，取消/失败分支保存 `startup.json`。完整来源和最终摘要写入 V15 JSON。

每次最终记录至少包含源码 HEAD 和未提交差异、实际命令、TRX 通过/失败/跳过数、Gate run-id/summary 摘要、截图位置、未执行范围。源码身份不能仅使用 HEAD，因为实施过程中存在未提交文件。

## 4. 独立原生桌面矩阵

| 编号 | 检查 | 完成证据 |
| --- | --- | --- |
| M01 | 实际打开 exe，首屏与主窗口交接 | 视频或有时间记录的观察，确认无空白长停、焦点跳失和误退出 |
| M02 | 插件加载时羽毛动画、关闭及 Alt+F4 | 实际窗口响应；不把 Headless 下一帧当作原生流畅性证明 |
| M03 | 浅深主题及 125%/150%/200% DPI、多屏 | 羽毛清晰、长标识可读、窗口与关闭入口没有越界 |
| M04 | 系统减少动画及环境开关 | 静态羽毛、文本和进度仍可用 |
| M05 | 实际工作台菜单/插件看板重启往返 | 助手无多余首屏，新进程正确展示与交接 |
| M06 | 经核查安全的外部插件原生/STA、账号、存储等初始化 | 由各插件独立确认真实依赖与副作用，记录耗时和线程约束 |
| M07 | 与源码匹配的既有单文件样本 | 羽毛无需旁置文件，区分解包/平台启动与插件耗时；不为验证触发发布 |

未执行项如实标记，不以自动化成功、源码支持或交叉构建代替。安装目录部署及正式发布另行记录。

## V21 测试基础设施更新

现行夹具、等待和证据目录约定见[V21 专用开发验证](host-v21-gate-and-test-efficiency-verification.md)。真实进程仍按用例独立复制和持有 PID；UI 动作等待实际就绪/完成，不以固定休眠判定成功。Gate 附件归入本轮套件目录，旧记录中的固定 TestResults 路径仅代表当时产物；没有改变本页保护的启动、关闭、取消、布局或所有权协议。
