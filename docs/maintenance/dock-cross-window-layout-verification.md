# Document 跨窗口布局崩溃专项维护

> 当前状态（2026-09-21）：Host 整体人工验收由项目所有者手工使用后确认通过，见[人工验收确认](../archive/records/host/manual-acceptance-20260921.md)。本文保留专项命令和后续回归矩阵；原阶段自动化结果以关联记录/JSON 为准，当前交付见[本机部署](local-deployment.md)。

> 核对日期：2026-09-21。框架补丁和自动化回归已实现；最终开发 verify 与产物身份以[开发证据](../archive/records/dock-area-fill/cross-window-layout-fix-evidence.json)为准。Host 人工验收见页首确认；交付状态与现行流程见[本机部署](local-deployment.md)。

## 原因与实际修复

主窗正文迁入已有浮窗后，旧布局队列仍持有同一个控件。Avalonia 12.1.2 的内部消费会沿控件的新父级继续处理，重新安排时触发 `Attempt to call InvalidateArrange on wrong LayoutManager.`。原异常、最小复现见[诊断](../archive/records/dock-area-fill/cross-window-layout-crash-20260917.md)。

已按[实施方案](../archive/plans/host-document-cross-window-layout-crash-fix-plan.md)切换到路线 B。路线 A 的“摘除后刷新旧窗”在 Arrange 回调内迁移时仍失败，因为布局重入不会真正排空队列；该候选已撤销。生产正文回收器不增加刷新、延迟、反射或异常吞并。

补丁在旧任务消费、祖先递归返回、用户布局回调返回和重新入队处核对布局根。旧根只处理自己的任务，新根正常测量和安排；模型、视图、编辑内容、Document Scope 均保留。框架代码只负责布局归属，构建脚本只负责来源与资产身份，Host 继续负责页面生命周期，遵循单一职责。

运行时身份为 `avalonia-12.1.2-host-layout.1`；详细构建、PublicSign 和 SDK 边界见[补丁维护说明](../../patches/avalonia-cross-window-layout/README.md)。仅构建 Host 运行时 Base，官方包、SDK 精确依赖和插件契约不变。Dock 区域 Fill 的命中、方向分屏及布局 V3 不改变。

## 本地开发入口

```powershell
$ErrorActionPreference = 'Stop'
pwsh -NoProfile -File tools/Build-AvaloniaLayoutPatch.ps1
if ($LASTEXITCODE -ne 0) { throw 'Avalonia 布局补丁准备失败' }
pwsh -NoProfile -File tools/Build-DockAreaFillPackage.ps1
if ($LASTEXITCODE -ne 0) { throw 'Dock 补丁准备失败' }
dotnet restore MyAvaloniaManagement.sln --locked-mode
if ($LASTEXITCODE -ne 0) { throw '还原失败' }
dotnet test Host/MyAvaloniaManagement.UiTests -c Release -m:1 --filter 'FullyQualifiedName~DockCrossWindowLayout|FullyQualifiedName~AvaloniaLayoutOwnership'
if ($LASTEXITCODE -ne 0) { throw '跨窗专项失败' }
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1
if ($LASTEXITCODE -ne 0) { throw '门禁工具自测失败' }
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
if ($LASTEXITCODE -ne 0) { throw '完整开发验证失败' }
```

默认 verify 在 restore 之前准备两个补丁，构建后核对 Host 和三个 Host 测试输出的真实 Base DLL。测试收据必须具有通过项、无失败、无跳过；DLL 缺失、旧缓存、摘要不符都阻断。没有执行 Windows CI、seal 或发布 Smoke。

## 自动化覆盖与证据解释

