# V6.1 公共图标与专属注册实施验收

日期：2026-09-11。基线：`cc41a2e` 加工作树。状态：实现、本地验收、NuGet 发布与公开源消费复验均完成。
对应 [设计方案](../../design/host-v6.1-extensible-icon-contributions-plan.md) 与 [开发说明](../../quick-start/plugin-icons.md)。

## 授权与范围

用户随后明确授权按设计实现、编译并发布 NuGet 包。本次不使用 AIFLOW、不运行 Windows CI、Windows Smoke 或
Host `seal`；没有修改外部业务仓库。V6.1 为改造顺序，Host 产品版本继续为 `3.0.0`。
提供的发布凭据只用于本次 NuGet 提交，不写入文档、源码或制品。

开始时存在 17 个已修改 lock 文件及先前 V6.1 设计文档改动。变更前的追踪文件已备份至
`artifacts/v6.1/baseline`，包括 `initial.patch` 与 `status.txt`。本轮 restore 需要更新 SDK 版本与新增资源依赖，
以普通 net10.0 开发构建重新生成相关锁文件；此前 win-x64/ILLink 还原上下文保存在基线备份中。

## 实现结果

- 新增无 SDK/UI/DI 依赖的 `MyAvaloniaManagement.Icons`，迁移原八个公共键与路径，完整只读 `CommonIcons.All`。
- UI SDK 新增 13 条 API，追加至 v3 Unshipped 并保持 Ordinal 排序；所有历史 Shipped 均不改写。
- 图标参与插件局部 Builder、Seal、Import、全局 Registry 发布与候选隔离；不使用静态可写注册表。
- Workspace 创建入口保留来自注册事实的 Owner；Host 按该 Owner 校验图标引用，查询先检查可用性再使用几何缓存。
- `HostIconCatalog` 仅做只读解析，`HostIconRenderer` 负责 UI 线程解析/缓存，`HostIconView` 负责按逻辑画布绘制。
  采用直接矢量绘制而非嵌套控件，保留原点、留白、宽高比及 FillRule，裁掉画布外路径，各视图独立使用主题画刷。
- Renderer 经 ViewModel 显式注入，因此 App 资源不持有第二个 Runtime 的服务；窗口重开和多个 Runtime 互不污染。
- MyPlugTest 演示自绘信封、公共表格复制为专属引用、直接 builtin 与省略图标四种情况。
- 模板使用公共资源注册，同时让 MainView/Standalone 直接绘制相同资源；Standalone 不执行 Configure，不模拟 Host 注册成功。

## 打包修正

实际部署发现原 Build 规则使用 `MyAvaloniaManagement.*` 前缀，误把纯资源包当作共享库。
因此必须同步更新 Build `1.1.3`，仅精确放行 `MyAvaloniaManagement.Icons.dll`，其余共享库限制保留。
NuGet 消费者使用 `ManagedPluginPrivatePackage`；本仓 ProjectReference 通过已有 `ManagedPluginAsset` 部署扩展点声明 DLL。
图标包不加入 Host 共享程序集根，私有资源类型不进入 SDK public API。

实际交付：Core/UI `3.4.0`、Icons `1.0.0`、Build `1.1.3`、Templates `1.4.1`。
Workflow `1.0.0` 与 Host 产品版本不变。候选制品、哈希、公开消费及最终验证结果如下。

## 已发现并修复的问题

- 坏路径解析实际抛出 `InvalidDataException`，已纳入图标降级并增加真实解析负例。
- 旧测试固定 Core/UI `3.3.0` 和 Unshipped 数量，已按本次明确升级调整，保留历史签名与接口形状约束。
- 按几何包围盒绘图会破坏非正方形画布；新增 Skia 像素测试验证 40×20 画布在 100×100 区域中的留白和缩放。
- 两个真实私有资源程序集夹具（1.0.0.0 / 1.1.0.0）与 Host 版本共存；新版夹具提供 Host 未知图形并提交共享 SDK 数据。
  夹具不可打包发布，不冒充正式 Icons 版本。

## 最终本地验证

