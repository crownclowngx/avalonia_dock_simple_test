# V9：六个 NuGet 包统一发布 3.4.1

日期：2026-09-16。分支：`codex/host-v9-avalonia-dock-upgrade`。

状态：准备与验证中；最终上传结果及公共源消费证据在本记录追加。用户在 V9 开发验收后明确授权上传 NuGet，并要求所有自有包统一版本。本次发布范围是 NuGet 开发依赖，不是 Host 安装程序发布。

## 版本与兼容政策

`Directory.Version.props` 的 `MyAvaloniaPackageVersion=3.4.1` 是六个包的唯一发布版本事实。各组件属性只映射它，项目不再硬编码独立版本。

| 包 | 此前公开版本 | 本次版本 | 职责与兼容边界 |
| --- | --- | --- | --- |
| MyAvaloniaManagement.PluginSdk | 3.4.0 | 3.4.1 | BCL-only Core；v3 API 基线不变 |
| MyAvaloniaManagement.PluginSdk.UI | 3.4.0 | 3.4.1 | Core 同版本；Avalonia 精确 12.1.2；无 Dock 依赖 |
| MyAvaloniaManagement.PluginSdk.Workflow | 1.0.0 | 3.4.1 | 包号对齐；v1 API 与 AssemblyVersion 1.0.0.0 保留 |
| MyAvaloniaManagement.Icons | 1.0.0 | 3.4.1 | 仍为普通私有资源程序集，不进入共享闭包 |
| MyAvaloniaManagement.Plugin.Build | 1.1.3 | 3.4.1 | 原构建/打包协议不变 |
| MyAvaloniaManagement.Plugin.Templates | 1.4.1 | 3.4.1 | Core/UI/Icons/Build 精确 3.4.1，Desktop 精确 12.1.2 |

这次统一的是包发布号。Host 产品仍为 3.0.0，Avalonia/Dock 为 12.1.2/12.1.0.6；manifest、Document、layout 仍为 schema 2。既有业务插件 DLL、清单及插件版本不批量重写；模板新生成插件自己的业务版本仍从 1.0.0 开始，最低 Host SDK 为 3.4.1。

Workflow 的包主版本由 1 跳至 3 仅为统一发布编号，不表示 API 破坏。文件版本反映 3.4.1.0，二进制契约身份保持 1.0.0.0。Core/UI 延续 V9 已验证的 3.4.1.0；Icons 由各插件私有加载，不要求旧插件替换它自己的副本。

## 可复现的发布顺序

1. 核对 NuGet.org 六个 ID 的已发布版本，确认 3.4.1 不存在；同步中央属性、模板依赖、包内 README 和版本政策测试。
2. 执行普通 `Gate verify`，验证所有单元/契约/Headless/真实测试 ZIP；显式运行旧插件产物夹具。
3. 在 `artifacts/host-v9/nuget-3.4.1/` 冻结五个基础 nupkg 和四个 snupkg；记录 SHA-256、nuspec 依赖和源码提交。
4. 用独立缓存、本地 feed、仓库外模板生成项目验证 Release 构建、测试与 ZIP。模板包包含的三个 lock file 必须覆盖同一候选包内容。
5. 上传五个基础包及符号包，等待公共源可读取。使用 NuGet.org-only 配置和独立缓存重新生成模板锁文件；NuGet 仓库签名会改变包哈希，不能把本地未签名包的 contentHash 直接作为最终锁文件。
6. 同步三个公共源锁文件后打包最终 Templates 3.4.1。独立 hive 安装并验证最终 nupkg，再上传相同文件；最后从公共源安装模板、locked-mode 还原、构建、测试和打包。
7. 下载公开包核对 nuspec 版本及依赖，并逐 ZIP 条目比对上传内容（仅允许 NuGet 添加 `.signature.p7s`）。保存实际回执及验证摘要，提交文档与最终锁文件。

API token 仅通过当前进程的 `NUGET_API_KEY` 环境变量传递给 NuGet，不写入源码、配置或发布记录；不使用 `--skip-duplicate` 隐藏版本冲突。

## 验证与交付边界

- 新增政策测试保护六个包共用一个版本、模板四个直接自有依赖精确锁定、生成 manifest 的最低 SDK 一致。
- 原 V9 开发结果继续保存在 [开发验收](development-acceptance.md)；本次更改后的实测结果单独追加，不覆盖旧提交证据。
- 不执行 AIFLOW、Windows CI 或 Host `seal`；它们不属于这次 NuGet 包上传。Host 的真实 Windows 拖拽、多屏、原生视频及外部业务验收仍需在 Host 发布前完成。
- NuGet 版本不可覆盖；若发布后发现缺陷，应统一追加新修订版本并重新生成公共源锁文件，不能重新推送不同的 3.4.1 内容。

## 本次实测结果

待验证与上传完成后填写。
