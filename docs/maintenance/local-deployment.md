# Host 本机部署

> 用途：当前本机交付形式、证据入口及后续部署流程。状态：当前；核对日期：2026-09-21。事实源：[Host 项目](../../Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj)、[版本属性](../../Directory.Version.props)及下列部署记录。

## 已记录的最新交付

V19 复杂度收敛版本的本机交付目标为 `D:\data\avalonia\MyAvaloniaManagement.exe`，见[专用交付说明](../archive/records/host-v19/local-deployment-guide.md)和[部署证据](../archive/records/host-v19/local-deployment-20260921.json)。最终状态、源码 revision、构建参数、EXE 和补丁摘要、安装文件核对及清理结果以该 JSON 为准，`status=completed` 才表示交付完成。V18 实现及 V19 方案文档的前一次交付保留在[历史记录](../archive/records/host-v18/local-deployment-with-v19-plan-20260921.json)。记录不能代替对安装目录实时状态的检查。

当前交付形式为 Release、win-x64、自包含压缩单 EXE，打入 .NET 运行时与原生绘制库，关闭裁剪；`Controls` 插件和 `HelpWeb` 帮助资源外置。安装版不要求另装 .NET。每次单文件形式核对和隔离启动/关闭均按本次产物记录；既有[项目所有者人工验收](../archive/records/host/manual-acceptance-20260921.md)只代表原始验收范围。

V18 在此前功能上提供命令面板连续分组、键盘会话和页面目标校验。方案名称 V11–V19 表示改造阶段，产品和包版本仍以[集中基线](../reference/platform-baseline.md)为准；文档随包嵌入不表示对应方案已经实施。

## 后续部署流程

1. 固定本次源码与文档输入，保存验证结果；从该输入创建独立构建目录。RID restore/publish 在隔离副本进行，避免改变主仓锁文件。
2. 按[跨窗布局](dock-cross-window-layout-verification.md)和[区域回停](dock-area-fill-verification.md)说明准备固定补丁。只发布 Host，设置 `SkipPluginDeploy=true`。
3. 使用 `SelfContained=true`、`PublishSingleFile=true`、`IncludeNativeLibrariesForSelfExtract=true`、`EnableCompressionInSingleFile=true`、`PublishTrimmed=false`，将构建警告视为错误。
4. 核对最终 bundle 中实际运行时和补丁身份，再用空插件、隔离数据根与解包缓存验证实际 EXE 启动、正常关闭及 Layout V3。需要验证单文件重启或具体业务时，另记实际覆盖场景。
5. 安装前检查实例是否退出，记录目标目录文件摘要并落实本次备份/回退安排；逐项交付已核对的文件，保留现有插件及用户数据，核对保留文件字节不变。
6. 记录已交付文件、源码、产物摘要、检查结果与清理范围。清理仅限本次隔离目录，先核对绝对路径和重解析点，再删除本次中间产物；保留小型日志与收据。

V15–V17 及 V18 首次部署的不备份操作来自对应任务的用户要求；2026-09-21 随 V19 方案文档再次部署时已备份被替换 EXE，详见各自 JSON。各次选择不构成后续部署的默认政策。回退必须检查备份当前仍存在且身份匹配，不能假定旧备份覆盖最新版本。正式发布条件见[主仓验证与封板](verification.md)。

## 历次交付

| 阶段 | 原说明或证据 |
| --- | --- |
| V19 复杂度收敛 | [专用交付说明](../archive/records/host-v19/local-deployment-guide.md)、[2026-09-21 JSON](../archive/records/host-v19/local-deployment-20260921.json) |
| V18 实现 + V19 方案文档 | [2026-09-21 后续部署 JSON](../archive/records/host-v18/local-deployment-with-v19-plan-20260921.json)；原存于 host-v19，按实际代码阶段归位，原始字节保留 |
| V18 命令面板交互 | [2026-09-21 首次部署 JSON](../archive/records/host-v18/local-deployment-20260921.json) |
| V17 可读性重构 | [2026-09-20 JSON](../archive/records/host-v17/local-deployment-20260920.json) |
| V16 文档浮窗与新建位置 | [2026-09-20 JSON](../archive/records/host-v16/local-deployment-20260920.json) |
| V15 启动窗口 | [归档说明](../archive/records/host-v15/local-deployment-guide.md)、[2026-09-20 JSON](../archive/records/host-v15/local-deployment-20260920.json) |
| V14 自动重启 | [归档说明](../archive/records/host-v14/local-deployment-guide.md)、[2026-09-19 JSON](../archive/records/host-v14/local-deployment-20260919.json) |
| V13 插件开关 | [归档说明](../archive/records/host-v13/local-deployment-guide.md)、[2026-09-19 JSON](../archive/records/host-v13/local-deployment-20260919.json) |
| V12 内部职责重构 | [归档说明](../archive/records/host-v12/local-deployment-guide.md)、[2026-09-17 JSON](../archive/records/host-v12/local-deployment-20260917.json) |
| Document 跨窗布局修复 | [归档说明](../archive/records/dock-area-fill/cross-window-layout-deployment-guide.md)、[2026-09-17 JSON](../archive/records/dock-area-fill/cross-window-layout-deployment-20260917.json) |
| 区域回停 | [2026-09-17 JSON](../archive/records/dock-area-fill/local-deployment-20260917.json) |
| V11 / P1 / P2 | [V11 JSON](../archive/records/host-v11/local-deployment-20260916.json)、[P1 JSON](../archive/records/host-v11/p1-local-deployment-20260916.json)、[P2 JSON](../archive/records/host-v11/p2-local-deployment-20260916.json) |
