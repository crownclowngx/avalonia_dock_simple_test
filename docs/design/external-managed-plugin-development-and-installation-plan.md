# 外部 Managed Plugin 开发与平台安装候选计划

> 状态：封版后候选，暂缓实施
> 当前阶段：仅保存设计方案，不代表代码、包格式或平台能力已经实现
> 审核时机：Host v1 完成封板并冻结发布产物后
> 封板关系：不计入当前 G12 验收，不阻塞 Host v1 封版
> 契约效力：本文选择均为候选决策，重新审核前不得作为正式公共契约或发布承诺

## 1. 目的与边界

本文保存 Host v1 封板之后可能开展的两项工作：

1. 把仓库内的 Managed Plugin 构建协议发布为 `MyAvaloniaManagement.Plugin.Build`，并提供
   `dotnet new` 插件模板，使仓库外开发者可以独立创建、构建和发布插件；
2. 在平台内提供单插件 ZIP 的导入、验证、安装、升级、卸载、纳管和回滚能力。

当前 G12 只负责四个仓库内插件的统一构建、开发部署和独立 ZIP，不负责第三方项目模板，也不负责
平台内安装。本文不回写 G12 的历史完成事实，不提前修改现有 ZIP、运行时加载协议或测试门禁。

第一轮候选实现仍遵守以下约束：Managed Plugin v1 只支持 Windows x64；不实现热更新、热卸载、
恶意代码沙箱、自动提权、自动重启或在线插件市场；不改变 Plugin SDK 现有 C# public API 和插件业务逻辑。

## 2. 候选交付物

### 2.1 `MyAvaloniaManagement.Plugin.Build`

新增独立 NuGet 构建包，把 G12 已验证的公共 Props、Targets 和打包脚本开放给仓库外项目：

- Props 只负责默认值和插件版本到程序集元数据的映射；
- Targets 只负责声明校验、运行清单生成、资产收集和可选目录部署；
- PowerShell 入口只负责锁定还原、隔离构建、最终校验、确定性 ZIP 和发布证据；
- NuGet 包通过 `buildTransitive` 自动导入，并以开发依赖方式引用，不进入插件运行目录；
- 仓库内四个插件与 NuGet 包复用同一份构建源码，禁止维护两套逐渐漂移的协议；
- 实现继续只使用 MSBuild 内置任务、Windows PowerShell 和 `ZipArchive`，不新增自定义 MSBuild
  Task 程序集、打包框架或按插件名称分支的业务逻辑。

对外保留的项目声明接口为：

| 声明 | 用途 |
| --- | --- |
| `ManagedPluginId` | 跨运行清单、安装回执和诊断稳定不变的插件身份 |
| `PluginVersion` | 插件、入口程序集和发布包的唯一版本事实 |
| `ManagedPluginDirectoryName` | `Controls` 下的插件目录名 |
| Host API/Common Contract 四个区间属性 | 显式声明左闭右开的兼容范围 |
| `ManagedPluginRuntimeIdentifier` | v1 固定为 `win-x64` |
| `ManagedPluginPrivatePackage` | 插件拥有并需要部署的私有 NuGet 运行时资产 |
| `ManagedPluginAsset` | 显式文件及其插件内目标路径 |
| `ManagedPluginAssetDirectoryRelativePath` | 构建期生成且需要完整携带的目录树 |

候选命令为：

```powershell
dotnet build
dotnet msbuild -t:BuildManagedPluginPackage -p:Configuration=Release
```

仓库外项目执行普通 `dotnet build` 时只生成并校验 DLL、deps、PDB 和运行清单，不猜测本机 Host
路径；只有显式提供 `ManagedPluginDeployRoot` 时才执行目录部署。仓库内项目继续由根构建配置提供
当前 Host 输出目录，以保留 G12 的开发体验。正式打包目标使用 `ManagedPluginPackageOutput` 覆盖输出目录。

打包脚本必须通过 MSBuild 的最终求值结果读取属性，不得直接解析项目 XML，也不得假设项目位于
MyAvaloniaManagement 仓库。正式打包要求已有 `packages.lock.json` 并使用锁定还原。

