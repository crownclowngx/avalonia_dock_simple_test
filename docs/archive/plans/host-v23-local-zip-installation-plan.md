# V23：Host 本地 ZIP 插件安装与更新方案

> 用途：直接消费现有插件项目的 ZIP 发布物，在 Host 中完成本地包检查、安装、更新、重启应用与上一版本恢复。
> 后续说明（2026-09-24）：方案已实施并归档；本文保留设计阶段的措辞，最终路径、保留策略和行为以[当前安装契约](../../reference/plugin-installation.md)为准，实际验证见[开发记录](../records/host-v23/development-acceptance.md)。原方案状态为“仅文档、尚未实施”，不代表当前源码。
> 调研基线：`764ff1fcd7b04546f32029fd9253bde1c82b7a73`，编写前工作树干净；实施前重新核对源码、插件产物和未提交差异。
> 配套：[V23 专用开发验证计划](../../maintenance/host-v23-local-zip-installation-verification.md)。当前运行能力以[兼容约束](../../../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md)、[插件开关](../../reference/plugin-enablement.md)、[重启契约](../../reference/host-restart.md)为准。

V23 是改造编号，不修改产品版本、SDK/NuGet 版本或 manifest schema。此前 [V22 在线插件源方案](../../roadmap/host-v22-plugin-distribution-plan.md)继续保持暂停；V23 独立实现本地文件入口，不恢复源管理、联网检查、自动下载或 ZIP 托管工作。

## 1. 目标与首要规定

用户在“工具 → 插件看板”选择现有 ZIP，看到插件身份、当前版本、候选版本和检查结果，确认后形成待应用操作；退出当前实例后，在下次加载插件前应用。用户不需要手工解压和覆盖目录。

1. **SOLID 是首要规定。** 包读取与检查、版本决策、磁盘提交、启动恢复、运行状态和 UI 各自负责明确事务；实现前说明依赖方向、资源寿命和失败边界。
2. **设计模式朴素使用。** 采用普通类、不可变记录、枚举和少量窄接口；只在文件提交、进程租约等真实边界提供替换点。不引入通用安装框架、状态机库、策略注册中心、第二套 DI 或事件总线。
3. **关键代码使用详细中文注释。** 包括测试夹具和工具代码。解释为何只在启动前替换、为何重复 ID 必须拒绝、锁顺序、持久化提交点、取消和崩溃恢复；优先说明设计思路与不变量，避免逐行翻译代码。
4. **单元测试和本地开发门禁齐全。** 正常路径、拒绝路径、真实文件系统失败、跨进程竞争与 UI 状态均有对应验证；不得用测试数量代替有效断言。
5. **不使用 AIFLOW，不使用 Windows CI 或发布门禁。** 后续实施运行本机专项和既有开发 `verify`；不运行 `seal`、发布 Windows Smoke、发布覆盖率或发布重复性验证，不修改发布阈值。
6. 第一版支持 Windows x64、团队维护的可信进程内 Managed Plugin；每个安装根同时最多一项未完成操作。

范围包含新装、升级、明确选择的同版本重装、明确选择的降级、取消待应用操作和恢复上一版本。安装检查是对所选本地包及磁盘状态的检查，不是联网查询新版本。不包含热加载/热卸载、卸载功能、Host 自更新、批量原子事务、跨插件依赖自动安装、数据迁移或执行包内脚本。

## 2. 已有 ZIP 输出与核对边界

### 2.1 已核对的项目

相邻 `avalonia_dock_plug_test` 下 12 个 Plugin 项目均引用 `MyAvaloniaManagement.Plugin.Build`，使用 `BuildManagedPluginPackage` 输出统一载荷。2026-09-24 的配置快照为：

| 项目目录 | Build 包版本 |
| --- | --- |
| myavalonia-baidu-netdisk、myavalonia-bili-downloader、myavalonia-classic-game | 1.1.3 |
| myavalonia-datang-work、myavalonia-fractal-art、myavalonia-image-lab | 1.1.3 |
| myavalonia-layer-unpack、myavalonia-novel-generate、myavalonia-video-security-player | 1.1.3 |
| myavalonia-workflow-studio、myavalonia-xiancai-business-worker | 1.1.3 |
| myavalonia-music-netease | 3.4.1 |

两个已安装 Build 包的脚本均采用下述 ZIP 和外置清单结构。此结论来自项目配置、实际 NuGet 缓存脚本与主仓打包实现；外部仓库没有找到现成正式发布 ZIP，不等于已经逐包验收 12 个发布物。上一轮已只读打开主仓 MyPlugTest 的真实验收 ZIP，确认其载荷布局；不把验收包命名当成正式命名协议。

