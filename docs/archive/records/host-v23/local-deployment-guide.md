# V23 单文件自包含本机交付

> 日期：2026-09-24。用途：记录用户授权的 V23 本机编译、交付与中间文件清理范围。实际完成状态和产物身份以[部署 JSON](local-deployment-20260924.json)为准；本文在构建前固定并随帮助一起嵌入。

本次交付包含已实施的本地 ZIP 插件检查、安装更新、重启前应用和失败恢复，使用方式见[插件看板](../../../quick-start/plugin-status.md)，边界见[安装契约](../../../reference/plugin-installation.md)。代码验证来自[完整开发证据](development-evidence.json)：1,456 项测试通过，零失败、零跳过。本次部署只同步交付文档，不修改生产源码、测试、依赖或版本。

## 构建与验证

使用固定 SDK `10.0.302`，从干净 Git 提交归档隔离副本，准备已验证的 Avalonia 与 Dock 补丁。RID restore 和 publish 仅在该副本执行，避免改变主仓锁文件。

发布参数为 Release、win-x64、`SelfContained=true`、`PublishSingleFile=true`、`IncludeNativeLibrariesForSelfExtract=true`、`EnableCompressionInSingleFile=true`、`PublishTrimmed=false`、`SkipPluginDeploy=true`，编译警告视为错误。运行时和原生绘制库进入 EXE；`Controls` 与 `HelpWeb` 保持外置，SDK XML 和调试符号不交付到安装目录。

核对最终 EXE 的 bundle 清单、运行时配置、Avalonia/Dock/Host 摘要和本仓嵌入文档。实际 EXE 使用空插件目录、隔离用户数据和解包缓存启动，走真实窗口的正常关闭流程，检查退出码、标准错误及 Layout V3。该检查覆盖单文件启动与正常关闭；已有 13 个外部插件的业务使用不属于本次隔离启动覆盖范围。

## 安装与清理

目标固定为 `D:\data\avalonia`。按照本次用户要求不创建备份。安装前检查运行实例、路径及重解析点；记录已有文件摘要，核对临时文件后同卷替换。仅交付 EXE 与 HelpWeb 资源，保留现有插件、配置和用户数据，并逐项核对保留文件字节不变。隔离启动产生的 `Controls/.plugin-management` 不交付到用户安装目录。

这里的“不备份”仅指本次 Host 部署，不改变 V23 插件安装事务为失败恢复保留旧载荷的行为。目标目录下的既有插件管理状态和其他历史数据均不属于清理范围。

清理限于本次专用构建目录中的源码副本（含 bin/obj）、发布暂存、源码 ZIP、解包缓存、隔离启动数据和临时提取程序集。删除前检查绝对路径及重解析点；保留小型日志、脚本、摘要清单和验证收据。其他任务的构建产物不属于本次清理范围。

本次是用户指定目录的本机交付，不上传包、不推送 Git，不执行 Windows CI 或发布门禁。正式发布条件仍见[主仓验证与封板](../../../maintenance/verification.md)，本次不修改门禁阈值。
