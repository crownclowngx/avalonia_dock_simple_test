# 阶段 3：非 UI 与插件依赖升级验收记录

## 1. 验收结论

- 完成状态：**已完成**
- 执行日期：2026-07-26（Asia/Shanghai）
- 代码验收修订：`78e63885dc0faae7a2a9009886d7313434720b0a`
- .NET SDK：`10.0.302`
- .NET Runtime：`10.0.10`
- 目标框架：`net10.0`、`net10.0-windows`
- UI 基座保持不变：Avalonia `11.3.18`、Dock `11.3.2.2`

阶段 3 的目标依赖版本已由阶段 2 提交 `4600e93` 提前写入中央包版本和锁文件。本阶段没有回退版本、重写历史或升级 Avalonia/Dock 大版本，而是通过专项兼容测试、私有部署证据和真实窗口播放门禁完成收口。

本记录所在提交是阶段 3 的文档收口提交；代码与专项测试分别由以下可独立回退的提交完成：

| 提交 | 内容 |
| --- | --- |
| `9aa39ff` | MySmallTools 托管桥接与 LibVLC 私有运行时版本、目录结构验证 |
| `a217dc7` | BiliDownloader Protobuf 固定样本、损坏输入、二维码 PNG 兼容验证 |
| `e241e64` | DaTang EPPlus 真实工作簿往返与文件句柄释放验证 |
| `78e6388` | MVVM Toolkit 生成属性和 Newtonsoft.Json 宿主数据往返验证 |

## 2. 直接依赖基线

| 范围 | 依赖 | 验收版本 |
| --- | --- | --- |
| MySmallTools | LibVLCSharp | `3.10.0` |
| MySmallTools | LibVLCSharp.Avalonia | `3.10.0` |
| MySmallTools | VideoLAN.LibVLC.Windows | `3.0.23.1` |
| BiliDownloader | Microsoft.Data.Sqlite | `10.0.10` |
| BiliDownloader | SQLitePCLRaw.bundle_e_sqlite3 | `2.1.12` |
| BiliDownloader | protobuf-net | `3.2.56` |
| BiliDownloader | QRCoder | `1.8.0` |
| DaTangAccountingHelpPlug | EPPlus | `8.6.3` |
| 公共包 | CommunityToolkit.Mvvm | `8.4.2` |
| 公共包 | Newtonsoft.Json | `13.0.4` |
| 测试基础设施 | coverlet.collector | `10.0.1` |
| 测试基础设施 | Microsoft.NET.Test.Sdk | `18.8.1` |
| 测试基础设施 | xunit | `2.9.3` |
| 测试基础设施 | xunit.runner.visualstudio | `3.1.5` |

MySmallTools 继续使用 `GeneratePathProperty="true"` 和 `PrivateAssets="all"` 管理原生包路径。托管程序集版本验证为 `3.10.0.0`；`libvlc.dll` 与 `libvlccore.dll` 的文件版本属于 `3.0.23`。NuGet 补丁版本 `3.0.23.1` 由中央版本文件和锁文件验证，不与原生 DLL 文件版本混用。

## 3. 设计约束与实现说明

- 只将 QRCoder 的纯字节编码职责从登录 ViewModel 提取到内部静态 `QrCodePngEncoder`，并仅通过 `InternalsVisibleTo` 向测试程序集开放。
- 未新增接口、工厂、仓储或 DI 注册；二维码编码当前只有一个实现，额外抽象不会改善替换性。
- 未修改 SECVID03、播放器会话、Document 创建、插件发现、消息总线或公共宿主接口。
- 未调整 PBKDF2、nonce、AAD、GCM Tag、分块大小和明文处理方式。
- 新增测试名称和关键设计注释使用中文，注释用于说明兼容边界和设计意图。

## 4. 执行命令

各分组先执行 `--force-evaluate` 恢复，再执行 locked restore、Release 严格构建和专项测试。最终门禁使用以下核心命令：

```powershell
dotnet restore .\MyAvaloniaManagement.sln --locked-mode
dotnet build .\MyAvaloniaManagement.sln -c Release --no-restore -warnaserror
dotnet test .\Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj -c Release --no-build --no-restore
dotnet test .\Plugins\BiliDownloader\BiliDownloader.Tests\BiliDownloader.Tests.csproj -c Release --no-build --no-restore
dotnet test .\Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj -c Release --no-build --no-restore
dotnet list .\MyAvaloniaManagement.sln package --vulnerable --include-transitive
```

覆盖率验证对 BiliDownloader 和宿主插件测试分别使用：

```powershell
dotnet test <测试项目> -c Release --collect:"XPlat Code Coverage"
```

G3 Windows x64 真实窗口专项门禁使用：

```powershell
dotnet run --project .\Plugins\MySmallTools\MySmallTools.Playback.IntegrationHarness\MySmallTools.Playback.IntegrationHarness.csproj -c Release -- --suite g3 --cycles 1 --dock-switches 1 --media-switches 1 --report .\artifacts\upgrade\net10\phase-3-plugins\g3-phase3-playback.json
```

## 5. 自动化结果