### 2.2 `MyAvaloniaManagement.Plugin.Templates`

新增模板 NuGet 包，提供一个短名称为 `myavalonia-plugin` 的模板。模板默认生成最小可加载模块，
通过选项增加 Document、Tool 或二者示例，避免维护多个高度重复的模板。

候选使用方式：

```powershell
dotnet new install MyAvaloniaManagement.Plugin.Templates::1.0.0

dotnet new myavalonia-plugin `
  -n ExamplePlugin `
  --plugin-id myavalonia.plugin.example `
  --sample both
```

模板参数和默认值：

- `--plugin-id` 必填，不能用可变化的显示名称代替稳定身份；
- `--plugin-version` 默认 `1.0.0`；
- `--sample` 支持 `none|document|tool|both`，默认 `none`；
- 目录名和程序集名默认从项目名称派生，并由 Build 包做最终合法性检查；
- 初始 Host API 与 Common Contract 兼容区间候选值均为 `[1.0.0,2.0.0)`，封版后按最终版本重审；
- 项目启用 NuGet 锁文件，第一次 Restore 生成 `packages.lock.json`；
- 模板不包含手写 `plugin.manifest.json`，清单由构建属性生成；
- 示例使用真实的 `IPluginModule` 和 `IPluginRegistrationContext`，所有非直观代码提供详细中文注释；
- 模板固定经过验证的 Plugin SDK、可选 UI Profile 和 Build 包版本，并维护独立兼容矩阵。

Build 与 Templates 包各自拥有版本事实，实际推送 NuGet.org 或私有包源需要单独发布授权。本候选任务
只要求产出可发布 nupkg、使用隔离本地源完成消费验证，并形成发布说明。

## 3. 候选 ZIP 与清单协议

### 3.1 两份包内清单的职责

`plugin.manifest.json` 不删除。它仍位于插件运行目录，是宿主发现插件、校验兼容区间和定位入口
程序集的必需运行时清单。缺少该文件的目录不能注册为 Managed Plugin。

为了让平台只选择一个 ZIP 就能完成安装，候选格式在 ZIP 根增加严格的 `plugin.package.json`：

```text
plugin.package.json
Controls/<PluginFolder>/
  plugin.manifest.json
  <EntryAssembly>.dll
  <EntryAssembly>.deps.json
  <EntryAssembly>.pdb
  ...
```

`plugin.package.json` 候选字段包括 schema、PluginId、插件版本、目录名、入口程序集、TFM、RID，
以及 `Controls` 下全部文件的相对路径、长度和 SHA-256。它不记录自身摘要，也不记录 ZIP 摘要，
从而避免自引用。平台在导入时计算最终 ZIP SHA-256，并把它写入安装回执。

ZIP 外的 sidecar 可以继续作为发布流水线证据，记录最终 ZIP 摘要，但平台导入不得依赖 sidecar。
用户只选择 ZIP。发布清单与运行清单中重复的身份、版本和入口字段必须完全一致。

这一格式会改变 G12 当前“ZIP 内只有 `Controls/<PluginFolder>/`”的契约，因此封版后必须单独审核
schema 和迁移方式。缺少 `plugin.package.json` 的历史 G12 ZIP 候选处理方式是给出明确旧格式错误并
要求重新打包，不进行静默猜测或自动补写。

### 3.2 包验证边界

平台只读取 ZIP、JSON、deps 和 PE 元数据，不加载或执行第三方程序集。验证至少覆盖：

- ZIP 只能包含一个 `plugin.package.json` 和一个 `Controls/<PluginFolder>/`；
- 发布清单采用严格字段集合，身份、版本、目录、入口、TFM 和 RID 必须合法；
- 运行清单采用宿主支持的严格 schema，且与发布清单和入口程序集版本一致；
- 入口 DLL、同名 deps、PDB 和清单均存在；
- 每个 payload 文件都被清单唯一列出，长度和 SHA-256 一致，不允许额外文件；
- 拒绝绝对路径、`..`、大小写冲突、Windows 保留名、备用数据流、符号链接和目标越界；
- 拒绝 Host、Plugin SDK、Avalonia、Dock、Semi、Ursa、CommunityToolkit、Microsoft.Extensions
  等共享程序集副本；
- 拒绝非 `win-x64` 原生资产和不兼容的 Host API/Common Contract 区间；
- 资源上限候选值为：ZIP 不超过 1 GiB、条目不超过 20,000、解压总量不超过 4 GiB、单文件
  不超过 1 GiB。最终数值必须结合封版后的真实最大插件包重新评审。

## 4. 平台安装与回滚候选设计

### 4.1 目录和权限

当前候选选择把活动插件继续安装到：

```text
<AppBase>/Controls/<PluginFolder>
```

事务状态与备用版本放在同一磁盘：

```text
<AppBase>/.managed-plugins/
  catalog.json
  pending/
  staging/
  alternate/
  transactions/
