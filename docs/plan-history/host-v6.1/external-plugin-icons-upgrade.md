# V6.1 现有插件图标同步升级验收

日期：2026-09-11。状态：十个外部插件实现、编译、测试、真实 ZIP、Host 图标验收及指定目录部署完成。
基于已发布的 Core/UI SDK `3.4.0`、Plugin.Build `1.1.3` 与 Icons `1.0.0`。
Host 生产代码与公共 SDK/资源包本轮无需继续改动；改造发生在相邻的十个独立插件仓库。

## 交付范围

- 51 个 Document、2 个 Tool 和 2 个创建意图；其中下载 Document 的两个意图投影为独立入口，共 52 个插件创建入口。
- 43 个专属矢量注册，逻辑画布 20×20；公共图标用于表格、分析、文本审核、下载、图像、目录和视频语义。
- 6 个仓库使用公共资源；另外 4 个全部采用专属图形，不引用未使用的资源包。所有原始业务 ID、Group、排序、命令与功能保持不变。
- 实际执行模块组合的 Standalone/测试注册器提供本次实例图标记录，不添加可写全局目录。
- 每个插件提高一个修订版本，以区分安装包；最低 SDK 为 3.4.0、上界为 4.0.0，Workflow SDK 仍为 1.0.0。

## 逐仓结果

| 插件仓库 | 插件版本 | 公共资源包 | 专属图标 | 完整测试 | ZIP 文件数 |
| --- | --- | --- | ---: | ---: | ---: |
| [myavalonia-baidu-netdisk](../../../../avalonia_dock_plug_test/myavalonia-baidu-netdisk/docs/plan-history/v6.1-plugin-icons.md) | 1.0.0 → **1.0.1** | 无需引用 | 2 | 209 | 7 |
| [myavalonia-bili-downloader](../../../../avalonia_dock_plug_test/myavalonia-bili-downloader/docs/plan-history/v6.1-plugin-icons.md) | 3.0.0 → **3.0.1** | Icons 1.0.0 | 2 | 748 | 15 |
| [myavalonia-classic-game](../../../../avalonia_dock_plug_test/myavalonia-classic-game/docs/plan-history/v6.1-plugin-icons.md) | 1.1.0 → **1.1.1** | 无需引用 | 14 | 556 | 4 |
| [myavalonia-datang-work](../../../../avalonia_dock_plug_test/myavalonia-datang-work/docs/plan-history/v6.1-plugin-icons.md) | 3.0.0 → **3.0.1** | Icons 1.0.0 | 1 | 76 | 10 |
| [myavalonia-fractal-art](../../../../avalonia_dock_plug_test/myavalonia-fractal-art/docs/plan-history/v6.1-plugin-icons.md) | 1.0.0 → **1.0.1** | 无需引用 | 1 | 270 | 4 |
| [myavalonia-image-lab](../../../../avalonia_dock_plug_test/myavalonia-image-lab/docs/plan-history/v6.1-plugin-icons.md) | 1.0.0 → **1.0.1** | Icons 1.0.0 | 17 | 803 | 5 |
| [myavalonia-layer-unpack](../../../../avalonia_dock_plug_test/myavalonia-layer-unpack/docs/plan-history/v6.1-plugin-icons.md) | 1.0.0 → **1.0.1** | Icons 1.0.0 | 2 | 513 | 10 |
| [myavalonia-video-security-player](../../../../avalonia_dock_plug_test/myavalonia-video-security-player/docs/plan-history/v6.1-plugin-icons.md) | 3.1.0 → **3.1.1** | Icons 1.0.0 | 3 | 238 | 432 |
| [myavalonia-workflow-studio](../../../../avalonia_dock_plug_test/myavalonia-workflow-studio/docs/plan-history/v6.1-plugin-icons.md) | 1.2.0 → **1.2.1** | 无需引用 | 1 | 88 | 4 |
| [myavalonia-xiancai-business-worker](../../../../avalonia_dock_plug_test/myavalonia-xiancai-business-worker/docs/plan-history/v6.1-plugin-icons.md) | 1.2.0 → **1.2.1** | Icons 1.0.0 | 0 | 121 | 10 |

## 验证依据

十个解决方案的普通还原、锁定还原、Release `-warnaserror -m:1` 编译与全部测试通过：**3622 项**，无失败、跳过。
其中新增 20 项图标契约测试用哨兵返回值检查模块没有手写引用或复用前次注册状态。
三个现有 Host 联调入口另 **33 项**通过：Bilibili 3、大唐 20、视频及工作流组合 10。
日志和 TRX 索引见 [机器可读汇总](../../../artifacts/v6.1/plugin-upgrade/summary.json)。