使用 .NET SDK `10.0.302`，Release 构建按现行 Gate 的 `-warnaserror -m:1` 执行，零警告、零错误。
SDK 历史 API Shipped 未修改；UI v3 Unshipped 从 66 增加为 79 条并通过兼容性分析与排序检查。
项目单独并行构建曾触发既有多属性 ProjectReference 的输出目录竞争，最终验证使用正式入口的串行构建策略。

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release
```

最新 [Gate 结果](../../../artifacts/gate/20260911-023642-cc41a2ef0755/summary.json) 为全部通过：

| 套件 | 通过 | 失败 / 跳过 |
| --- | ---: | --- |
| SDK（含公共资源与图标契约） | 91 | 0 / 0 |
| Host Unit | 350 | 0 / 0 |
| Host Plugin | 207 | 0 / 0 |
| Host UI（含 Skia 像素与 V6 交互） | 79 | 0 / 0 |
| MyPlugTest | 11 | 0 / 0 |
| 真实 MyPlugTest ZIP 加载验收 | 1 | 0 / 0 |
| Gate 工具自身测试（另行运行） | 46 | 0 / 0 |

正式 verify 共 **739** 项，Gate 自身另 **46** 项。没有缺少 ZIP 后返回的伪通过，也没有降低门槛或排除图标代码。
已发布 SDK/UI `3.3.0` NuGet 编译的旧插件，通过真实 Loader 和 Provider 组合后仍可使用 `builtin:table`。

覆盖率按当前配置采集 Unit、Plugin、UI 与真实包验收四份报告，只包含 `MyAvaloniaManagement` Host 程序集。
Plugin 份采用增补旧二进制兼容测试后的 207 项结果，其余三份对应相同 Host 实现。
[合并报告](../../../artifacts/v6.1/coverage-final/merged/Cobertura.xml)：行 **87.38%**，分支 **71.27%**；
对应门槛 **84.39% / 70.58%**，均通过。SDK 和 Icons 行数没有混入 Host 指标。

图像验收文件位于 [screenshots](../../../artifacts/v6.1/screenshots)：旧目录、新树与功能中心的浅/深色，
以及真实模板 MainView 在 Standalone App 下的预览。100×100 像素断言覆盖非方形画布、留白、越界裁切、
EvenOdd/NonZero 与路径 F0/F1 优先级、独立红蓝画刷；坏路径去重诊断、可用性撤回、Runtime 缓存隔离均通过。

## 独立消费与包边界

所有生成项目位于仓库外的任务临时目录，路径记录在 [consumer-root.txt](../../../artifacts/v6.1/consumer-root.txt)。
通过独立 template hive 安装，不修改开发者默认模板环境。

1. 基础候选 feed 的四个实际 nupkg 还原后，逐个核对消费缓存与候选 SHA-256 一致。
2. 生成 DemoPlugin，编译 Plugin/Standalone/Tests，4 个模板测试通过；真实 Standalone App + MainView 的
   Headless Skia 探针验证路径、画布、主题画刷和图像，见 [预览日志](../../../artifacts/v6.1/preview-probe.log)。
3. 实际调用 Build `1.1.3` 的 `BuildManagedPluginPackage`，生成的 ZIP 恰含入口 DLL、PDB、deps、manifest
   和私有 Icons DLL 五个文件，不含共享 SDK DLL。
4. 通过真实部署 Target 验证 Host DLL、UI SDK DLL、伪扩展 `MyAvaloniaManagement.Icons.Other.dll`
   及子目录中的 Core SDK DLL 均按共享库规则拒绝，见 [四个负例](../../../artifacts/v6.1/forbidden-assets-final.log)。
5. 基础包发布后使用仅含 NuGet.org 的配置和新的全局缓存，重新还原、编译、测试，4 项通过；
   没有候选 feed 兜底。随后将真实公开还原的三个 lock 文件同步回模板源码。
6. 打包 Templates `1.4.0`，从该 nupkg 安装生成点分名称 Preview.V61，锁定还原、Release 零警告编译、
   4 个测试及真实 ZIP 均通过，随后提交同一份模板字节。
7. Templates `1.4.1` 本地 nupkg 安装生成 Patch.V61，锁定还原、Release 编译与 4 项测试通过后提交。
8. 最终使用 NuGet.org 公开源安装 `MyAvaloniaManagement.Plugin.Templates@1.4.1`，在独立 hive 中生成
   Public.V61；三个项目锁定还原、Release 零警告编译、4 项测试与真实 ZIP 全部通过。
   [完整日志](../../../artifacts/v6.1/public-template-final.log) 与
   [公开安装诊断日志](../../../artifacts/v6.1/public-template-install-diagnostic.log) 可复验整个过程。
   ZIP 的 [五项文件清单](../../../artifacts/v6.1/public-template-zip-entries.txt) 确认私有 Icons DLL 存在、
   共享 SDK DLL 不存在；ZIP SHA-256 为 `2ECB8A73FE44337A2DBF5971FD810675F1B14D27F43899054D8E02484F5946A5`。

### 模板安装语法更正

首次公开安装的“包不存在”被错误归因于 `@` 分隔符，导致额外发布了仅调整版本与文档的 `1.4.1`。
后续 `::` 同样一度失败，而稍后的 `@1.4.1` 公开安装成功，说明该错误不能证明分隔符不受支持；
发布后索引或缓存的短暂差异是可能原因，未进一步确定服务端成因。
[微软 CLI 文档](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-install) 明确说明：
从 .NET SDK 9.0.200 起推荐 `@`，`::` 已弃用但仍兼容。
当前仓库快速开始和包 README 已恢复推荐语法；已发布 `1.4.1` 内的 `::` 示例仍可使用，
本次不为此再次升版，不覆盖已上传制品。`1.4.0` 与 `1.4.1` 的生成代码、依赖与锁文件一致。

## NuGet 发布

发布只上传经过验收的 nupkg 与已有 snupkg，没有重新编译替换已签署候选，也没有使用跳过重复来掩盖冲突。
Core/UI/Icons 三份符号包与四个基础主包均得到 Created；Templates 两个版本的主包亦得到 Created。
日志见 [基础包](../../../artifacts/v6.1/publish-foundation.log)、[模板 1.4.0](../../../artifacts/v6.1/publish-template.log)
与 [模板 1.4.1](../../../artifacts/v6.1/publish-template-patch.log)。

| 包 | 版本 | 候选 nupkg SHA-256 |
| --- | --- | --- |
| [PluginSdk](https://www.nuget.org/packages/MyAvaloniaManagement.PluginSdk/3.4.0) | 3.4.0 | `7CAAE2FA7D1E778C2DB3FE858A02C04E8EB7A030020636D0A6D811CD2775FC42` |
| [PluginSdk.UI](https://www.nuget.org/packages/MyAvaloniaManagement.PluginSdk.UI/3.4.0) | 3.4.0 | `34A070509FA830C6E1AD8F0F658E3B862E11F2072AD2D8EC2D27F9E8CE4D2EFD` |
| [Icons](https://www.nuget.org/packages/MyAvaloniaManagement.Icons/1.0.0) | 1.0.0 | `3A34CF53CF2C9D9E4341BD3F12237652886C2BF85B4E5FF868FDBD3D6935969D` |
| [Plugin.Build](https://www.nuget.org/packages/MyAvaloniaManagement.Plugin.Build/1.1.3) | 1.1.3 | `1DD984F9F0D15794752274A701A8D0CE2B7B02BB1F51E16E303A799948A312BA` |
| [Plugin.Templates](https://www.nuget.org/packages/MyAvaloniaManagement.Plugin.Templates/1.4.0) | 1.4.0 | `30669C0452FEF26FFE85E1C7194A7514ABD9C978A5E80C8C638D1F646374FF5F` |
| [Plugin.Templates（最终版本）](https://www.nuget.org/packages/MyAvaloniaManagement.Plugin.Templates/1.4.1) | 1.4.1 | `57D6FF840EC2C46873E1177E59181762CFF8E52D1278591DF7286DEE5EB5654A` |

NuGet 添加仓库签名后，公开 nupkg 的整体哈希与原始候选不同。验证逐项比较 ZIP 内容，
仅排除新增 `.signature.p7s`；程序集、XML、README、nuspec、构建脚本等全部一致。
[公开载荷核对](../../../artifacts/v6.1/public-payload-checks.json) 保存两种整体哈希与内容核对结果。
以上六个已发布版本均已公开下载，逐项载荷一致；五个包 ID 的最终版本已通过纯公开源消费，
没有候选 feed 或项目引用兜底。发布后的仓库安装说明更正不属于上述已上传制品内容。

## 验收边界

本轮没有运行 Windows CI、Windows Smoke、Host seal、真实 Windows 桌面人工操作或 Host 产品发布，
因此 Gate 中 `host.publishable=false` 与本次 NuGet 包已获授权发布并不矛盾。
渲染证据来自真实 Avalonia/Skia Headless；外部业务插件未批量迁移，也未提交 Git commit/tag。
旧版目录切换只改变展示，不能把依赖 SDK 3.4.0 的新插件变成旧 Host 可加载插件。
