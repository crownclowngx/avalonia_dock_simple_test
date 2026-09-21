# V20 单文件自包含本机交付

> 日期：2026-09-21。用途：记录用户授权的 V20 本机编译、交付与中间文件清理范围。实际完成状态和产物身份以[部署 JSON](local-deployment-20260921.json)为准；本文在构建前固定并随帮助一起嵌入。

本次交付包含已经实施的 Layout V2 完整退役、V3 行为验证、布局产物与写锁验证修正，以及旧文档创建分组查询退出。代码验证来自[完整开发证据](development-evidence.json)：1,325 项测试通过，零失败、零跳过；本次仅同步交付文档，不修改生产源码、测试、依赖与锁文件。

## 构建与验证

使用固定 SDK `10.0.302`，从干净 Git 提交归档隔离副本，准备已验证的 Avalonia 与 Dock 补丁。RID restore 和 publish 仅在该副本执行，避免污染主仓锁文件。

发布参数为 Release、win-x64、`SelfContained=true`、`PublishSingleFile=true`、`IncludeNativeLibrariesForSelfExtract=true`、`EnableCompressionInSingleFile=true`、`PublishTrimmed=false`、`SkipPluginDeploy=true`，编译警告视为错误。运行时和原生绘制库进入 EXE；`Controls` 与 `HelpWeb` 保持外置，SDK XML 和调试符号不交付到安装目录。

核对最终 EXE 的 bundle 清单、运行时配置、Avalonia/Dock/Host 摘要和本仓嵌入文档。实际 EXE 使用空插件目录、隔离用户数据和解包缓存启动，走真实窗口的正常关闭流程，检查退出码、标准错误及 Layout V3。该检查覆盖单文件启动与正常关闭，不代表外部业务插件、重启往返或多屏人工验收。

## 安装、回退与清理

目标固定为 `D:\data\avalonia`。安装前检查运行实例、路径及重解析点；对真正改变的已有文件创建独立回退副本并核对摘要。交付后逐项核对 EXE、帮助资源与保留文件，现有插件及用户数据保持原样。旧 Layout V2 文件不属于清理范围，文档信封 V2 和默认用户数据目录不变。

清理限于本次专用构建目录中的源码副本（含 bin/obj）、发布暂存、源码 ZIP、解包缓存、隔离启动数据和临时提取程序集。删除前检查绝对路径及重解析点；保留小型日志、脚本、摘要清单、验证收据和安装回退副本。其他任务的构建产物不属于本次清理范围。

本次是用户指定目录的本机交付，不上传包、不推送 Git，不执行 Windows CI 或正式 seal。正式发布条件仍见[主仓验证与封板](../../../maintenance/verification.md)，本次不修改门禁阈值。
