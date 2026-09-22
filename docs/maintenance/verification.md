# 主仓验证与封板

> 用途：维护 Host、SDK 和 MyPlugTest。状态：当前；核对日期：2026-09-21。事实源：[Gate 参数](../../tools/MyAvaloniaManagement.Gate/GateOptions.cs)、[配置](../../tools/MyAvaloniaManagement.Gate/gate.config.json)与执行实现。

## 日常开发

在主仓根目录运行：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

`verify` 允许未提交修改，只使用本仓源码。它先经过 `avalonia-layout-patch` 和 `dock-patch`，准备[布局运行时补丁](../../patches/avalonia-cross-window-layout/README.md)及[固定 Dock 补丁](../../patches/dock-area-fill/README.md)，再执行 locked restore、Release 零警告构建和输出 DLL 摘要检查、契约及已发布 API 比较、SDK/Host Unit/Host Plugin/Host Headless UI/MyPlugTest Unit、MyPlugTest 打包和真实 ZIP 验收。不采集覆盖率，不启动 Windows Smoke，不授予发布资格。首次补丁构建需要 Git、PowerShell 7、SDK 10.0.302 及上游下载；首次 API 比较需要下载已记录摘要的三个公共基线包，后续使用校验后的缓存。跨窗崩溃的回归矩阵见[专项指南](dock-cross-window-layout-verification.md)。

`--scope host` 和 `--scope all` 都执行完整本仓验证。`workflow`、`workbench` scope 以及旧外部仓库参数均已退役；外部插件业务验证由各仓库独立负责。

## 包验收与专项排错

MyPlugTest ZIP 验收使用 `Category=PackageAcceptance`。Gate 打包解压后设置 `MYAVALONIA_MY_PLUG_TEST_PACKAGE_ROOT`，必须恰好通过一项包验收；缺输入、加载失败或零测试均失败。单独运行常规 Plugin 测试时排除该项：

```powershell
dotnet test Host/MyAvaloniaManagement.PluginTests -c Release -m:1 --filter 'Category!=PackageAcceptance'
```

按实际测试类定位问题，不恢复已退役的 G 阶段脚本。例如：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release -m:1 --filter FullyQualifiedName~HelpContentTests
dotnet test Host/MyAvaloniaManagement.PluginTests -c Release -m:1 --filter 'FullyQualifiedName~HostLifecycleOwnershipTests|FullyQualifiedName~PluginLifecycle'
```

`-m:1` 避免专项工程引用的不同全局属性并发写入同一中间目录。专项通过只代表选定范围；具体测试数量和覆盖率应记入本次验收记录。

外部旧插件产物需要单独提供完整 Controls 副本，默认 verify 不编译这些外部输入夹具；统一入口、报告交付、候选消费和覆盖边界见[插件兼容开发验证](plugin-compatibility-verification.md)。V9 原命令与历史范围保留在 [V9 验证说明](../quick-start/host-v9-upgrade.md)。

## 专项回归索引

Host 整体人工验收已由项目所有者[确认通过](../archive/records/host/manual-acceptance-20260921.md)。以下文档保留可复用命令与检查矩阵；其中阶段自动化计数指原开发记录。当前部署见[本机部署](local-deployment.md)。

| 主题 | 专项验证 |
| --- | --- |
| 浮窗、工具关闭与布局 V3 | [浮窗与布局](floating-layout-verification.md) |
| 区域回停及跨窗口布局补丁 | [区域回停](dock-area-fill-verification.md)、[跨窗布局](dock-cross-window-layout-verification.md) |
| Registry、UI 调度与工作区查询 | [V12 内部职责](host-v12-refactor-verification.md) |
| 插件兼容与下次启动开关 | [产物兼容](plugin-compatibility-verification.md)、[V13 插件开关](host-v13-plugin-enablement-verification.md) |
| 进程重启与关闭交接 | [V14 自动重启](host-v14-automatic-restart-verification.md) |
| 首屏及真实插件进度 | [V15 启动窗口](host-v15-startup-verification.md) |
| Document 浮窗关闭及命令面板创建目标 | [V16 文档窗口](host-v16-document-window-verification.md) |
| 诊断、命令展示和服务注册 | [V17 可读性](host-v17-readability-verification.md) |
| 命令面板分组、键盘会话与页面目标校验 | [V18 命令面板](host-v18-command-palette-verification.md) |

V18 完整开发验证已通过，证据对应当时定稿的代码和嵌入 Markdown，见[开发记录](../archive/records/host-v18/development-acceptance.md)。后续整体手工验收见[2026-09-21 所有者确认](../archive/records/host/manual-acceptance-20260921.md)；输入法、多屏等矩阵保留供以后回归，不从 Headless 结果推定具体设备上的逐项操作。

V19 已实施：[复杂度收敛专用验证](host-v19-complexity-verification.md)保留可复用矩阵，[方案](../archive/plans/host-v19-complexity-reduction-plan.md)已归档；实际测试映射及最终完整开发验证见[开发记录](../archive/records/host-v19/development-acceptance.md)。

V20 已实施：[归档方案](../archive/plans/host-v20-layout-retirement-plan.md)、[专用开发验证](host-v20-layout-retirement-verification.md)与[开发记录](../archive/records/host-v20/development-acceptance.md)覆盖有效布局测试迁至 V3、Writer Lease、Gate 产物检查自测、旧查询及文档收口。仅执行本机专项和完整 `verify`，不使用 AIFLOW、Windows CI 或发布门禁。

已实施的 [V21 开发门禁与测试效能收敛方案](../archive/plans/host-v21-gate-and-test-efficiency-plan.md)配有[专用开发验证计划](host-v21-gate-and-test-efficiency-verification.md)，覆盖夹具、UI 等待、测试去向和 Gate 证据。V21 将开发契约前置到构建和必需 DLL 身份核对之后、测试之前；兼容 scope 和发布政策保持。实际结果见[开发记录](../archive/records/host-v21/development-acceptance.md)，其开发验收不执行下述发布门禁。

待实施的 [V22 插件源与安装升级方案](../roadmap/host-v22-plugin-distribution-plan.md)配有[专用开发验证计划](../roadmap/host-v22-plugin-distribution-verification.md)，覆盖默认源持久化、多源决策、ZIP 校验、安装事务、跨进程占用、重启及回滚。本次仅编写文档；实施时运行本机专项与 verify，不使用 AIFLOW、Windows CI 或下述发布门禁。

## 正式 Host 封板

仅在具备发布验收条件时使用：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- seal
# 需要证明隔离重复性时：
dotnet run --project tools/MyAvaloniaManagement.Gate -- seal --repeat
```

