# V9：发布后的生成物清理记录

日期：2026-09-16。用户要求在 NuGet 上传和验证完成后清理项目人工制品；本记录对应主仓与 `avalonia_dock_plug_test` 下 11 个插件仓库。

## 结果

- 首次盘点的 **191 个生成目录、92,925 个文件、72.414 GiB** 已全部清除。
- 另清理 25 个零散测试报告、约 308.43 MiB 的历史测试/旧帮助页开发输出，以及 5 个临时模板工程（2.823 GiB）。
- 按已盘点文件逻辑大小合计，清理量至少约 **75.54 GiB**。这不是磁盘空闲空间的精确差值；首次盘点之后新增的公共源缓存未重复估算。
- 最终扫描：所有原目标目录不存在，项目内匹配的 Git 忽略生成文件为 **0**。主仓没有源码删除/修改，11 个外部仓库的 Git 状态与清理前完全一致，原有 `.gitignore` 修改与 ClassicGame 研究文档保留。

| 项目 | 首次盘点目录数 | 文件数 | GiB |
| --- | ---: | ---: | ---: |
| avalonia_dock_simple_test | 67 | 58,879 | 35.823 |
| myavalonia-baidu-netdisk | 6 | 307 | 0.667 |
| myavalonia-bili-downloader | 15 | 1,595 | 3.044 |
| myavalonia-classic-game | 7 | 540 | 1.167 |
| myavalonia-datang-work | 14 | 1,421 | 2.882 |
| myavalonia-fractal-art | 11 | 818 | 1.703 |
| myavalonia-image-lab | 6 | 622 | 2.222 |
| myavalonia-layer-unpack | 13 | 5,525 | 4.796 |
| myavalonia-novel-generate | 14 | 3,173 | 4.313 |
| myavalonia-video-security-player | 19 | 14,100 | 7.274 |
| myavalonia-workflow-studio | 9 | 4,609 | 5.741 |
| myavalonia-xiancai-business-worker | 10 | 1,336 | 2.784 |

## 范围与保护

清理 `bin`、`obj`、`artifacts`、未跟踪的 TestResults 输出、构建缓存和 `tools/help-assets/node_modules`。HelpWeb/vendor 中额外清理的是其 `.gitignore` 明确标注为早期完整 npm 拷贝残留的开发文件，133 个已跟踪正式资源保留。

每个目录删除前都核对解析后的绝对路径在指定仓库内、没有已跟踪文件并由 Git 忽略；父级不允许目录联接跳出边界，子级联接只移除链接。混合 TestResults 与 vendor 目录先审阅 `git clean -ndX`，再限定到三个具体路径执行 `git clean -fdX`，由 Git 保护正式文件。

没有改动源码、清单、锁文件、Git 历史、现有未提交工作、实际安装目录 `D:\data\avalonia` 或共享 NuGet 全局缓存。没有运行 AIFLOW。

## 执行中处理

- 第一遍清理被 MSBuild 常驻进程持有的 `Avalonia.Build.Tasks.dll` 阻挡。执行正常的 `dotnet build-server shutdown` 后成功清理剩余缓存，没有结束用户 Host。
- VLC 历史测试输出含长路径；仅对该次 Git 命令设置 `-c core.longPaths=true` 后清理成功，没有修改全局 Git 配置。
- 成功重试前后的源文件聚合 SHA-256 相同；首次清理使用跟踪文件排除规则，外部仓库 Git 状态另与原始盘点比对。详细路径、计数和核对口径见 [机器可读清理记录](generated-artifacts-cleanup.json)。JSON 的 `totalBytes/totalFiles` 只计成功重试时剩余删除量，不能作为全部清理总量；总规模以 `originalInventory` 与后续补扫分别理解。

## 保留的发布证据与后续构建

六个 3.4.1 nupkg 和四个 snupkg 的上传回执、源码提交、公共下载比对、哈希与测试计数已在清理前提交，见 [统一发布记录](nuget-unified-3.4.1-release.md) 和 [发布证据 JSON](nuget-unified-3.4.1-evidence.json)。历史 [V9 开发证据](development-evidence.json) 保留原始计数与哈希；原 artifacts 路径作为历史位置，不再承诺文件仍在。

下次 `dotnet restore` / `build` 或 `Gate verify` 会重新生成依赖和输出；无需重新发布已经存在的 3.4.1。外部产物验收需要重新准备完整 Controls 副本。帮助页开发依赖通过 `tools/help-assets` 的锁文件重新安装，正式运行资源仍随源码保留。

## 一项清理限制

自动审批两次拦截包含临时清理检查点删除的命令，只返回 `blocked by policy`，没有说明详细原因。为遵守限制，暂留 `C:\Users\crown\AppData\Local\Temp\myavalonia-341-cleanup-checkpoint.json`（57,459 字节，约 56.1 KiB）。它仅为本次目录盘点/哈希记录，不含 NuGet token；五个临时工程已经删除，12 个项目内部的生成物均已清理。
