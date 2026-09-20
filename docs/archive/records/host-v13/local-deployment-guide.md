# V13 桌面工作台单文件自包含部署

> 归档更新（2026-09-20）：本文保留原阶段方案、操作和验证快照；正文中的“本轮”“未执行”“待验收”指原记录时间。当前 Host 人工验收已由项目所有者确认通过，见[统一验收记录](../host/manual-acceptance-20260920.md)；后续候选与发布事项见[待办](../../../roadmap/README.md)，现行操作从[文档导航](../../../README.md)进入。

> 日期：2026-09-19。用户授权提交 Git，并部署至 `C:\Users\admin\Desktop\工作台`。
> 构建源码、最终 EXE 摘要、备份路径和完成状态以[本次部署证据](local-deployment-20260919.json)为准；本文先于最终产物构建定稿。

## 交付范围

使用 `Release`、`win-x64`、`SelfContained=true`、`PublishSingleFile=true`、`IncludeNativeLibrariesForSelfExtract=true`、`EnableCompressionInSingleFile=true`、`PublishTrimmed=false`。Host 与 .NET 运行时、原生绘制库打入主 EXE；`HelpWeb` 是外置帮助资源，`Controls` 保持独立插件目录。无需安装 .NET 运行时；插件自身的外部依赖仍按原要求提供。

交付包含 V13 插件看板开关：**工具 → 插件看板 → 选择插件 → 概览 → 下次启动启用**，保存后完整退出并重启生效。原子设置、错误恢复和当前/下次状态规则见[插件开关契约](../../../reference/plugin-enablement.md)。

## 构建与本机检查

1. 提交当前源码与文档，用 `git archive` 导出固定提交。RID restore/publish 在隔离目录执行，不改变主仓依赖锁。
2. 使用已通过验证的 Avalonia 布局补丁与固定 Dock 本地包，核对收据和实际摘要。发布时设 `SkipPluginDeploy=true`，不把开发夹具部署进用户插件目录。
3. 编译警告按错误处理。核对 EXE bundle：Host、CoreLib、原生库与自包含运行时配置；验证实际打入的 Avalonia 和 Dock 补丁摘要。
4. 对暂存产物使用隔离数据目录、空插件目录和不存在的 DOTNET_ROOT 启动，沿用现有自动关闭入口正常退出，核对诊断与 Layout V3。此检查不替代业务插件或原生手工验收。
5. 确认目标实例未运行，记录目录文件摘要。把被替换文件备份至桌面 `工作台-host-backups` 的本次目录，然后原子替换改变的交付文件。逐项核验 EXE/帮助资源与暂存一致，Controls 和其他保留文件字节不变。
6. 仅回填非嵌入 JSON 部署证据并提交，不再改变已打包代码和 Markdown。

前一阶段本地开发 verify 已通过 1143 项、零失败/跳过，构建零警告/错误，详见[开发证据](final-development-evidence.json)。本次无生产代码变化，针对最终发布格式重新编译和检查。

## 使用与回退

部署后从 `C:\Users\admin\Desktop\工作台\MyAvaloniaManagement.exe` 启动。禁用一个插件时当前页面应继续工作，重启后看板保留禁用项；重新启用并再次重启恢复入口。配置文件位于原 Host 数据根，部署不重置开关、布局、收藏或业务文件。

需要回退时先关闭安装实例，按 JSON 中的备份清单恢复被替换文件并核对原 SHA-256；保留 Controls 和用户数据。备份撤销本轮 Host 更新，不撤销用户此后编辑的业务内容。

本次是用户指定目录的本机交付，不推送 Git、不上传包或创建公开发布；不使用 AIFLOW、Windows CI 或正式 seal 门禁。原生桌面与业务手工验收仍单独记录。
