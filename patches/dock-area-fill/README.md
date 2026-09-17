# Dock 区域停靠补丁

本目录保存 Host 专用呈现层补丁，依据 [MIT 许可证](LICENSE.TXT) 维护。它不是官方 Dock 发布包，也不是本项目 Plugin SDK 的新版本。

## 固定输入与产物

唯一版本事实见 [baseline.json](baseline.json)：上游仓库、提交 `cc08602d02fde1b85067cec064da29f34785e505`、官方依赖 `12.1.0.6`、补丁包 `12.1.0.7-area.5`、SDK `10.0.302`、最低测试数量与最终包 SHA-256。补丁见 [area-fill.patch](area-fill.patch)。仅 `Dock.Avalonia` 使用补丁，其 Model、Settings、Controls、Theme 使用官方依赖。程序集版本仍为 `12.1.0.6`，以便官方主题按原身份绑定。

上游只有公开签名密钥，因此补丁启用 PublicSign，保持相同公钥标识；不能把它说成持有上游私钥签名的官方产物。本项目运行于 .NET 10；其他运行时的签名验证不在本次范围。

## 重建

在主仓根目录安装 Git、PowerShell 7 和指定 SDK 后执行：

```powershell
pwsh -NoProfile -File tools/Build-DockAreaFillPackage.ps1
dotnet restore MyAvaloniaManagement.sln --locked-mode
```

脚本把干净上游检出放在 `artifacts/dock-area-fill/build/<输入摘要>/source`，校验固定提交后应用补丁，并用完整 Git diff 验证没有额外源码。先运行区域命中、两条拖动入口、目标选择及关闭窗口相关的 Headless 测试，再构建 net8.0/net10.0 包到仓库本地 feed。测试必须达到基线数量，零失败、零跳过。Host 的真实样式和生命周期测试另由主仓 verify 运行。

构建启用确定性源路径，使 PDB/SourceLink 不包含本机检出位置；NuGet ZIP 使用基线中按包版本固定的时间戳，随机 core-properties 名称及关系 ID 统一规范化，DLL 和 XML 原字节保留。每个新包版本必须分配不同的时间戳（至少相差 2 秒），防止增量构建把同大小、同时间戳的旧 DLL 留在输出目录。Gate 在构建后逐字节哈希比对 Host 与测试输出中的 DLL 和包内 DLL。`ContinuousIntegrationBuild=true` 只是本地确定性编译参数，不启动 CI 或发布流程。

每次都检查版本控制中的包哈希；已存在的同版本包不允许被不同内容覆盖。成功结果写入构建目录 `receipt.json`。只有源码输入、脚本身份、测试收据和包哈希全部匹配时，才复用已测试的包。`-ForceRebuild` 使用全新检出目录再次构建并与同一固定包哈希比较。

`nuget.config` 将 `Dock.Avalonia` 映射到本仓 feed，其余包映射到 NuGet 官方源。构建上游时显式使用上游源配置，不继承主仓对补丁的映射。不能手工替换用户 NuGet 缓存里的 DLL。首次联网构建需要从 GitHub 与 NuGet 下载固定输入。

## 补丁责任

| 文件 / 类型 | 责任 |
| --- | --- |
| `DockTarget.FillOnAreaDrop` | 默认关闭的局部区域策略，Host 样式显式开启 |
| `DockTargetBase` | 区分明确按钮命中与校验结果，检查正文边界和裁剪，绘制选中区域 |
| `DockManagerState` | 两条入口共用操作决策，全局明确按钮优先，统一预览清理与额外限制 |
| `DockControlState`、`HostWindowState` | 保留原输入协议，在松开时刷新最终落点和权限；取消清理 |
| `DockHelpers` | 排除关闭窗口后再进行窗口层级排序，保留非 Window 的有效 TopLevel |
| `IDockDropGuard` | 由 Host 提供额外拒绝条件，不代替 Dock 能力验证 |
| `IDockPreviewProvider` | 由 Host 提供绘制矩形，不接管布局与模型 |

上游测试依赖的 xUnit 对齐到 `3.2.2`；SourceLink 构建工具固定到 `10.0.401`，避免原版本 NuGet 审计告警。打包时仅呈现项目切换为官方 PackageReference，防止补丁包版本传递给未修改的 Model 项目。

## 后续更新和回退

1. 在独立上游检出修改并验证；从固定基线生成 `git diff --binary --full-index --no-ext-diff`，不要手工复制 DLL。
2. 修改 `baseline.json`、中央补丁版本并递增预发布尾号，同时分配新的固定 `packageTimestampUtc`。同版本的已知哈希必须保留；新候选首次定稿后固定哈希，再用全新检出重建验证。
3. 审阅并更新主仓锁文件，执行 [专项开发验证](../../docs/maintenance/dock-area-fill-verification.md) 和完整 verify。
4. 临时关闭区域回退只需将 Host 的 `FillOnAreaDrop` 样式设为 False；恢复官方包还必须同时移除 Host 对两个补丁接口的实现，并恢复官方包源映射和锁文件。不能只替换 DLL。

新 SDK、上游版本、签名方式或补丁输入都需要重新构建、审阅哈希及完整回归。原生遮挡、多屏 DPI 和外部原生控件验收继续单独记录。