```

同卷目录允许使用重命名完成短暂、可恢复的版本切换。若程序目录不可写，平台仍可读取并验证 ZIP，
但安装、纳管、升级、卸载和回滚进入只读状态，并显示中文权限诊断。第一版不自动请求管理员权限，
也不静默回退到 LocalAppData。

程序目录在正式安装场景中可能由安装器保护或在产品升级时被整体替换，所以这个位置只是当前候选，
是封版后必须重新审核的高风险决策。若最终改用 LocalAppData，还必须同步设计多插件根发现、冲突
优先级、内置插件升级和目录迁移，不能只修改一个路径常量。

### 4.2 职责划分

候选实现保持朴素的 SOLID 边界：

- **包读取与验证器**只解析 ZIP、清单、摘要和兼容性，不写活动目录；
- **安装 Catalog Store**只管理路径、回执和原子 JSON，不解释 UI；
- **安装协调器**只处理版本政策、目录冲突和待处理操作；
- **启动应用器**只在插件发现前恢复事务并执行同卷目录切换；
- **UI ViewModel**只负责选择 ZIP、请求确认、提交命令和展示状态；
- 文件系统、插件 ZIP 选择器和风险确认提示使用最小接口注入，避免扩张现有文档存储接口。

这些接口只服务于有外部副作用的真实边界。校验规则使用固定、可读的顺序流程，不建设反射式规则
插件、通用工作流引擎、多层工厂或为了模式而模式的抽象。

### 4.3 导入和重启生效

运行中的宿主已经加载插件 DLL，而且当前 `PluginLoadContext` 不支持可靠卸载。因此候选流程不尝试
覆盖已加载文件或热注册：

1. 用户选择单个 ZIP；
2. 平台完成全量只读验证并计算 ZIP SHA-256；
3. 平台明确提示未签名插件拥有宿主进程内执行权限；
4. 用户确认后，平台把原始 ZIP 和待处理操作写入 `pending`；
5. 当前会话仅显示“待重启生效”，不改变 Runtime Registry；
6. 用户自行正常关闭并重新启动应用；
7. `Program` 在 `HostRuntime.Create` 和插件发现之前恢复事务、重新校验并切换目录；
8. 宿主随后按现有 `plugin.manifest.json` 和 `IPluginModule.Configure` 完成真正的运行时注册。

平台“注册”表示建立安装回执并激活目录，不新增第二套可写 Plugin Registry，也不允许运行中追加
Document、Tool、View 或服务。

### 4.4 安装事务

同一 PluginId 最多存在一个待处理操作，不同插件可以分别排队。启动应用器必须先检查整个待处理
集合的身份与目录冲突，再按稳定 PluginId 顺序逐项执行：

1. 恢复上一次未完成的事务；
2. 将 ZIP 解压到同卷 staging 并重新验证最终文件；
3. 写入带明确阶段的事务日志；
4. 更新时把当前活动目录重命名到唯一 alternate；
5. 把 staging 插件目录重命名为活动目录；
6. 原子更新 catalog 回执；
7. 标记事务完成并清理超出保留策略的文件。

任一步失败都按日志恢复旧目录。若无法证明活动目录是完整新版本或完整旧版本，必须终止宿主启动，
不能在状态不明确时继续执行插件代码。Catalog 回执记录 PluginId、版本、目录、ZIP 摘要、完整文件
摘要、活动/备用版本、操作类型和待重启状态。

### 4.5 版本、卸载与单版本回滚

- 同版本、同 ZIP 摘要视为已安装，不重复覆盖；
- 同版本、不同摘要直接拒绝，保证同一发布版本不可变；
- 普通导入只允许升级，降级必须选择平台保留的备用版本；
- 每个插件只保留一个备用版本；新升级成功后删除更早的备用版本；
- 回滚交换活动版本和备用版本，因此允许撤销一次回滚；
- 卸载在重启时把活动目录移入备用区，允许恢复最近一次卸载；
- 新安装没有旧版本时，激活失败的恢复动作是移除该插件；
- 所有改变活动目录的动作均在下一次完整启动、插件发现之前执行。

如果新版本导致清单发现、程序集加载或 Host 组合阶段致命失败，平台记录关联 PluginId 和激活失败
状态。最小启动失败窗口可以安排恢复备用版本；用户下次手工启动时执行恢复。非致命的插件业务
生命周期失败不自动回滚，由插件管理页展示并允许用户主动选择。

### 4.6 手工复制插件的纳管

平台扫描到包含有效运行清单、但没有 catalog 回执的 `Controls` 目录时，把它标记为“未纳管插件”：

- 未纳管插件仍按当前宿主规则发现和加载；
- 平台只读展示其来源和运行状态，默认不能升级或回滚；
- 用户选择“纳管”后，平台重新校验目录、显示未签名代码警告，并为当前文件生成回执；
- 纳管时没有备用版本，首次升级或卸载后才产生回滚点；
- 导入 ZIP 与未纳管目录完全一致时，提示用户直接纳管；
- PluginId 相同但内容不同、目录名冲突或身份不一致时拒绝覆盖，要求先处理原目录；
- 平台每次启动校验已纳管活动目录与回执的摘要，发现外部篡改时不得把变化冒充成已验证发布物。

## 5. UI 候选范围

保留现有插件状态工具的稳定 ID，把界面扩展为“插件管理”容器；运行时状态和安装状态由两个独立
子模型提供，避免让安装事务污染现有生命周期模型。候选操作包括：

- 导入单个 ZIP；
- 纳管手工目录；
- 安排升级、卸载或回滚；
- 打开活动目录；
- 刷新安装与运行状态；
- 展示当前版本、唯一备用版本、来源、摘要状态和是否需要重启。

每次导入或纳管都必须明确说明：SHA-256 只能证明内容未在导入后变化，不能证明发布者身份；未签名
插件会在宿主进程内获得与宿主相同的操作系统权限。候选第一版允许用户确认后继续，但不建立“永久
信任此发布者”，因为没有可验证的发布者身份。

## 6. 测试与门禁候选

### 6.1 Build 包与模板

- 打包并检查 Build/Templates 两个 nupkg 的文件、版本和依赖边界；
- 使用隔离本地 NuGet 源安装模板；
- 在仓库外临时目录分别创建 `none`、`document`、`tool`、`both` 项目；
- 验证锁文件、Debug/Release Build、显式开发部署和 Release ZIP；
- 连续两次隔离构建比较 ZIP、包清单和全部文件摘要；
- 验证外部项目不依赖仓库根 `Directory.Build.*`、Host 源码路径或未发布脚本；
- 覆盖缺少身份/版本/区间、非法目录、资产越界、重复路径、共享程序集和外来 RID 等中文负例。

### 6.2 ZIP 安全负例

- 严格 package/运行清单 schema、身份、版本、RID、兼容区间和程序集版本；
- 摘要错误、未列出文件、额外文件、缺少入口/deps/PDB；
- 路径穿越、绝对路径、大小写重复、Windows 保留名、ADS 和符号链接；
- 文件数、单文件、总解压量和 ZIP 大小上限；
- 共享程序集和非 win-x64 原生资产；
- 验证失败时不加载插件程序集、不写活动目录、不创建安装回执。

### 6.3 安装与恢复

- 新装、升级、同版本幂等、同版本内容漂移拒绝；
- 排队期间不修改当前 `Controls`；
- 重启安装、升级、卸载、撤销卸载和双向回滚；
- 在每个事务阶段注入异常并验证日志恢复；
- 无权限、目录锁定、写入失败、catalog 损坏和并发操作；
- 未纳管展示、纳管、完全一致包接管和不同内容冲突；
- 已纳管目录被外部修改后的摘要漂移诊断；
- 新版本致命加载失败后的备用版本恢复或新插件移除；
- 最终 ZIP 经平台安装后由真实 Host 完成发现、注册和生命周期初始化。

### 6.4 端到端门禁

新增统一脚本，使用本地包源完成以下完整链路：

```text
打包 Build SDK 与模板
  -> 安装 dotnet new 模板
  -> 在仓库外创建插件
  -> 两次确定性构建 ZIP
  -> 平台无 UI 导入与重启应用事务
  -> 最终目录真实 Host 加载