事实入口：[Build 包说明](../../../Packaging/MyAvaloniaManagement.Plugin.Build/README.md)、[打包目标](../../../Packaging/MyAvaloniaManagement.Plugin.Build/build/MyAvaloniaManagement.Plugin.Build.targets)、[打包脚本](../../../Packaging/MyAvaloniaManagement.Plugin.Build/tools/Build-ManagedPluginPackage.ps1)、[统一部署目标](../../../build/MyAvaloniaManagement.ManagedPlugin.targets)、[模板交付说明](../../../Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/deployment-and-release.md)。外部仓库只是可选消费方，不成为主仓开发门禁前提。

### 2.2 正式输出

现有调用方式保持不变；以下为模板命令说明，本次文档任务不执行打包：

```powershell
dotnet msbuild src/DemoPlugin.Plugin/DemoPlugin.Plugin.csproj -t:BuildManagedPluginPackage -p:Configuration=Release
```

默认输出位于 Plugin 项目的 `artifacts/managed-plugin-packages/`：

```text
<AssemblyName>-<PluginVersion>-win-x64.zip
<AssemblyName>-<PluginVersion>-win-x64.manifest.json

ZIP 内：
Controls/
└─ <PluginFolder>/
   ├─ plugin.manifest.json
   ├─ <AssemblyName>.dll
   ├─ <AssemblyName>.deps.json
   ├─ <AssemblyName>.pdb
   ├─ plugin.build.json          可选，旧版 Build 产物可能没有
   └─ 私有托管依赖、原生文件和显式资源
```

运行清单与包清单各有职责：

| 文件 | 已有字段或用途 | V23 用法 |
| --- | --- | --- |
| 包内 `plugin.manifest.json` | schemaVersion、pluginId、pluginVersion、entryPoint、sdk | 唯一插件身份、版本和声明兼容性事实；复用严格 schema 2 读取 |
| 包外同名 `.manifest.json` | 上述字段、directoryName、targetFramework、runtimeIdentifier、sourceRevision、archive、files | 可选的发布摘要对照；核对 ZIP 长度/摘要、文件集合、逐文件长度/摘要及元数据一致性 |
| 包内 `plugin.build.json` | 编译目标及解析包版本等可选信息 | 沿用已有展示和检查用途；不存在不阻断旧包安装，不用它替代运行清单 |

包内 manifest 示例沿用现行字段，不增加展示名、下载地址或哈希字段：

```json
{
  "schemaVersion": 2,
  "pluginId": "myavalonia.plugin.my-plug-test",
  "pluginVersion": "3.0.0",
  "entryPoint": {
    "assembly": "MyPlugTest.dll",
    "type": "MyPlugTest.Plugin.MyPlugTestPluginModule"
  },
  "sdk": {
    "minInclusive": "3.4.1",
    "maxExclusive": "4.0.0"
  }
}
```

主仓 [Gate PackageBuilder](../../../tools/MyAvaloniaManagement.Gate/PackageBuilder.cs)还会生成不带版本号的 `MyPlugTest-win-x64.zip`，其证据由 Gate 保存，不必具有同名外置清单。安装器识别内容，不依赖文件名推断身份、版本或平台。正式输出工具和测试打包工具保持各自职责。

## 3. 单个 ZIP 与配套清单的接入策略

**现有 ZIP 可以直接安装，不强制作者重新打包或升级 Build 包。** 用户默认只选择一个 ZIP；Host 查找相邻同名 `.manifest.json`，也允许显式选择配套清单，适应用户改名或分开保存文件的情况。

| 输入情况 | 检查与用户结果 |
| --- | --- |
| 只有 ZIP | 执行结构、身份、版本、SDK、入口和已知资产检查；显示“基础检查通过；未进行发布摘要对照” |
| ZIP 加有效配套清单 | 执行基础检查及完整摘要对照；显示“基础检查通过；与配套发布清单一致” |
| 发现或显式选择了损坏/不匹配的配套清单 | 阻断本次安装；指出清单不匹配，不静默退回仅 ZIP 路径 |
| 用户明确移除错误配套清单后重新检查 | 重新形成仅 ZIP 检查结果和安装预览，不能沿用上一次通过结论 |

