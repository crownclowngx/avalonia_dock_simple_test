# .NET 10 升级阶段 0 基线摘要

> 本摘要只保留可提交的脱敏结论。完整命令输出位于
> `artifacts/upgrade/net10/phase-0-baseline/`，该目录不纳入 Git。

## 执行信息

| 项目 | 结果 |
| --- | --- |
| 执行日期 | 2026-07-26 |
| 源提交 | `25bc8d1494a54b02e4cf22fcbd66f995a233f275` |
| 工作区 | 干净 |
| 平台 | Windows x64 |
| SDK | 9.0.315 |
| 普通项目 TFM | `net9.0` |
| 真实窗口 Harness TFM | `net9.0-windows` |

## 项目和直接依赖基线

- 解决方案包含 12 个项目：宿主、公共 UI、宿主插件测试、三个普通插件、
  BiliDownloader 测试，以及 MySmallTools 的主项目、测试、性能工具、发布验收和真实窗口 Harness。
- Avalonia 核心包为 11.3.4，TreeDataGrid 为 11.1.1。
- Dock 系列统一为 11.3.2.2；Semi 为 11.2.1.9；Ursa 为 1.12.0。
- Microsoft.Extensions 和 Microsoft.Data.Sqlite 为 9.0.0。
- LibVLCSharp/LibVLCSharp.Avalonia 为 3.9.4，私有 Windows LibVLC 为 3.0.21。
- 项目文件尚未使用中央包版本和锁文件。

## 构建和测试

| 门禁 | 结果 |
| --- | --- |
| 解决方案 Release 普通构建 | 通过，0 警告、0 错误 |
| MySmallTools Release `-warnaserror` | 通过，0 警告、0 错误 |
| MySmallTools.Tests | 180/180 通过 |
| MyAvaloniaManagement.PluginTests | 21/21 通过 |
| BiliDownloader.Tests | 21/21 通过 |

单独强制重建 DaTangAccountingHelpPlug 可稳定复现 21 个历史警告：

- `CS8618`：11 个非空属性未初始化。
- `CS1998`：3 个没有 `await` 的异步方法。
- `CS4014`：1 个未等待的 UI Dispatcher 调用。
- `CS8600`、`CS8603`：各 1 个可空契约不准确。
- `CS8602`：2 个可能的空引用解引用。
- `MVVMTK0034`：2 个直接访问生成器管理字段的警告。

全解决方案并行执行 `Rebuild` 时还观察到公共项目输出被多个依赖项目同时清理、
以及性能工具输出文件短暂占用的问题。普通 Release 构建和逐项目强制重建均成功；
该现象作为历史构建编排问题记录，不作为放宽最终严格构建门禁的理由。

## 依赖安全基线

当前存在以下高危传递依赖：

- `Tmds.DBus.Protocol 0.21.2`，由 Avalonia 11.3.4 依赖链带入。
- `SQLitePCLRaw.lib.e_sqlite3 2.1.10`，由 Microsoft.Data.Sqlite 9.0.0 依赖链带入。
- `System.Security.Cryptography.Xml 9.0.3`，由 EPPlus 8.1.1 依赖链带入。

阶段 2 的退出条件是高危和严重依赖公告清零，不允许通过忽略公告完成验收。

## G10/G11 证据状态

- 现有 G10 参考基线属于 .NET 9.0.17、LibVLCSharp 3.9.4、LibVLC 3.0.21，
  只能作为升级前历史比较数据，升级后不得直接覆盖或冒充 .NET 10 正式基线。
- 未找到 `g11-final-acceptance.json`，因此当前没有可复用的正式 G11 人工签字证据。
- 阶段 0 只冻结事实，不生成新的 G10/G11 正式签字。

## 阶段结论

阶段 0 基线可重现，222 项现有自动化测试全部通过。阶段 1 可以开始建立中央构建和
依赖治理，但必须保持本摘要中的 TFM、直接依赖版本和测试行为不变。
