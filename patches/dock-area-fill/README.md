# Dock 区域停靠补丁

本目录保存 Host 专用呈现层补丁，依据 [MIT 许可证](LICENSE.TXT) 维护。它不是官方 Dock 发布包，也不是本项目 Plugin SDK 的新版本。

## 固定输入与产物

固定基线为官方 `12.1.5`，本地包为 `12.1.5.1-area.1`，保留 `Dock.Avalonia` 程序集版本 `12.1.5.0` 以便官方主题绑定。Model、Settings、Controls 和 Theme 使用官方包。SDK `10.0.302`、固定提交、最低测试数量和最终包 SHA-256 见 [baseline.json](baseline.json)，源码差异见 [area-fill.patch](area-fill.patch)。

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
| `DockManagerState` | 将宿主预览矩形投影到当前提示层 |
| `DockControlState`、`HostWindowState` | 接入宿主局部预览，使用上游拖动、校验和预览清理逻辑 |
| `DockHelpers` | 排除关闭窗口后再进行窗口层级排序，保留非 Window 的有效 TopLevel |
| `IDockPreviewProvider` | 提供宿主特有的分屏预览矩形 |

额外的停靠约束接入官方 `IDockDropPolicy`。测试沿用上游 xUnit `3.2.2`，SourceLink 沿用 `10.0.402`。打包时呈现项目引用与基线同版的官方包，保持 Model 等程序集的来源。

## 后续更新和回退

1. 在独立上游检出修改并验证；从固定基线生成 `git diff --binary --full-index --no-ext-diff`，不要手工复制 DLL。
2. 修改 `baseline.json`、中央补丁版本并递增预发布尾号，同时分配新的固定 `packageTimestampUtc`。同版本的已知哈希必须保留；新候选首次定稿后固定哈希，再用全新检出重建验证。
3. 审阅并更新主仓锁文件，执行 [专项开发验证](../../docs/maintenance/dock-area-fill-verification.md) 和完整 verify。
4. 可以通过 Host 的 `FillOnAreaDrop` 样式停用正文区域回停。切换到官方包时，一并替换区域回停和预览实现，调整包源映射以及锁文件。

新 SDK、上游版本、签名方式或补丁输入都需要重新构建、审阅哈希及完整回归。原生遮挡、多屏 DPI 和外部原生控件验收继续单独记录。