外置清单严格验证实际 schema 2 字段、类型、重复项、路径、长度及由 64 个十六进制字符组成的 SHA256；文件集合必须与 ZIP 中全部文件条目完全一致，目录条目不参与逐文件集合。内外 pluginId、版本、入口、SDK 和目录均须一致。`archive.file` 仅记录原发布名；文件改名后可接受明确选定且长度/摘要匹配的原清单，不把文件名当身份。

仅 ZIP 无完整发行级 RID/TFM 声明：不能根据 `win-x64` 文件名宣称已验证平台。检查当前 Host 平台、可读的程序集/依赖元数据及原生目录；明确异平台资产拒绝，缺少独立 RID 证据如实展示。有配套清单时额外要求 `runtimeIdentifier=win-x64`、目标框架符合 Host 支持范围。静态检查不保证全部原生依赖和业务流程可运行，运行结果仍由启动阶段提供。

SHA256 对照只说明文件与所给清单一致，不是作者签名。仅 ZIP 路径生成的实际摘要用于暂存复查、冲突检测和恢复，不把自行计算摘要称为验证了发布来源。本版不增加签名系统或新包格式；将来若要求单 ZIP 自带发行元数据，应新增独立包描述文件，不能扩展严格运行清单，ZIP 整体摘要仍需放在 ZIP 外。

## 4. 安装预览与版本决策

在现有[插件看板](../../../Host/MyAvaloniaManagement/Views/PluginStatus/PluginStatusWindow.axaml)增加“从 ZIP 安装/更新…”入口。预览包含插件 ID、入口名、磁盘版本、候选版本、SDK 要求、检查级别、目标目录和重启生效说明。现有 ZIP 没有静态展示名时，使用 ID/程序集名，不执行插件代码获取名称。

| 磁盘结果 | 默认决策 |
| --- | --- |
| ID 未安装且目标目录空闲 | 新装 |
| 同 ID，候选三段数字版本更高 | 更新 |
| 同 ID，同版本且完整载荷指纹相同 | 提示已安装，无需更新 |
| 同 ID，同版本但指纹不同 | 默认不覆盖，用户明确选择“重新安装”后再形成操作 |
| 同 ID，候选版本更低 | 默认不覆盖，用户明确选择“降级”并看到版本变化后继续 |
| 同 ID 多个目录、登记与磁盘冲突、不同 ID 占据目标目录 | 阻断，不自动删除或覆盖冲突项 |
| 已有未完成操作 | 显示该操作，先完成、取消或恢复，再接受新操作 |

版本使用现有三段数字规则，不能按字符串比较。已安装清单检查包含禁用和加载失败的候选；不只依赖当前已加载集合。升级保留现有目录、PluginId、启用选择及布局/偏好；包内目录改名不制造第二份同 ID。新装采用已校验的包目录段，目标碰撞则拒绝，不自动改名。旧目录没有有效身份时不得仅凭目录名接管。

对人工部署插件，首次更新明确展示“将接管该目录并保留完整备份”；确认时捕获整目录基线。不能知道其旧 ZIP 摘要时记录未知，不从 DLL 哈希冒充 ZIP 摘要。未知附加文件随完整旧目录保存，不混入新载荷，也不擅自清理。插件配置与业务数据应由原存储契约拥有；目录内自写数据没有自动迁移承诺，预览应提示发现的载荷差异。

运行版本、磁盘版本、待应用版本与本次运行结果分开展示。既有“检查安装产物”和“导入兼容报告”继续检查当前产物/证据，不把它们改名成 ZIP 安装，也不让安装状态覆盖生命周期状态。

## 5. 包检查与暂存边界

先复制到 Host 拥有的唯一暂存目录，随后预览和提交都消费这一份快照；不在确认后重新打开可能已被替换的用户源 ZIP。配套清单同时快照，记录实际 ZIP 和解包载荷摘要。