| 门禁 | 结果 |
| --- | --- |
| 解决方案 locked restore | 通过 |
| 解决方案 Release 严格构建 | 通过，`0` 警告、`0` 错误 |
| MySmallTools 测试 | `181/181` 通过，`0` 跳过 |
| BiliDownloader 测试 | `24/24` 通过，`0` 跳过 |
| 宿主插件测试 | `28/28` 通过，`0` 跳过 |
| 测试总数 | `233/233` 通过，`0` 跳过 |
| BiliDownloader XPlat Code Coverage | 成功生成 Cobertura 报告 |
| 宿主插件 XPlat Code Coverage | 成功生成 Cobertura 报告 |
| DaTang 独立严格构建 | 通过 |
| MyPlugTest 独立严格构建 | 通过 |
| 12 个项目漏洞审计 | 未发现已知漏洞 |

锁文件在分组恢复和最终恢复后没有非预期语义漂移。

## 6. 插件部署证据

### 6.1 MySmallTools

- 私有部署清单共记录 `428` 个文件，包含相对路径、长度、版本和逐文件 SHA-256。
- `LibVLCSharp.dll`、`LibVLCSharp.Avalonia.dll`、`libvlc.dll`、`libvlccore.dll` 和 VLC `plugins` 子目录均存在。
- 解压后部署探针结果为 `isReady: true`，问题列表为空。
- 64 MiB 与 512 MiB 内存门禁均通过，临时文件已清理，文件句柄可释放。
- 候选 ZIP：`MySmallTools-p0-win-x64-78e63885dc0f.zip`
- ZIP SHA-256：`F47F5C25A1A740B173CCA77BD5DA06642453AA5ABA4B49F4CFDB88F11C700544`

该候选包使用 `-AllowDirty -SkipPlaybackGate` 生成，只用于阶段 3 的 ZIP、Manifest、哈希和部署探针取证，因此 `publishable` 为 `false`。它不冒充 G4/G11 正式发布签字；真实播放能力由独立 G3 报告验收。

### 6.2 BiliDownloader

- 部署清单共记录 `34` 个托管与原生文件。
- SQLite、protobuf-net、QRCoder 托管程序集均存在。
- `e_sqlite3.dll` 的 3 个 RID 私有运行时副本均存在。
- 原有 SQLite 凭据加密、数据库回读和原生库加载测试继续通过。

## 7. G3 真实窗口验收

G3 报告结果为 `Success: true`，运行参数为 1 次生命周期、1 次 Dock 切换和 1 次媒体切换，覆盖播放、Seek、暂停、Stop、关闭 Document、重新创建 Document 和资源回收。

| 项目 | 实际值 |
| --- | --- |
| .NET Runtime | `10.0.10` |
| Avalonia | `11.3.18.0` |
| Dock | `11.3.2.2` |
| LibVLCSharp | `3.10.0.0` |
| 原生 LibVLC | `3.0.23.0` |
| 失败断言 | `0` |
| 最终播放器/媒体输入/加密流/缓存/原生调度资源 | 全部为 `0` |

运行期间 LibVLC 向 stderr 输出了 `imem`、缩略图裁剪、暂停预取和损坏 seekhead 等诊断噪声，但报告断言、真实播放矩阵和最终资源归零均通过。这些消息保留在原始日志中，不通过过滤伪造成无诊断输出。

## 8. 人工核对结果

- 主动文档中的生产版本已更新为 LibVLCSharp `3.10.0`、LibVLCSharp.Avalonia `3.10.0`、VideoLAN.LibVLC.Windows `3.0.23.1`。
- G3/G4/G10 历史文档、基准 JSON 和 `TestResults` 中的旧版本保持不变，避免改写历史证据。
- 工作区原有的非本阶段行尾差异没有暂存或提交；本阶段只提交实际编辑的文件。
- 未进入 Avalonia 12 或 Dock 12；后续仍由阶段 4 兼容性闸门作出 GO/NO-GO 决策。

## 9. 已知问题

1. 阶段 3 候选 ZIP 不是 G4/G11 正式可发布产物，正式发布仍需完整发布签字流程。
2. LibVLC 的非阻断诊断噪声仍会出现在真实窗口门禁日志中；当前没有资源泄漏或功能断言失败证据。
3. 仓库中阶段 3 之前形成的历史证据仍显示当时的旧版本，这是预期保留项。

## 10. 退出条件与回退点

阶段 3 的退出条件已满足：依赖版本受锁文件约束，专项兼容测试通过，测试基础设施可收集覆盖率，插件私有部署完整，真实播放门禁通过，解决方案严格构建通过，且 12 个项目无已知漏洞。

按插件回退时使用以下边界：

- MySmallTools：回退 `9aa39ff`，托管桥接、原生 LibVLC 与部署验证必须成组处理。
- BiliDownloader：回退 `a217dc7`，同时回退二维码内部帮助类及其测试。
- DaTang：回退 `e241e64`。
- 公共包兼容测试：回退 `78e6388`。
- 文档记录：回退包含本文件的阶段 3 文档收口提交。

完整原始日志、覆盖率、包图、部署清单、候选 ZIP、Manifest、SHA-256、部署探针和 G3 报告位于忽略目录：

```text
artifacts/upgrade/net10/phase-3-plugins/
```