当前布局只支持 V3。V20 已适配发布 Smoke 的共享产物检查并通过工具单测，真实发布进程链本轮未执行；正式发布时仍按本节执行。开发专项见[浮窗与布局指南](floating-layout-verification.md)，不因工具适配降低发布条件。

seal 只支持 Windows x64，要求干净工作树，固定 global.json 的 SDK 和 Release 配置。它创建无硬链接源码克隆，执行本仓验证、MyPlugTest 双次确定性打包、包身份和资产检查、真实包验收、Host-only 覆盖率及真实窗口 layout-v3.json Smoke。

覆盖率合并 Host Unit、Plugin、UI、包验收四份报告，只统计主程序集；最低行/分支阈值为配置中的 84.39% / 70.58%。缺报告或混入其他程序集失败。默认一轮，`--repeat` 才执行第二份隔离工作区并比较稳定证据。

**当前 Core/UI Unshipped 非零，尚不满足 seal 的 API 条件。** 不得为了执行 seal 清空基线或降阈值；详情见 [API 维护](../reference/plugin-sdk-api-compatibility.md)。seal 不包含上传、签名、Git 标签或安装目录部署；真实业务和人工体验也不能仅由 Smoke 替代。

## 证据与工具自测

输出位于 `artifacts/gate/<run-id>/`，包含 schema 2 summary、阶段日志、TRX 及包证据；覆盖率仅在发布 profile 生成。每个 pass 的 `tests/summary.json` 关联源码身份、完整命令、过滤器、退出码、TRX 摘要、计数、运行时间与最慢十项；尚未执行标为 `not-run`，不补零。`wallMilliseconds` 是命令及读取证据总耗时，`result.milliseconds` 来自 TRX 起止时间。

测试附属文件位于 `pass-N/tests/<suite>/attachments/<scenario>/`，由 `MYAVALONIA_TEST_EVIDENCE_DIRECTORY` 传递。独立运行时必需附件写入测试输出下唯一的 `TestResults/run-*`；可选截图仍支持原环境变量。截图导出与像素断言分开，保存 PNG 本身不是视觉验证。失败工作区保留供排查；成功后仅清理 Gate 自己标记的临时目录。对外保留结论前，保存源码身份、实际命令、失败/跳过数及必要摘要；产物清理后注明原路径已失效。

Gate 自测独立运行，避免重建正在执行的工具：

```powershell
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1
```

现有文档检查只覆盖根 README、docs/README 和 Host 文档 README 的本仓文件链接；仓库外相对链接不要求邻仓存在，锚点、正文事实和其他文档需要额外检查。文档调整还应核对嵌入帮助的原文与链接。V10 的文件、规则、报告、异步、看板测试已纳入原测试组，工具自测另行执行。
