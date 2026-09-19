# V14 桌面工作台单文件自包含部署

> 日期：2026-09-19。用户授权提交 Git，并将产物部署到 `C:\Users\admin\Desktop\工作台`。
> 本文在构建前定稿；固定源码、实际产物摘要、备份和完成状态以[本次部署证据](../archive/records/host-v14/local-deployment-20260919.json)为准，不以计划冒充部署成功。

## 交付内容

Host 使用 `Release`、`win-x64`、`SelfContained=true`、`PublishSingleFile=true`、`IncludeNativeLibrariesForSelfExtract=true`、`EnableCompressionInSingleFile=true`、`PublishTrimmed=false`。Host、.NET 运行时和原生绘制库打入 `MyAvaloniaManagement.exe`，无需另外安装 .NET；帮助资源 `HelpWeb` 和独立插件目录 `Controls` 继续外置。

包含 V14 **文件 → 重新启动 Host…**、插件看板 **重启并应用更改…**，行为见[重启指南](../quick-start/restart-host.md)及[契约](../reference/host-restart.md)。保存和关闭可取消，助手只在旧 Host 清理成功并实际退出后启动新进程。上次 Document 不自动重开，默认欢迎页仍创建。

## 构建与交付检查

1. 提交部署文档，从干净 master 的固定提交 `git archive` 到隔离目录；RID restore/publish 不修改主仓 lock 文件。编译警告按错误处理。
2. 核对已有 Avalonia 布局补丁和固定 Dock 包的摘要与测试收据，再复制到隔离构建输入；发布设置 `SkipPluginDeploy=true`，不向安装目录部署测试插件。
3. 检查最终 EXE 的 bundle 清单、Host/CoreLib、原生库、自包含运行时配置及实际打入的两个补丁摘要。
4. 暂存 EXE 使用隔离数据根、空插件目录、隔离解包缓存和不存在的 DOTNET_ROOT 启动；沿用既有自动关闭入口，核对正常退出、诊断及 Layout V3。该本机产物检查不运行正式 Windows Smoke 门禁，不操作安装插件或用户数据。
5. 确认安装实例未运行，记录目标全部文件摘要；变更文件先备份到桌面 `工作台-host-backups` 本轮目录，再逐项原子替换。核对交付文件与暂存一致，插件及其他保留文件字节不变。
6. 部署完成后只回填非嵌入 JSON 并提交，不改已打包的源码或 Markdown。

V14 完整开发 `verify` 已通过 1,186 项，零失败、零跳过，Release 构建零警告/错误，见[开发证据](../archive/records/host-v14/final-development-evidence.json)。本次不改生产代码，针对单文件、自包含交付重新编译并验证实际产物。

启动检查只证明该格式的真实启动与正常关闭，不替代单文件下菜单/看板重启往返、原生保存选择器、焦点、多屏或业务插件验收；这些范围继续按[专用验证](host-v14-automatic-restart-verification.md)分别记录。

## 使用与回退

部署后打开 `C:\Users\admin\Desktop\工作台\MyAvaloniaManagement.exe`。原数据根和已有插件保持原位；不会重置插件开关、布局、收藏或业务文件。

回退前关闭安装实例，按部署 JSON 的备份清单恢复被替换文件并核对原 SHA-256；保留 Controls 和用户数据。Host 备份不回滚用户此后保存的业务内容。

本次是指定目录的本机交付，不推送 Git、不上传包、不创建公开发布。沿用此前部署约定，不使用 AIFLOW、Windows CI 或正式 seal 门禁；不修改发布政策或 API 基线。