1. 只接受一个 `Controls/<安全单层目录>/` 载荷，允许普通目录条目，拒绝多插件、额外顶层文件、嵌套包装目录和不唯一清单。
2. 拒绝绝对路径、驱动器/UNC、`..`、NTFS ADS、设备名、尾空格/点、大小写碰撞、文件/目录冲突和链接/reparse point；每个规范化输出路径都必须位于拥有的暂存根，父目录也检查。ZIP 自带权限或属性不能授权写出目标根。
3. 初始限制：ZIP 最大 2 GiB，实际解包累计最大 4 GiB，条目最多 20000（含目录），载荷目录深度最多 32；包内运行清单继续使用既有 64 KiB 限制，外置摘要清单最多 16 MiB。限制集中定义并测试边界，不散落魔法数字；流式统计真实读写，不信任 ZIP 头声明。
4. 复用 `PluginManifestReader`、`PluginCompatibilityEvaluator` 和 `PluginDirectoryLayout` 的无副作用部分，检查 DLL/deps 存在、托管程序集有效性、程序集版本与清单一致，以及当前 SDK 区间。PDB 是正式打包要求，安装运行不以缺 PDB 拒绝可运行旧包；有摘要清单时仍要求列出的 PDB 完整。
5. 禁带共享程序集规则来自现有 RuntimeProfile，不复制正则；现有精确私有资产例外仍生效。`plugin.build.json` 可选。不得创建 PluginLoadContext、构造入口、调用 Configure 或生命周期来做安装预检。
6. 路径、格式、摘要、SDK、权限、空间或取消失败均不改活动目录。临时资源由安装服务负责释放；只清理其已验证拥有的操作目录。

复用位置：[目录预检](../../../Host/MyAvaloniaManagement/Business/Plugins/Discovery/PluginDirectoryLayout.cs)、[清单及版本检查](../../../Host/MyAvaloniaManagement/Business/Plugins/Discovery/PluginManifest.cs)、[完整载荷指纹](../../../Host/MyAvaloniaManagement/Business/Compatibility/ArtifactFingerprint.cs)、[共享资产规则](../../../build/MyAvaloniaManagement.RuntimeProfile.props)。需要抽取时只抽取实际共用的校验，保留既有错误码和失败语义。

## 6. SOLID 职责与接入位置

以下名称为拟实施的职责建议，不是已经存在的 API；放在 Host 内部，不扩展 Plugin SDK public API。

| 职责建议 | 唯一职责与边界 |
| --- | --- |
| `PluginPackageInspector` | 拥有 ZIP/清单快照和受限解包，返回不可变检查结果；不改活动目录、不访问 UI |
| `PluginInstallPlanner` | 根据包事实、磁盘快照和明确选择生成安装决策；纯规则，不开文件、不启动进程 |
| `PluginInstallationStore` | 严格读取和原子写入登记/日志，处理修订冲突；不加载插件 |
| `PluginInstallationService` | 组织预览、暂存提交、取消、恢复请求；拥有后台任务和状态快照，不代替生命周期协调器 |
| `PluginInstallApplier` | 启动前文件替换、持久化步骤及幂等恢复；通过窄文件提交端口注入失败 |
| `PluginInstallationLease` | 安装根共享使用/独占修改租约和固定锁顺序；寿命由进程入口拥有 |
| 看板 ViewModel 与 View | 展示、选择文件和传递用户意图；只依赖查询/操作窄端口，不解析 ZIP 或修改文件 |

SRP 由表中职责落实；OCP 允许未来网络下载复用已检查本地包入口，但本版不预建来源策略体系；LSP 要求真实/测试文件提交端口保持相同失败、取消和幂等语义；ISP 将 UI 查询、安装操作与启动应用端口分开；DIP 让业务决策不依赖 Avalonia、静态文件系统或进程启动实现。普通纯函数/记录不为形式统一再加接口。

安装器只能在 [`HostRuntime.CreateAsync`](../../../Host/MyAvaloniaManagement/Business/Composition/HostRuntime.cs)调用 `AssemblyLoaderHelper.Discover` 之前完成恢复/应用。当前发现按根缓存且 ALC 不支持卸载；一旦开始加载插件，本进程不再进行文件替换。启动进度沿用既有协调器，不在 UI 线程计算大文件摘要。`Program` 拥有跨 Runtime 的租约，不能在关闭看板或释放 DI 时提前允许其他进程替换文件。

## 7. 安装根存储与跨进程租约

沿用现有发现根；管理根由其规范化绝对路径推导，与 `Controls` 同卷且位于发现范围之外，不从工作目录、数据根或包清单取得任意目标路径。

```text
Host 安装目录/
├─ Controls/<PluginFolder>/            当前活动载荷
└─ .plugin-management/
   ├─ installed-v1.json               Host 管理登记
   ├─ operation-v1.json               至多一项未完成操作
   ├─ runtime.lock                    活动载荷使用租约
   ├─ operations.lock                 元数据串行提交锁
   ├─ staging/<operationId>/           ZIP、清单快照与候选目录
   └─ backups/<pluginId>/<operationId>/ 完整旧目录及旧登记
```

