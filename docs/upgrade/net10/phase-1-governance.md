# .NET 10 升级阶段 1 治理摘要

## 执行信息

| 项目 | 结果 |
| --- | --- |
| SDK | 10.0.302 |
| 普通项目 TFM | `net9.0` |
| 真实窗口 Harness TFM | `net9.0-windows` |
| 中央包管理 | 已启用 |
| 锁文件 | 12/12 项目已生成 |

## 治理结果

- `global.json` 固定 10.0.302 功能带，只允许同功能带稳定补丁。
- 根级构建属性统一 Nullable、确定性构建、CI 构建标志和锁文件生成。
- 38 个直接包版本已经从项目文件迁移到中央版本文件。
- 项目文件不再包含包版本，只保留引用及 `PrivateAssets`、`IncludeAssets`、
  `GeneratePathProperty` 等资产语义。
- 普通项目从根级配置继承 `net9.0`，真实窗口 Harness 保留显式
  `net9.0-windows`，因此本阶段没有改变运行目标。
- 中央化会改变 NuGet 锁文件记录的包规范来源，首次中央化后对锁文件执行了一次
  有意重新评估；随后 locked restore 成功。

## 一致性验证

- 中央版本文件中的 38 个直接依赖名称和版本与阶段 0 项目文件完全一致。
- Release 构建通过，0 警告、0 错误。
- MySmallTools.Tests：180/180 通过。
- MyAvaloniaManagement.PluginTests：21/21 通过。
- BiliDownloader.Tests：21/21 通过。
- 测试总数仍为 222，无失败、无跳过。
- DaTangAccountingHelpPlug 的历史源码警告没有新增；阶段 2 将按明确的空值和异步契约修复。

## 阶段结论

阶段 1 只改变 SDK 选择、公共构建属性、版本声明位置和依赖锁定方式，没有升级 TFM
或依赖版本。阶段 2 可以基于可重复恢复的 net9 状态集中迁移到 net10。
