# MyAvaloniaManagement 统一 Gate

> 当前唯一受支持的项目验证与封板入口是 `tools/MyAvaloniaManagement.Gate`。历史 G 阶段文档中出现的
> `scripts/*.ps1` 命令已经退役，仅用于说明当时如何取得已有证据。

## 日常验证

在主仓根目录运行：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

`verify` 只使用本仓，允许未提交修改。执行顺序为 locked restore → Release 零警告构建 →
SDK、Host Unit、Host Plugin、Host Headless UI、MyPlugTest Unit → 契约检查 → MyPlugTest 打包 → 真实包验收。
它不采集覆盖率、不启动真实窗口、不授予发布资格。

以下两个别名均执行完整本仓验证：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify --scope host
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify --scope all
```

`workflow/workbench` scope、`--workflow-studio` 和 `--classic-game` 已退役，使用时明确报错。
Host 的插件加载、隔离、生命周期以及 Workflow/Workbench 内核最小夹具仍保留；外部业务插件专项与
跨插件业务组合验收已从主仓测试入口删除。

MyPlugTest 真实 ZIP 测试标记 `Category=PackageAcceptance`，常规阶段使用
`Category!=PackageAcceptance`。Gate 在打包后解压并设置 `MYAVALONIA_MY_PLUG_TEST_PACKAGE_ROOT`，
单独执行包验收，必须恰好通过一项测试，验证四个 Document、一个 Tool 与 Workspace 目录。
缺少包输入、加载失败或零测试执行均判失败，不再通过直接返回形成成功结果。

单独运行常规 Plugin 测试时也必须使用分类过滤；完整包闭环使用 Gate：

```powershell
dotnet test Host/MyAvaloniaManagement.PluginTests -m:1 --filter 'Category!=PackageAcceptance'
```

## V5 生命周期所有权专项

`HostLifecycleOwnershipTests` 使用真实 Microsoft DI 容器中的释放探针、可控任务和
`ManualLifecycleTimeProvider` 检查资源使用与释放的先后关系。测试范围包括超时后迟到成功、
取消通知阻塞、保留政策、启动失败回滚、间接创建的 Workflow 管理器、重复关闭与诊断出口关闭。
`PluginLifecycleCoordinatorTests` 保留既有顺序、失败隔离和 UI 同步上下文回归，并补充迟到初始化关闭。

单独排错入口：

```powershell
dotnet test Host/MyAvaloniaManagement.PluginTests -m:1 --filter "FullyQualifiedName~HostLifecycleOwnershipTests|FullyQualifiedName~PluginLifecycle"
```

`-m:1` 避免专项工程引用的多种全局属性并发写入同一 SDK 中间产物；正式完整验证仍使用上述 Gate。
专项成功不等于完整门禁成功，实际证据见
[V5 专属实施验收记录](../plan-history/host-v5/lifecycle-ownership-repair-acceptance.md)。

V5 历史验收曾额外采集 Host Unit/Plugin/UI 与 DaTang Host/Host UI 覆盖来源，
按 Host-only 程序集范围合并，并核对 `gate.config.json` 的既有 Host 阈值；详细口径和结果见专属记录。

## V6 插件导航专项

V6 的专项开发验证和结果见 [插件目录与功能中心验收记录](../plan-history/host-v6/plugin-navigation-and-function-center-acceptance.md)。
专项覆盖路径/图标/偏好、三处创建入口、真实 XAML、重复输入、窗口会话释放和退出文档排空。
可通过以下过滤定位本轮新增回归，完整结果仍以 Gate 为准：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -m:1 --filter "FullyQualifiedName~PluginNavigation|FullyQualifiedName~DocumentOperationShutdown"
dotnet test Host/MyAvaloniaManagement.UiTests -m:1 --filter FullyQualifiedName~PluginNavigationUiTests
```

V6 不运行下面的发布命令；独立采集现有 Host-only 覆盖率，不通过 seal 间接启动 Windows Smoke。