真实 ZIP 经生产 Host Loader 和 Provider 组合：10 个插件、51 个 Document、2 个 Tool、43 个专属声明均完整。
使用生产 Workspace 与 HostIconRenderer 后，52 个插件创建入口均可解析，全部专属几何没有默认回退。
见 [Host 验收数据](../../../artifacts/v6.1/plugin-upgrade/host-icons-verification.json) 与
[联调日志](../../../artifacts/v6.1/plugin-upgrade/host-probe.log)。

图像：[专属图标图集](../../../artifacts/v6.1/plugin-upgrade/custom-icon-atlas.png)、
[浅色入口](../../../artifacts/v6.1/plugin-upgrade/host-plugin-icons-light.png)、
[深色入口](../../../artifacts/v6.1/plugin-upgrade/host-plugin-icons-dark.png)。

## 本轮修正的兼容点

1. 公共资源 DLL 作为 `ManagedPluginPrivatePackage` 显式部署，使用公共资源的 6 个插件实际 ZIP 均含该 DLL；纯专属插件不携带它。
2. Bilibili、大唐、视频的旧联调脚本曾按 MyAvaloniaManagement 前缀误拦截图标包，已改为精确放行 Icons，保留其余共享 DLL 限制。
3. Bilibili、大唐的联调 ZIP 文件名改为读取项目版本；视频与工作流的联调版本断言按本次版本同步。
4. ImageLab 旧四项 NuGet 白名单更新为明确的五项；原先三处仅计数断言提升为精确包名检查。
5. 独立预览原有注册器也会执行真实 Module，必须同步实现可选图标能力；本轮修改了 18 个预览/测试注册源文件。

## 验收与交付边界

百度网盘、Bilibili、分形声明了生命周期。联合图标探针没有执行登录、下载恢复或文件清理，
而是在内存测试 Runtime 中设置 Ready 作为展示前提。未就绪时为 48 个插件创建入口，就绪后为 52 个；
Host 自带入口单独排除计数。此测试状态不写入用户的 Host 配置，也不替代真实生命周期启动验收。

所有外部仓库开始时均干净。本轮不使用 AIFLOW，不运行 Windows CI、Windows Smoke、Host seal 或线上发布门禁。
本轮编译插件本地 ZIP，并按用户后续明确要求覆盖发布到 `D:\data\avalonia\Controls`；不再次发布 NuGet。
用户随后要求提交 Git，十个插件仓库的实现分别提交，Host 仓库归档整体验收，记录见下文。
使用新版 ZIP 时，Host 必须提供 SDK 3.4.0 契约；新版树和功能中心显示声明图标，旧版目录继续显示默认图标。

## 指定目录部署记录

2026-09-11，按用户“同步发布到 D:\data\avalonia\Controls，重复文件直接替换”的要求，
将上述十个已完成 Release 编译及验收的插件包内容部署到对应插件目录。

- 共部署 **501 个文件**：覆盖同名文件 **495 个**，新增公共图标资源 DLL **6 个**。
- 部署前校验全部 ZIP 与解压文件，部署后逐个核对文件长度和 SHA-256，全部与包清单一致。
- 十个安装清单的插件 ID、目标版本及最低 SDK `3.4.0` 全部符合逐仓结果。
- 按文件清单覆盖，不清理目标目录额外内容；原有 `MyPlugTest` 目录及其他非本轮文件未修改。
- 本次验证安装文件，不触发用户 Host 的业务生命周期。重新启动 Host 后加载新版插件；在新版树或功能中心查看声明图标。

完整版本、文件哈希和部署时间见 [部署验收记录](../../../artifacts/v6.1/plugin-upgrade/deployment-controls.json)。

## Git 提交记录

以下十个独立仓库分别提交实现、相关依赖锁文件、契约测试和专属文档，提交说明包含版本、验证与部署结果。
Host 仓库同步提交文档导航与本验收记录。插件 ZIP 在提交前已完成构建及部署，
构建时的 `sourceRevision` 和下表之后列出的 ZIP 哈希保留原始值；本节记录随后创建的源码提交。

