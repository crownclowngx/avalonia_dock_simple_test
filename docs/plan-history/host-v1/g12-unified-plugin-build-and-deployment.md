# G12：统一插件构建、部署与独立发布

> 完成日期：2026-08-20
> 状态：已完成
> 支持平台：Managed Plugin v1 / Windows x64
> 证据入口：`scripts/Test-ManagedPluginPackages.ps1 -Configuration Release`

## 1. 结果与发布模型

G12 把四个插件重复维护的部署 Target 收敛为一份声明式协议，但没有把四个插件捆成一个发布单元。
每个插件仍拥有独立的稳定身份、版本和兼容区间，并分别生成：

```text
<AssemblyName>-<PluginVersion>-win-x64.zip
<AssemblyName>-<PluginVersion>-win-x64.manifest.json
```

ZIP 内只允许出现一棵 `Controls/<PluginFolder>/`。外置 `.manifest.json` 记录 ZIP 摘要和每个条目的
路径、长度、SHA-256；ZIP 内只有宿主运行时读取的 `plugin.manifest.json`，不再放第二套发布清单。
因此四个插件可按各自节奏独立构建、签名、上传和回滚。

开发构建与正式包复用同一资产集合：直接 `dotnet build <插件项目>` 仍默认清理并部署当前插件目录；
`SkipPluginDeploy=true` 完全关闭部署；打包脚本用 `ManagedPluginDeployRoot` 指向空临时根，不触碰真实 Host。

## 2. 构建接口

根级 `Directory.Build.targets` 只为声明 `ManagedPlugin=true` 的项目导入公共 Props/Targets。插件项目只声明：

| 声明 | 责任 |
| --- | --- |
| `ManagedPluginId` | 跨清单、注册与诊断保持不变的稳定身份 |
| `PluginVersion` | 插件、程序集和包的唯一版本事实 |
| `ManagedPluginDirectoryName` | `Controls` 下的独占目录名 |
| 四个兼容区间属性 | Host API 与 Common Contract 的左闭右开承诺 |
| `ManagedPluginRuntimeIdentifier` | v1 固定为 `win-x64`，通常使用公共默认值 |
| `ManagedPluginPrivatePackage` | 插件拥有并需要部署的 NuGet 运行时资产 |
| `ManagedPluginAsset` | 显式文件及其插件内目标相对路径 |
| `ManagedPluginAssetDirectoryRelativePath` | 构建期生成的完整目录树，例如 LibVLC x64 |

公共 Props 从 `PluginVersion` 派生 Version/FileVersion/InformationalVersion/AssemblyVersion 和默认部署根。
公共 Targets 只校验声明、生成严格 schema v1 清单、收集资产并复制当前插件目录。ZIP 编排、哈希和
发布证据由 PowerShell 处理，避免让 MSBuild 同时承担发布工作流。

## 3. 资产矩阵

| 插件 | 私有资产 | 特殊边界 |
| --- | --- | --- |
| BiliDownloader | Flurl、SQLite、protobuf-net、QRCoder | SQLite 原生库只保留 `runtimes/win-x64` |
| DaTangAccountingHelpPlug | EPPlus 及其私有运行时闭包 | 不复制 Host/UI/SDK 共享程序集 |
| MyPlugTest | EPPlus、Flurl 及其私有运行时闭包 | 与 DaTang 独立发布，不共享插件目录 |
| MySmallTools | LibVLCSharp 托管桥接、完整 LibVLC x64 树 | 保留 `native/win-x64/libvlc` 全树并包含 PDB |

所有插件自动包含入口 DLL、同名 `.deps.json`、PDB 和生成清单。Host、Plugin SDK、Avalonia、Dock、
Semi、Ursa、CommunityToolkit、Microsoft.Extensions 与其他宿主共享闭包不得进入插件包。

## 4. SOLID 与朴素设计取舍

- **SRP**：Props 管默认值和版本映射；Targets 管构建资产；单插件打包脚本管 ZIP；专项脚本只管业务探针。
- **OCP**：新增插件通过属性和 Item 扩展，不修改公共 Target 的插件名分支；当前四插件的资产哨兵仅存在于测试门禁。
- **ISP**：项目只声明自己需要的私有包或目录树，不被迫实现统一的业务发布接口。
- **DIP**：专项发布入口依赖通用打包命令及外置清单，而不是复制其暂存和压缩实现。

