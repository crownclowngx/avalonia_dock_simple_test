# MyAvaloniaManagement 统一 Gate

> 当前唯一受支持的项目验证与封板入口是 `tools/MyAvaloniaManagement.Gate`。历史 G 阶段文档中出现的
> `scripts/*.ps1` 命令已经退役，仅用于说明当时如何取得已有证据。

## 日常验证

在主仓根目录运行：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

`verify` 允许主仓、WorkflowStudio 和 ClassicGame 存在未提交修改。它对每个参与仓库只执行一次 locked
restore 和一次 Release 零警告构建，然后运行现行 SDK、Unit、Plugin、Headless UI、插件业务测试、单次真实
打包、跨仓组合测试及 1 轮 MySmallTools 资源 Harness。它不启动真实窗口，也不授予发布资格。

排错时可以限制范围：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify --scope host
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify --scope workflow
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify --scope workbench
```

`workbench` 自动包含 Host 基座。外部仓不在默认位置时使用 `--workflow-studio <path>` 或
`--classic-game <path>`。

## 正式封板

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- seal
```

`seal` 只支持 Windows x64，固定 `global.json` 中的 SDK 和 Release 配置，并拒绝主仓脏工作树。默认创建一份
无硬链接主仓克隆和外部仓内容快照，执行完整覆盖率、六插件双包确定性比较、manifest/共享程序集检查、真实
Workflow/Workbench 双包组合、20 轮资源 Harness 和真实窗口 `layout-v2.json` Smoke。

只有里程碑需要重新证明隔离重复性时才运行：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- seal --repeat
```

第二轮从与第一轮完全相同的源码快照创建新工作区，并比较覆盖率、包哈希、manifest 哈希和文件数量等稳定事实。

## 证据与失败处理

每次运行写入 `artifacts/gate/<run-id>/`：

- `summary.json`：schema v1 总摘要、源码指纹、Host 发布资格、外部快照和重复性结论；
- `pass-*/stages/*.json`：阶段状态与耗时；
- `pass-*/tests`、`coverage`、`packages`：TRX、Cobertura 和实际 ZIP；
- 各命令日志以及 Harness、Windows Smoke 的隔离数据。

成功后 Gate 只删除带 `.myavalonia-gate-owned` 标记的 `%TEMP%/MAVG-*` 目录；失败工作区会保留并在控制台给出
位置。Gate 不包含上传、签名、标签或外部发布命令。

## 维护原则

- 测试行为放入对应 xUnit 项目，Gate 不复制业务断言；
- 测试项目、覆盖率阈值、插件项目和外部仓约定集中在 `gate.config.json`；
- API `Unshipped` 可以在 `verify` 中作为候选存在，但正式 `seal` 必须为零；
- 新门禁能力应增加 Gate stage、配置或测试，不再创建新的 G 阶段 PowerShell 脚本。