不同自定义发现根采用不同管理根标识；同一发现根即使数据根不同，也共享安装登记和租约。路径段由 Host 生成或严格校验，日志不允许提供任意绝对路径。首次管理要求安装根可写，不隐式提权、不转移到另一个发现根。只读安装若已有可读的租约文件，可以获得共享运行租约；无法建立可靠租约时明确阻止该根插件加载，不能静默无锁运行。该行为变化必须在后续用户文档和专项中说明。

所有 V23 Host 在发现前持有共享运行租约，覆盖整个插件代码可能执行的进程寿命，包括延迟依赖加载和关闭期间保留资源；实际文件修改必须取得独占租约。文件存在不等于被占用，PID 单独也不是进程身份。已有运行租约不得在活跃进程内升级成独占。

固定锁顺序为先 runtime 租约、再 operations 锁；只做暂存元数据提交可以单独取得 operations 锁，但持有它时绝不等待 runtime。独占应用结束后转为共享租约，取得共享后重读日志/磁盘；若另一进程赢得竞争或验证所有者不同，不按旧快照加载。试启动候选期间仅记录的 PID 加启动时间对应进程可加载，其他实例显示验证中并退出该加载路径。普通已提交版本允许多个共享读者。

有其他实例占用时保留待应用操作，显示需要退出其他实例，不强杀。旧 Host 不参与新租约，首次使用前要求退出旧实例；不声称能可靠控制旧程序或任意外部文件编辑器。文件占用或基线变化仍须使实际应用安全失败。

### 7.1 登记与日志内容

登记至少保存 schema、插件 ID、目标安全目录段、磁盘版本、完整载荷摘要、原 ZIP 摘要（人工接管可空）、摘要对照级别、上一可恢复版本引用和验证状态。它不替代实际磁盘、启用设置或本次加载结果。

操作日志至少保存 schema、随机 operationId、install/upgrade/reinstall/downgrade/restore 动作、阶段、插件 ID、目标目录、前后版本/载荷摘要、包快照摘要、原登记快照引用及试启动所有者 PID/启动时间。动作和路径必须与候选清单重新核对；日志本身不构成跳过检查的授权。

登记最多 4 MiB，单操作日志最多 1 MiB，UTF-8，拒绝重复/未知字段、未知 schema 和不合法路径。通过同目录临时文件、Flush 和原子文件替换提交；前一份恢复资料保留。不能把多份文件更新称为整体原子：操作日志最后提交最终成功状态，恢复按日志和实际载荷共同裁决。损坏或歧义保留证据，进入受控恢复界面，不能重置为空而继续覆盖。

## 8. 安装、重启与恢复流程

### 8.1 当前实例只准备

检查完成并显示预览后，用户确认具体操作。服务取得操作锁，复查没有其他待办、磁盘基线未变化、目标身份无冲突，再完整持久化候选快照与 `Staged` 日志，界面才显示“待重启应用”。提交失败不改变活动目录。预览可以取消；取消 `Staged` 先持久化取消，再清理暂存，清理失败不让已取消操作复活。

关闭看板只解除订阅；服务拥有任务。Host 退出或显式取消负责停止未提交检查。重启准备冻结新的安装提交并排空已接受的提交；用户取消重启时解除冻结，已提交待办保留。普通退出不创建重启助手，下一次手动启动同样消费已经确认的待办。

### 8.2 下一次启动应用

1. 在插件发现/ALC/Configure 之前取得独占使用租约及操作锁，读取并恢复旧事务。
2. 重新核对暂存摘要、旧目录完整摘要、身份、实际 Host SDK/平台、写权限和空间；变化即阻断本操作。
3. 写入 `Applying` 及旧登记快照，将完整旧目录移动到专属备份，再把完整候选目录移动到活动位置。每个不可逆文件步骤之前持久化意图，之后记录结果；两次目录移动之间的空档由恢复日志覆盖。
4. 文件就位后写入 `AwaitingStartup` 和试启动所有者，再进入既有启动链。新装没有旧备份；恢复操作先保留当前失败载荷，不删除它来掩盖问题。
5. 主窗口完成启动交接、目标插件成功注册且其声明的生命周期达到 Ready，才确认启用插件的本次启动成功。目标已禁用时只确认文件安装，记为“已安装，未运行验证”；全局启动失败不能确认目标成功。
6. 幂等更新登记，最后提交 `Committed` 日志，保留上一可恢复版本。后续清理仅处理工具拥有的多余备份，绝不在候选确认前删除旧版。

复用 V14 文档保存、命令排空、布局提交和干净退出。重启助手仍只负责进程交接；不添加任意命令或文件替换参数。当前进程取消重启、异常退出或资源清理失败时，不通过助手偷偷安装或重启。

