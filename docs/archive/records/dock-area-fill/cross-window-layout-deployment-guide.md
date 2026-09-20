# 跨窗口布局修复的单文件自包含部署

> 归档更新（2026-09-20）：本文保留原阶段方案、操作和验证快照；正文中的“本轮”“未执行”“待验收”指原记录时间。当前 Host 人工验收已由项目所有者确认通过，见[统一验收记录](../host/manual-acceptance-20260920.md)；后续候选与发布事项见[待办](../../../roadmap/README.md)，现行操作从[文档导航](../../../README.md)进入。

> 用途：把已验证的 Document 跨窗修复部署到本机 `D:\data\avalonia`。日期：2026-09-17。用户已授权提交、编译和部署；实际完成状态、源码提交、安装版摘要及备份位置以[本次部署证据](cross-window-layout-deployment-20260917.json)为准。

## 输入与交付范围

源码修复为 `19c0b3b`，完整开发验证记录在[修复证据](cross-window-layout-fix-evidence.json)：1059 项本地 verify、68 项门禁工具自测和七项独立框架测试通过。后续部署提交仅同步文档，不改变已经验证的生产代码。

发布参数固定为 `Release`、`win-x64`、`SelfContained=true`、`PublishSingleFile=true`、`IncludeNativeLibrariesForSelfExtract=true`、`EnableCompressionInSingleFile=true`、`PublishTrimmed=false`。Host 和 .NET 运行时打入同一个 EXE；现有 `HelpWeb` 为帮助资源目录，`Controls` 为外部插件目录，不把插件合入主程序。

必须包含 `avalonia-12.1.2-host-layout.1` 布局补丁和 `12.1.0.7-area.5` Dock 区域回停补丁。Base DLL 的预期摘要读取 `patches/avalonia-cross-window-layout/baseline.json`，不能只根据 EXE 文件名或 NuGet 版本判断补丁是否进入产物。

## 执行顺序

1. 提交文档并确认工作树干净，用 `git archive` 导出该提交。RID 还原、发布在隔离源码目录进行，避免更改工作区依赖锁。
2. 核对现有补丁构建收据和实际文件摘要，将已经验证的 Base 运行时与固定 Dock 本地包复制到隔离源码的对应 artifacts 目录。使用固定 SDK `10.0.302`，发布时指定 `SkipPluginDeploy=true`。
3. 从隔离副本还原并生成自包含单 EXE，编译警告按错误处理。暂存主 EXE 和 HelpWeb，PDB、SDK XML 与验证探针留在构建目录。
4. 解读实际 EXE 的 bundle 清单并解压 Base DLL，核对补丁摘要。使用隔离数据目录执行身份探针启动和无探针启动，核对自包含运行时、补丁版本、正常退出及布局 V3 文件；暂存目录不装载业务插件。
5. 核对安装进程未运行，记录安装目录和 Controls 的完整文件摘要、目录树。只替换内容发生变化的交付文件，先验证临时文件，再原子替换并备份旧文件到 `D:\data\avalonia-host-backups` 下本次专用目录。
6. 核对安装产物与暂存产物完全一致，核对 Controls 及其余保留文件未改变，写入本次非嵌入 JSON 证据并单独提交。部署后的证据提交不改变已打包代码与嵌入文档。

## 验收边界与回退

本地编译、bundle 核对和隔离启动负责证明交付物可启动且包含修复。真实桌面 M01–M07 仍需要在安装版执行，包括百度网盘正文拖入已有欢迎浮窗、遮挡、多屏 DPI 和原生页面；隔离启动不能代替这些业务操作。此次不调用 Windows CI 或正式 seal 流程。

回退时先关闭安装实例，按本次证据中的文件清单从专用备份恢复被替换的文件，并核对恢复后的 SHA-256。保留 Controls 和用户数据。备份的旧 EXE 带有原已知崩溃，恢复它只表示撤销部署。
