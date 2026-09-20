# 主仓验证与封板

V16 Document 浮窗关闭与新建位置目前只有[实施方案](../roadmap/host-v16-document-floating-close-and-creation-target-plan.md)和[专用开发验证计划](host-v16-document-window-verification.md)，尚未实现或执行测试。后续使用本地专项、Gate 自测和完整 verify；不使用 AIFLOW、Windows CI 或本页正式发布门禁。

V15 羽毛启动窗口与插件进度已接入，见[专用开发验证](host-v15-startup-verification.md)和[开发记录](../archive/records/host-v15/development-acceptance.md)。本轮仅使用本地专项、工具自测及完整 verify，不使用 AIFLOW、Windows CI 或发布门禁。

V14 一键自动重启已接入；实际测试矩阵、真实子进程边界和本地命令见[专用开发验证](host-v14-automatic-restart-verification.md)，最终结果见[开发记录与证据](../archive/records/host-v14/development-acceptance.md)。本轮使用本地专项和完整 verify，不使用 AIFLOW、Windows CI 或正式发布门禁。

V13 插件开关已实现，矩阵与命令见[专用开发验证](host-v13-plugin-enablement-verification.md)，实际结果见[开发记录](../archive/records/host-v13/development-acceptance.md)。本轮只使用本地专项和 verify，不使用 AIFLOW、Windows CI 或下文正式发布门禁；发布命令不属于 V13 执行范围。

> 用途：维护 Host、SDK 和 MyPlugTest。状态：当前；核对日期：2026-09-19。事实源：[Gate 参数](../../tools/MyAvaloniaManagement.Gate/GateOptions.cs)、[配置](../../tools/MyAvaloniaManagement.Gate/gate.config.json)与执行实现。

## 日常开发

V12 内部职责重构的阶段矩阵、专项命令和证据规范见 [V12 专用开发验证](host-v12-refactor-verification.md)。三阶段已接入，专项与最终结果见[开发记录](../archive/records/host-v12/development-acceptance.md)。本轮只使用本地开发验证，不使用 Windows CI 或本页的正式发布门禁。

在主仓根目录运行：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

`verify` 允许未提交修改，只使用本仓源码。它先经过 `avalonia-layout-patch` 和 `dock-patch`，准备[布局运行时补丁](../../patches/avalonia-cross-window-layout/README.md)及[固定 Dock 补丁](../../patches/dock-area-fill/README.md)，再执行 locked restore、Release 零警告构建和输出 DLL 摘要检查、SDK/Host Unit/Host Plugin/Host Headless UI/MyPlugTest Unit、契约及已发布 API 比较、MyPlugTest 打包和真实 ZIP 验收。不采集覆盖率，不启动 Windows Smoke，不授予发布资格。首次补丁构建需要 Git、PowerShell 7、SDK 10.0.302 及上游下载；首次 API 比较需要下载已记录摘要的三个公共基线包，后续使用校验后的缓存。跨窗崩溃的回归与桌面待办见[专项指南](dock-cross-window-layout-verification.md)。

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

## 正式 Host 封板

仅在具备发布验收条件时使用：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- seal
# 需要证明隔离重复性时：
dotnet run --project tools/MyAvaloniaManagement.Gate -- seal --repeat
```

V11 当前布局已升级为 V3。既有发布 Smoke 的 V2 文件断言尚待实际发布前适配和验证，本轮未运行或放宽它；开发验证见 [浮窗与布局专项指南](floating-layout-verification.md)。

seal 只支持 Windows x64，要求干净工作树，固定 global.json 的 SDK 和 Release 配置。它创建无硬链接源码克隆，执行本仓验证、MyPlugTest 双次确定性打包、包身份和资产检查、真实包验收、Host-only 覆盖率及真实窗口 layout-v2.json Smoke。

覆盖率合并 Host Unit、Plugin、UI、包验收四份报告，只统计主程序集；最低行/分支阈值为配置中的 84.39% / 70.58%。缺报告或混入其他程序集失败。默认一轮，`--repeat` 才执行第二份隔离工作区并比较稳定证据。

**当前 Core/UI Unshipped 非零，尚不满足 seal 的 API 条件。** 不得为了执行 seal 清空基线或降阈值；详情见 [API 维护](../reference/plugin-sdk-api-compatibility.md)。seal 不包含上传、签名、Git 标签或安装目录部署；真实业务和人工体验也不能仅由 Smoke 替代。

## 证据与工具自测

输出位于 `artifacts/gate/<run-id>/`，包含 summary、阶段日志、TRX、覆盖率及包证据。失败工作区保留供排查；成功后仅清理 Gate 自己标记的临时目录。对外保留结论前，保存源码身份、实际命令、失败/跳过数及必要摘要；产物清理后注明原路径已失效。

Gate 自测独立运行，避免重建正在执行的工具：

```powershell
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1
```

现有文档检查只覆盖根 README、docs/README 和 Host 文档 README 的本仓文件链接；仓库外相对链接不要求邻仓存在，锚点、正文事实和其他文档需要额外检查。文档调整还应核对嵌入帮助的原文与链接。V10 的文件、规则、报告、异步、看板测试已纳入原测试组，工具自测另行执行。