## 正式封板

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- seal
```

`seal` 只支持 Windows x64，固定 `global.json` 中的 SDK 和 Release 配置，并拒绝主仓脏工作树。
默认创建一份无硬链接主仓克隆，执行本仓完整测试与契约检查、MyPlugTest 双次构建打包及哈希比较、
manifest/身份/共享程序集检查、真实包验收、Host 覆盖率门禁和真实窗口 `layout-v2.json` Smoke。
覆盖率阶段在包验收之后，合并 Host Unit、Plugin、UI、包验收四份报告，只统计
`MyAvaloniaManagement` 主程序集，SDK、插件和测试程序集不进入分母。缺少报告或混入其他程序集均失败。
现有最低行覆盖率 84.39%、分支覆盖率 70.58% 保持不变；API Unshipped 必须为零才能继续正式封板。

只有里程碑需要重新证明隔离重复性时才运行：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- seal --repeat
```

第二轮从与第一轮完全相同的源码快照创建新工作区，并比较覆盖率、包哈希、manifest 哈希和文件数量等稳定事实。

## 证据与失败处理

每次运行写入 `artifacts/gate/<run-id>/`：

- `summary.json`：schema v2 总摘要、本仓源码指纹、Host 发布资格与重复性结论（不含跨仓 integration 字段）；
- `pass-*/stages/*.json`：阶段状态与耗时；
- `pass-*/tests`、`coverage`、`packages`：TRX、Cobertura 和实际 ZIP；
- 各命令日志、MyPlugTest 解压目录以及 Windows Smoke 隔离数据。

成功后 Gate 只删除带 `.myavalonia-gate-owned` 标记的 `%TEMP%/MAVG-*` 目录；失败工作区会保留并在控制台给出
位置。Gate 不包含上传、签名、标签或外部发布命令。

## 维护原则

- 测试行为放入对应 xUnit 项目，Gate 不复制业务断言；
- 五组测试、覆盖率阈值和唯一插件项目集中在 schema v2 的 `gate.config.json`；
- API `Unshipped` 可以在 `verify` 中作为候选存在，但正式 `seal` 必须为零；
- 新门禁能力应增加 Gate stage、配置或测试，不再创建新的 G 阶段 PowerShell 脚本。

## Gate 工具自测

Gate 自身测试独立执行，避免在运行 Gate 时重建它自己的进程文件：

```powershell
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1
```

自测覆盖参数退役、配置范围、阶段顺序与失败短路、包身份、零测试拒绝、覆盖率口径、文档链接及报告格式。
文档门禁严格检查本仓链接；解析到仓库外的相对链接作为外部引用，不要求邻接仓库存在。

## V6.1 图标专项

公共资源与注册字段测试放在现有 SDK 测试套件；Host Unit 验证归属、Seal、冲突与可用性，Plugin 测试验证候选失败原子性、两个私有资源版本以及已发布 SDK 3.3.0 的真实旧二进制兼容。UI 套件以无窗口 Skia 像素验证画布、裁切、填充、独立画刷、坏路径与缓存撤回，保留 V6 全部交互回归。真实 MyPlugTest ZIP 必须含私有 Icons DLL 且不含共享 SDK DLL。

执行本仓 verify；覆盖率另采集 Unit、Plugin、UI、包验收四份 Host-only 报告并合并，不运行 Windows Smoke 或 seal。实际数字见 [专属实施记录](../plan-history/host-v6.1/icon-contributions-and-resources-acceptance.md)。

## V8 工作区交互开发验证

V8 沿用完整 `verify`，另以当前 runsettings 单独采集 Unit、Plugin、Headless UI 与真实包验收四份覆盖率。没有修改 Gate 图或阈值，没有启用 Windows CI／发布 seal。实际 810 项结果、四份报告、复查命令和人工待验收边界见 [V8 实施记录](../plan-history/host-v8/cognitive-ux-convergence-acceptance.md)。