### 8.3 恢复裁决

| 观察到的状态 | 处理 |
| --- | --- |
| `Staged`，尚未改变活动目录 | 可取消；下次启动重新检查后应用 |
| `Applying`，旧目录已备份但新目录未就位 | 独占租约下恢复旧目录与旧登记；新装则撤除本操作半成品 |
| 新目录就位，但阶段/登记尚未提交 | 对照前后摘要和备份恢复，不凭文件存在猜测成功 |
| `AwaitingStartup` 所有者存活 | 其他实例不加载试运行载荷，不触发并发恢复 |
| 已加载新插件后失败或进程中断，未提交成功 | 记录或保留恢复要求；本进程不替换已加载 DLL，下一次独占启动恢复旧版 |
| 新装首次启动失败，无旧版 | 下次启动将候选移出发现范围并保留证据，恢复安装前未安装状态 |
| 旧版/备份也损坏或归属无法证明 | 显示恢复失败并停止自动覆盖，保留各份载荷与诊断 |
| 用户主动恢复上一版本 | 形成新的明确待应用操作，使用同一检查、事务和启动确认流程 |

状态使用普通枚举：`Staged → Applying → AwaitingStartup → Committed`；失败经 `RecoveryRequired → RolledBack`，未应用可以 `Cancelled`。`Applying` 和 `RecoveryRequired` 内有明确的文件步骤记录，不能只靠一个笼统布尔值恢复。恢复不循环重试候选、不自动反复重启，不在同进程重新创建 Runtime/ALC。

恢复保证针对插件文件与 Host 管理登记，不承诺撤销插件已经写入的数据库、配置或业务文件。升级及降级的数据兼容性由插件负责，本版不运行安装/迁移钩子。

## 9. 分阶段实施与完成条件

| 阶段 | 实施内容 | 退出条件 |
| --- | --- | --- |
| G0 现状与夹具 | 固定 1.1.3/3.4.1 输出协议、无旁车/有旁车样本及现有行为；检查真实包是否可得 | 明确样本来源，不把构造包或 Gate 包冒充外部发布包；形成测试映射 |
| G1 检查与决策 | 快照、受限解包、内外清单、版本与目标决策，复用现有纯校验 | 包检查不执行插件代码；P/V 矩阵通过 |
| G2 暂存与持久化 | 登记、单操作日志、取消、基线复查与操作锁 | 不修改活动目录；失败和并发提交有 T/L 断言 |
| G3 启动应用与恢复 | 进程租约、发现前应用、故障恢复、启动确认及上一版恢复 | 文件步骤中断可恢复；试启动唯一；T/L/R 矩阵通过 |
| G4 看板与重启接入 | 选择 ZIP/清单、预览、待应用、失败/恢复反馈和原重启冻结 | U 矩阵通过，已有看板、开关和关闭行为不退化 |
| G5 文档与开发验收 | 同步当前契约/指南/模板，执行专项、必要工具自测和完整本地 verify | 所有必需矩阵有真实证据；实施、人工、部署、发布分别记账 |

允许先进行纯检查器开发，但不能把只有 UI 或成功解压标记为安装能力完成。全部完成前保持本方案“尚未实施/实施中”的准确状态。

## 10. 文档同步与交付

本次同步根 README、主文档导航、roadmap、Host 内部导航和验证索引；新增本方案及专用验证计划。现行使用指南继续说明人工退出/替换，不提前写成已支持安装。

后续实际实施必须同步：

- `docs/quick-start/plugin-status.md`、`restart-host.md`、`verification-and-troubleshooting.md`：真实操作、无旁车级别、重启取消、占用和恢复。
- `docs/reference/host-restart.md`、`plugin-enablement.md` 及 Host 兼容约束：启动前安装阶段、租约行为、状态和数据边界；新增当前安装契约作为唯一详细事实源。
- Build 包说明及模板 `docs/deployment-and-release.md`：保持原输出协议，新增 Host 消费步骤和配套清单用途，不要求为 V23 升级全部外部项目。
- 应用内相关帮助原文、主导航和专项索引：重新构建验证嵌入读取与渲染，不能只做 Markdown 文件存在检查。

完成后将方案归档、专用回归矩阵移入 maintenance，并在 `docs/archive/records/host-v23/` 保存开发记录、实际测试映射和非嵌入证据 JSON。源码、测试结果、人工体验、本机部署和公开发布分别标记；本方案不提前创建“安装成功”或“验收通过”的结果。