实现刻意只使用 MSBuild 内置任务、PowerShell 和 `ZipArchive`。没有自定义 Task 程序集、打包框架、
反射式插件策略或多层抽象工厂。固定条目时间、稳定排序和固定构建槽足以解决当前确定性问题，继续增加
框架只会扩大维护面。

## 5. 失败语义与安全边界

以下情况构建或包门禁立即失败并给出中文诊断：缺少身份/版本/兼容区间，非法目录名，区间反转，
非 win-x64 RID，缺少 DLL/deps/PDB，文件或目录资产不存在，目标路径绝对化或包含 `..`，路径重复或
大小写冲突，携带宿主共享程序集，以及混入其他 RID 原生资产。

部署前只删除 `ManagedPluginDeployRoot/<当前目录名>`；不会删除 `Controls` 根或兄弟插件。打包和矩阵
脚本的递归清理均限制在系统 Temp 或仓库 `artifacts` 下。ZIP 验证从最终压缩包重新解压、计数和复算
摘要，不信任打包前 staging。

## 6. 专项发布入口

`Release-BiliDownloaderP0.ps1` 继续保留联网、ffmpeg、Range 恢复、敏感扫描和宿主加载门禁；
`Release-MySmallToolsP0.ps1` 继续保留 LibVLC 部署探针、流式内存和真实播放门禁。两者只把最终 ZIP、
哈希和通用布局委托给 `Build-ManagedPluginPackage.ps1`，专项 acceptance JSON 继续位于 ZIP 外。

## 7. 自动化证据

下节由 `scripts/Update-G12DocumentationEvidence.ps1` 从 TRX、宿主 `summary.json` 和包 `summary.json`
重写。数字是 2026-08-20 的时间点证据，不进入测试逻辑，也不会成为永久固定门槛。

<!-- G12_EVIDENCE_BEGIN -->
生成时间：2026-08-20 03:09:32Z

| 宿主测试套件 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| Unit | 168 | 0 | 0 |
| UI | 38 | 0 | 0 |
| Plugin | 146 | 0 | 0 |

宿主合计 **352** 项；行覆盖率 **80.62%**，分支覆盖率 **65.91%**；Windows Smoke：**True**。

| 独立插件包 | 文件数 | ZIP 字节数 | SHA-256 |
| --- | ---: | ---: | --- |
| myavalonia.plugin.bili-downloader | 14 | 2489608 | `AA8ED583B6A8098CD7ADCE10AF1F533701C882DE329A742D4E2A02CC4DF5090E` |
| myavalonia.plugin.datang-accounting-help | 9 | 2393273 | `7F4C1AAE553E62E5D986DCD89C5395E7773FAB653C022C2AF3FB34F7E327814F` |
| myavalonia.plugin.my-plug-test | 11 | 2387717 | `117A241AD80397CC89FE06BF9F91BED2BBD4413F159D94F85EE1AC3B2AB6AE27` |
| myavalonia.plugin.my-small-tools | 431 | 48981596 | `197A86B973110E3C9CBA3609697305AE25716DF91CD7EEA1B6FC6AD2895E3646` |

包数量 **4**；每插件隔离构建 **2** 次，摘要一致；构建契约负例 **16** 个；最终 ZIP 宿主加载：**True**。
<!-- G12_EVIDENCE_END -->

额外门禁：Plugin SDK 正反向包消费通过；BiliDownloader 本地候选（跳过联网）和 MySmallTools 本地候选
（跳过真实播放）均完成统一包复验及部署探针。跳过项使候选明确不可发布，不冒充正式发布证据。

## 8. 回滚边界

单插件开发构建可用 `SkipPluginDeploy=true` 暂停目录部署，发布方也可只回滚某一个独立 ZIP。
允许回滚公共 Target 的实现，但必须保留声明接口、单目录清理、严格清单、共享依赖排除和包门禁；
不能恢复四份复制 Target 后仍宣称 G12 通过。G12 没有改变 Plugin SDK public API、宿主加载协议、
插件业务逻辑、插件版本节奏或预发布数据边界。