| 插件仓库 | 提交 | 标题 |
| --- | --- | --- |
| myavalonia-baidu-netdisk | `a3803ae` | feat(icons): 为百度网盘和传输中心注册专属图标 |
| myavalonia-bili-downloader | `156024f` | feat(icons): 接入 Bilibili 公共下载与专属功能图标 |
| myavalonia-classic-game | `21457d2` | feat(icons): 为经典游戏注册 14 个专属矢量图标 |
| myavalonia-datang-work | `fa82e59` | feat(icons): 为大唐票据与对账接入语义图标 |
| myavalonia-fractal-art | `c5b314b` | feat(icons): 为分形艺术注册专属矢量图标 |
| myavalonia-image-lab | `f1c9863` | feat(icons): 为 ImageLab 接入公共与算法专属图标 |
| myavalonia-layer-unpack | `a1e2a70` | feat(icons): 为归档浏览与打包解包接入语义图标 |
| myavalonia-video-security-player | `bee661d` | feat(icons): 为视频播放与加解密接入语义图标 |
| myavalonia-workflow-studio | `10952ff` | feat(icons): 为工作流编辑器注册专属图标 |
| myavalonia-xiancai-business-worker | `561dfc5` | feat(icons): 为闲才业务入口接入公共语义图标 |

## 本地插件包

[全部十个插件包](../../../artifacts/v6.1/plugin-upgrade/external-plugins-v6.1.zip) 是分发集合；先解压集合，
再分别安装其中的插件 ZIP。它自身不是单插件安装包。

| 插件包 | SHA-256 |
| --- | --- |
| [BaiduDiskPlugin.Plugin-1.0.1-win-x64.zip](../../../artifacts/v6.1/plugin-upgrade/myavalonia-baidu-netdisk/packages/BaiduDiskPlugin.Plugin-1.0.1-win-x64.zip) | `800119A5305FDD6F60E39B7049244F4B4ECBF99EA6B2640FF4DA1AB1227315C8` |
| [BiliDownloader-3.0.1-win-x64.zip](../../../artifacts/v6.1/plugin-upgrade/myavalonia-bili-downloader/packages/BiliDownloader-3.0.1-win-x64.zip) | `FF062D28B337AC8A13E84B886B0A6CCA1C25557186798266D5C01B48F44C5165` |
| [ClassicGamePlugin.Plugin-1.1.1-win-x64.zip](../../../artifacts/v6.1/plugin-upgrade/myavalonia-classic-game/packages/ClassicGamePlugin.Plugin-1.1.1-win-x64.zip) | `1B90E400DBB739295A9C10A280AD78BD243B4D7A14DF930952285ED78F78BFE2` |
| [DaTangAccountingHelpPlug-3.0.1-win-x64.zip](../../../artifacts/v6.1/plugin-upgrade/myavalonia-datang-work/packages/DaTangAccountingHelpPlug-3.0.1-win-x64.zip) | `75AE4D5C56CA3CDF4C1C2CC02A19A9F2C8C9726AF699E10BF0A04BA4D02896C2` |
| [FractalArtPlugin.Plugin-1.0.1-win-x64.zip](../../../artifacts/v6.1/plugin-upgrade/myavalonia-fractal-art/packages/FractalArtPlugin.Plugin-1.0.1-win-x64.zip) | `821C7B093EE3D0FF76B3C8FEFB87EABE3848A5CA52C4F770E7F4B907E151DD46` |
| [ImageLabPlugin.Plugin-1.0.1-win-x64.zip](../../../artifacts/v6.1/plugin-upgrade/myavalonia-image-lab/packages/ImageLabPlugin.Plugin-1.0.1-win-x64.zip) | `F25F907B819590816BB1CB305C2DD9585FE28E3184A7C7D663AD7EB2EC8A3711` |
| [LayerUnpackPlugin.Plugin-1.0.1-win-x64.zip](../../../artifacts/v6.1/plugin-upgrade/myavalonia-layer-unpack/packages/LayerUnpackPlugin.Plugin-1.0.1-win-x64.zip) | `B0EBE339A686F6AD1BF2883517BA25BE2348D8220A77929A135E5B0171A1801A` |
| [VideoSecurityPlayer.Plugin-3.1.1-win-x64.zip](../../../artifacts/v6.1/plugin-upgrade/myavalonia-video-security-player/packages/VideoSecurityPlayer.Plugin-3.1.1-win-x64.zip) | `AC264CCA880F751B70B5BB5EB4AAEE88F251A442FF8C3235DC9FDE4960B95047` |
| [WorkflowStudio.Plugin-1.2.1-win-x64.zip](../../../artifacts/v6.1/plugin-upgrade/myavalonia-workflow-studio/packages/WorkflowStudio.Plugin-1.2.1-win-x64.zip) | `AC6783E0CE90C0737DC446BB395C9581A1AEB0122060F7DCDA3EB8B2D607DF65` |
| [XianCaiWorkerPlugin.Plugin-1.2.1-win-x64.zip](../../../artifacts/v6.1/plugin-upgrade/myavalonia-xiancai-business-worker/packages/XianCaiWorkerPlugin.Plugin-1.2.1-win-x64.zip) | `07BC3D9F511F833895AE2164356BAEF35F586D63A23EEED7C6FA1BCF61E15FD6` |