| 方案编号 | 实际覆盖 |
| --- | --- |
| T01–T03 | `DockCrossWindowLayoutUiTests`：主窗最后/非最后文档到已有浮窗，源/目标先布局，检查窗口、唯一归属、编辑状态和有效 Bounds |
| T04 | 同类中的四项浮窗移出测试：浮窗→主窗、浮窗→浮窗；源保留文档或关闭空窗 |
| T05、T07、T10 | `AvaloniaLayoutOwnershipUiTests`：Measure/Arrange 两种待处理队列、祖先回调内迁移、两种窗口处理顺序下各往返 20 次；不依赖 Dock |
| T06 | 生产 Dock 延迟正文模板和首次目标显示，正文包含 Border、TextBlock、TextBox；框架层另含 ScrollViewer/ItemsControl |
| T08 | 同一 Presenter 与同窗交接专测、`DocumentControlRecyclingTests` 的绑定保留及重复请求回归 |
| T09 | `DockAreaFillUiTests` 整组移动保留模型/View/顺序/活动项，测试正文升级为真实多层控件 |
| T11、T13 | Dock 补丁 67 项中的失效目标、最终命中、取消与方向按钮；Host 的 `DockAreaFillUiTests`、`DockPointerCaptureRegressionTests`、全屏限制回归 |
| T12 | 主窗迁移后最终关闭，模型和 View 各释放一次，ClosingToken 取消，晚到模板请求返回空；既有失败清理/生命周期回归继续运行 |
| T14 | `DockToolSplitUiTests`、`DockToolWindowCloseUiTests`、`DockLayoutV3UiTests`，以及工具合并与重启回归 |
| T15 | `MouseDown/MouseMove/MouseUp` 从主窗标签拖入已有浮窗正文，分别覆盖主窗最后/非最后文档 |

新专项共有 20 项：13 项 Host 交接和七项框架布局测试。没有通过删除控件、重建页面、关闭主窗或放宽布局断言取得绿色结果。最终 verify 还执行完整 UI 和非 UI 套件。

P0 初始四个逻辑用例为三失败一通过，额外一条相同清理异常导致 TRX 记录四失败一通过。随后 T15 的最后/非最后两个鼠标用例在官方 Base（SHA-256 `39F7C973A20346B2A0C0E758B875749670A610D63863E9A85FFEA22D5F320279`）下均以同一异常失败，补齐 T02 红灯证据。具体 TRX 摘要见[红灯证据](../archive/records/dock-area-fill/cross-window-layout-red-evidence.json)和本轮证据，不能混用前一轮 1039 项通过结果。

## 产物身份与重建

固定 .NET SDK 10.0.302；补丁代码来自 Avalonia `d3c867a9e2de379249b03dbeb3495bd7f076a81a`。已用不同检出路径和独立 NuGet 目录比对：Base DLL 与 PDB 字节一致。官方公开 API 对候选的 APICompat 比较通过。

MSBuild 在 build/test 和 publish 文件列表中选择同一 Base DLL，并在 bundle 生成前核对唯一性和哈希。本轮另在隔离 artifacts 目录核对普通 publish 的 DLL，以及压缩单 EXE 内实际解压得到的 DLL。单文件格式核对依据 [.NET Manifest](https://github.com/dotnet/runtime/blob/v10.0.10/src/installer/managed/Microsoft.NET.HostModel/Bundle/Manifest.cs) 和 [FileEntry](https://github.com/dotnet/runtime/blob/v10.0.10/src/installer/managed/Microsoft.NET.HostModel/Bundle/FileEntry.cs)；最终产物摘要写入非嵌入证据。

上述开发证据只覆盖本地构建路径，当时没有替换 `D:\data\avalonia`，也不构成发布门禁或真实桌面验收。安装部署单独记录，不倒写开发阶段的事实。修改 Markdown 后重新构建，以确保嵌入帮助与源码一致；最终通过后仅更新非嵌入 JSON。

## 桌面验收和回退

Host 当前人工验收已经收口；原方案 M01–M07 作为后续回归矩阵保留，不追加虚构的逐项日志。百度网盘、视频/WebView 等外部插件专有业务结果仍由对应插件记录。Headless 鼠标事件与所有者实际手工验收是不同来源的证据。

部署按专用说明生成最终自包含单 EXE，核对本次补丁身份并记录安装版哈希；不得拿旧部署记录证明新修复已安装。完整回退恢复源码、补丁身份与对应交付物；修复前安装版仍带已知崩溃，恢复它只表示撤销候选，不能表示故障解决。
