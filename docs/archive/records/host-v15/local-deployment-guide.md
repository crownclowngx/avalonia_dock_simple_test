# V15 单文件自包含本机部署

> 归档更新（2026-09-20）：本文保留原阶段方案、操作和验证快照；正文中的“本轮”“未执行”“待验收”指原记录时间。当前 Host 人工验收已由项目所有者确认通过，见[统一验收记录](../host/manual-acceptance-20260920.md)；后续候选与发布事项见[待办](../../../roadmap/README.md)，现行操作从[文档导航](../../../README.md)进入。

> 日期：2026-09-20。用户授权提交 Git、编译并部署到 `D:\data\avalonia`，明确不做备份，并清理本次构建中间文件。
> 本文在编译前定稿；实际源码提交、产物摘要、部署和清理结果以[部署证据](local-deployment-20260920.json)为准。

## 交付形式

使用 `Release`、`win-x64`、`SelfContained=true`、`PublishSingleFile=true`、`IncludeNativeLibrariesForSelfExtract=true`、`EnableCompressionInSingleFile=true`、`PublishTrimmed=false`。Host、.NET 运行时和原生绘制库打入 `MyAvaloniaManagement.exe`，不依赖目标机器另装 .NET。动态插件仍位于 `Controls`，帮助资源仍位于 `HelpWeb`。

本次包含 [V15 羽毛启动窗口与真实插件进度](../../../reference/host-startup.md)，保留 V14 重启协议。按 SOLID 分离后台组合、启动状态及桌面适配；加载完成才交接主窗口，取消和失败复用同一资源关闭链。

## 构建、核对和部署

1. 将 V15 实现、测试和文档提交，记录完整 title 与 desc。从固定提交 `git archive` 到本轮专用隔离目录；RID restore/publish 不改主仓锁文件。
2. 核对固定 Avalonia 布局补丁和 Dock 包，复制已验证输入。启用 `SkipPluginDeploy=true`，只发布 Host；Release 构建将警告视为错误。
3. 读取最终 EXE 的 bundle，检查宿主、CoreLib、原生绘制库、自包含运行时配置和实际打入的补丁摘要；发布根目录不应散落运行时 DLL。
4. 在空插件、独立数据根及独立解包缓存中启动实际 EXE，指定不存在的 `DOTNET_ROOT`，通过既有自动关闭入口验证正常启动、关闭和 Layout V3。这是本机产物检查，不运行正式 Windows Smoke 门禁，也不加载安装目录的业务插件。
5. 确认安装实例没有运行，记录原安装目录文件摘要。核对待交付文件后逐项替换；遵从用户要求，不创建备份。保留现有 `Controls` 和不属于本次交付的文件，并核对其摘要不变。
6. 部署后核对 EXE 与暂存产物一致。回填非嵌入 JSON 并提交，保持已打包源码与 Markdown 不变。

开发基线已通过完整本地 `verify`：1,218 项测试，零失败、零跳过，Release 零警告、零错误；门禁工具自测 68 项通过。详见[开发证据](final-development-evidence.json)。本次只增加部署说明及交付记录，不改变生产实现。

## 清理和验收边界

清理限定在本轮专用目录：隔离源码及其 `bin/obj`、源码压缩包、发布暂存副本、解包缓存和隔离启动数据。删除前检查绝对路径及重解析点，避免越出本轮目录。保留小型构建日志、摘要与验证记录；不清空安装根目录、插件目录、用户数据或其他任务的历史产物。

部署完成后从 `D:\data\avalonia\MyAvaloniaManagement.exe` 启动。实际插件的原生依赖、动画连续性、焦点、多屏和混合 DPI 仍按[专项矩阵](../../../maintenance/host-v15-startup-verification.md)独立验收；空插件启动检查不能替代这些观察。

本次为本机目录交付，不推送 Git、不公开发布，不使用 AIFLOW、Windows CI 或正式 seal/发布门禁。没有备份时不能从本轮目录直接恢复旧 EXE；已有历史备份不在本次清理范围内。