```

同时继续执行现有 G12 包矩阵、Plugin SDK 包门禁、四插件专项测试、解决方案锁定还原、Release
零警告构建和 Windows Smoke。测试数量、覆盖率、ZIP 文件数和摘要必须从 TRX 与机器可读 JSON
动态生成，不在文档或脚本中写死。

## 7. 文档、发布和兼容边界

实施时需要同步根 README、文档导航、外部插件快速开始、Build/Templates 发布指南、兼容契约、
验证排错、测试说明和 G12 当前契约说明。历史验收文档只追加后续格式演进链接，不改写当时事实。

所有新增 XML、PowerShell、C#、XAML 和测试辅助代码使用中文注释。注释重点说明路径所有权、失败
原子性、清单职责和不能热加载的原因，不逐行翻译语法。专项文档记录 SRP、OCP、ISP、DIP 的实际
取舍，以及未采用自定义 Task、通用安装框架、反射规则引擎和自动提权辅助进程的原因。

本候选任务不包含：

- 修改 Plugin SDK C# public API；
- 改变四插件的独立版本和发布节奏；
- 多 RID 支持；
- 在线市场、自动下载或自动更新；
- 发布者签名、证书信任和吊销；
- 热更新、热卸载或进程隔离；
- 实际推送公共/私有 NuGet 源；
- 初始化或改变 AIFLOW 上下文。

## 8. 封版后重新审核清单

只有 Host v1 已完成 G0–G16、正式标签与发布产物已经冻结后，才能把本文转为实施任务。届时必须
逐项重新确认，不能直接照搬当前候选值：

- [ ] 最终 Host API、Common Contract、Plugin SDK 和运行清单 schema 版本；
- [ ] 程序目录 `Controls` 是否仍是正式安装位置；
- [ ] 正式安装器是否允许写程序目录，是否需要改用 LocalAppData；
- [ ] G12 最终 ZIP 是否适合加入内嵌 `plugin.package.json`；
- [ ] 历史 G12 ZIP 是拒绝、迁移还是提供独立转换工具；
- [ ] 是否仍允许未签名插件；
- [ ] 是否需要发布者签名、证书信任、吊销或插件市场；
- [ ] Host 加载上下文是否仍不支持热卸载；
- [ ] 平台升级是否会完整保留 `Controls` 和 `.managed-plugins`；
- [ ] Build/Templates NuGet 包的发布源、版本策略和兼容矩阵；
- [ ] 现有四插件在正式 Host 发布包中的身份：随附、独立安装或两者之一；
- [ ] 手工目录纳管是否仍是必须支持的入口；
- [ ] ZIP 大小、文件数、路径和解压资源上限；
- [ ] 事务恢复、启动失败恢复和单版本回滚策略；
- [ ] 最终平台安装目录的备份、应用升级和卸载边界。

重新审核完成后，应为实施工作分配新的正式编号和验收基线；在此之前，本文始终保持“封版后候选”状态。
