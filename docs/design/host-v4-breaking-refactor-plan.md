# MyAvaloniaManagement V4 宿主内部破坏式收口评审与整改任务书

> 状态：实施中；G0–G7 已完成，G8 待实施。V4 尚未封板、尚不可发布。
> 评审日期：2026-08-23。
> 事实基线：[Managed Plugin V2 破坏式架构重构任务书](./host-v2-breaking-refactor-plan.md)、
> [Managed Plugin V3 破坏式架构重构任务书](./host-v3-breaking-refactor-plan.md)、
> [V3 G14 封板记录](../plan-history/host-v3/g14-v3-sealing.md)、
> [宿主—插件架构评审](./host-plugin-architecture-review.md)及 2026-08-23 当前工作树代码。
> 计划性质：V4 是一次“Host internal 死面删除 + 概念单一源 + 显式所有权 + 目录语义收口”，
> 不是第四次 Plugin SDK 扩张，也不以文件数量、抽象数量或设计模式数量作为成果。
> 本文只定义目标、阶段、删除面、验证、回滚和签署边界；每个 G 的实际提交、测试数量、覆盖率、
> 制品摘要和最终结论必须在实施时写入 `docs/plan-history/host-v4/`，不得预填或沿用 V3 数字。

## 1. 目的与结论

Managed Plugin V3 已经完成修订化 Document 保存、互斥激活、插件私有消息、注册所有权、
Workspace/Dock 分离、Host Catalog/Plugin Registry 分离、全屏租约、四插件迁移和 V3 API Shipped。
当前 Host 没有需要再次推翻的核心架构，也没有证据支持重做 SDK、Provider、Document envelope、
layout 或插件生命周期协议。

V4 只处理封板后仍可由当前代码直接证明的内部收口问题：

- `Business/Helpers` 同时承载组合根、插件发现、插件注册、Document Scope、Dock 回收和菜单查询，
  目录名已经无法表达变化原因；
- `DockNameConstant` 与 `HostExtensionIds` 重复维护稳定 Tool ID，身份存在双源；
- 布局区域的两个文件名与主类型同时错位，Mapper 又实际承担运行时验证，代码与架构文档的职责不一致；
- `DockDocumentLifetime` 通过 `Application.Current.Resources` 和字符串键寻找回收器，Document 关闭链
  没有显式声明其真正依赖；
- 主窗口仍保留空拖放协议、未使用构造依赖和被测试/Harness 直接调用的非 UI 用例入口；
- 插件菜单同时保留字符串创建命令与强类型菜单入口，生产 XAML 只使用后者；
- 文件树路径帮助器把风格漂移和 UNC 路径语义混在一起，现有测试没有覆盖 UNC 子目录；
- 多个文件、目录、命名空间和实际概念不一致，增加搜索与维护成本。

V4 的固定结论为：

1. **不重做 V3 正确边界**。SDK、插件 Provider、Registry、Workspace、Document 保存和线格式保持现状；
2. **稳定身份只有强类型单一源**。Host Document/Tool ID 不再以第二组裸字符串常量存在；
3. **测试和 Harness 使用真实用例所有者**。不得为了测试继续在生产 ViewModel 中保留转发命令；
4. **Mapper、Validator、Lifecycle 按变化原因分开**。不能因转发层薄就把验证重新塞回 Mapper；
5. **Document 控件缓存具有显式唯一所有者**。XAML Style 与关闭生命周期必须使用同一个实例；
6. **目录反映领域，不使用 Helpers 作为默认落点**。移动后不新增 Facade、Manager 或无生产价值接口；
7. **V4 名称不自动等于 SDK 4.0.0**。没有 public 契约变化时，Core/UI SDK 继续使用 V3 Shipped 基线。

### 1.1 实施范围

本任务书覆盖：

- `Host/MyAvaloniaManagement` 内部代码、XAML、项目依赖和锁文件；
- Host Unit、Headless UI、Plugin/Dock 测试中受内部命名空间和用例入口影响的部分；
- `MySmallTools.Playback.IntegrationHarness` 对主窗口测试接缝的迁移；
- Host 内建稳定 ID、布局 Mapper/Validator/Lifecycle、Document 控件缓存所有权；
- `Business/Helpers` 的领域迁移、Host 文件/类型/目录命名对齐；
- 文件树 UNC/驱动器根语义、插件菜单展示模型和低价值风格漂移；
- 架构、测试、兼容与发布说明，以及 V4 本地封板门禁。

四个业务插件的下载、播放、加解密、会计、登录和工具业务功能不在本轮范围。插件代码只允许为
保持既有 Host Harness、编译引用和最终验收所必需的修改，不得借 V4 增加插件功能。

### 1.2 设计纪律

- 按变化原因和所有权拆分，不按行数、类型数或“一文件一类型”机械拆分；
- 优先删除空协议、重复事实和转发入口，不用新抽象包住旧抽象；
- internal 协作者默认使用具体类型；只有两个生产实现或真实替换边界才增加接口；
- 目录迁移必须服从依赖方向，不能把带 Avalonia/Dock 依赖的类型仅因名称包含 Document 就塞入纯业务区域；
- 测试可通过 `InternalsVisibleTo` 使用真实 internal 用例所有者，不扩大 public API；
- 每个 G 只建立一项可验收事实；行为变化、机械移动和封板不得混在同一阶段；
- 破坏 Host internal 是允许手段，不是目标；不得重写 V3 Shipped 文本掩盖 public 破坏；
- 所有删除先证明生产、测试、Harness、XAML 和反射消费者；所有所有权修改必须覆盖失败与释放顺序；
- 不使用 AIFLOW 作为本任务书的执行、验证或发布前提。

### 1.3 兼容与破坏边界

本轮允许破坏：

- Host internal 类型、构造函数、命名空间、文件名和测试可见入口；
- Host Tests、UiTests、PluginTests 与获准 IntegrationHarness 对 internal 类型的编译引用；
- Host 项目不再需要的直接 NuGet 依赖及其锁文件图；
- 本机被忽略构建残留，但其清理不得伪装成版本库生产变化。

本轮默认不允许破坏：

- `MyAvaloniaManagement.PluginSdk` 与 `MyAvaloniaManagement.PluginSdk.UI` 的 V3 Shipped public API；
- 四插件 manifest schema 2、版本 3.0.0 与 SDK `[3.0.0, 4.0.0)` 兼容区间；
- Document envelope schema 2、插件内容 schema、`layout-v2.json` 与 layout schema 2；
- 默认数据根 `v2`、已有用户 Document、布局和外观设置；
- 插件独立 Provider、注册 Seal/Commit、生命周期、诊断脱敏和确定性打包语义。

如果任一 G 发现必须修改 SDK public API、manifest、Document envelope、layout 或数据根，必须暂停 V4，
新增独立契约/格式评审、迁移或拒绝策略、四插件影响矩阵和主版本决策；不得顺手修改。

## 2. 当前基线与代码审查

### 2.1 V4 G0 前置事实

以下是计划评审时的历史观察：当时 V3 G14 差异尚未形成独立源码提交，V4 不能直接从混合工作树开始。
G0 实施后已确认 V3 源码基线为 `16ce75e`，V4 计划提交为 `6585b9a`，并在干净工作树上开始分阶段提交；
实际证据见 [G0 V3 源码基线](../plan-history/host-v4/g0-v3-source-baseline.md)。未创建 V3 tag，且未上传或发布。

G0 必须重新读取并记录实际事实，不能把本文写作时观察到的数量当作封板输入：

- 源提交、分支和工作树状态；
- Core/UI v3 Shipped/Unshipped 条数；
- Host Unit/UI/Plugin、SDK 和四插件实际测试数；
- Host 覆盖率与关键类型覆盖率；
- 四插件 ZIP/manifest、Windows Smoke 和两轮隔离门禁摘要；
- V3 数据格式、版本和发布状态。

### 2.2 必须保留的 V3 成果

- Core/UI SDK 分包与 Core BCL-only 边界；
- V3 Core/UI Shipped 127/45 的历史签署及 Unshipped 新增规则；
- 插件独立 Provider、空集合注册、Seal、Host 最终提交和不可变 Registry；
- Host Catalog 与 Plugin Registry 分离，Host 内建项不伪装成插件；
- `HostDockFactory` 只承担 Dock Framework Adapter，`WorkspaceSession` 独占工作区所有权；
- `DocumentPersistenceCoordinator` 作为创建、打开、恢复和保存用例所有者；
- 修订化保存、New/Restore 互斥激活、Document Scope 和 ClosingToken 释放顺序；
- `IHostDocumentOpenService`、`IPluginWindowInteraction` 等已被真实消费者证明的窄端口；
- manifest/envelope/layout 严格读取、原子文件事务、诊断白名单与失败隔离；
- 四插件独立构建、确定性包、真实 Host 加载、资源 Harness 和 Windows Smoke。

### 2.3 已确认问题与处置

下表保留 G0 评审输入；所列处置已分别在 G1–G6 完成，当前事实以各阶段专用记录和源码为准。

| 问题 | 当前证据 | V4 处置 |
| --- | --- | --- |
| 空拖放协议 | `IDropTarget` 只有空 `DragOver/Drop`；XAML 只有 `AllowDrop`，没有事件链 | 三处一起删除 |
| 主窗口无用依赖 | `PluginMenuService` 被注入 `MainWindowViewModel` 但未读取 | 从字段、构造和组合根工厂移除 |
| 文件菜单悬空分隔线 | 文件菜单最后一个元素为 `Separator` | 删除 |
| Generic Host 死依赖 | Host 直接引用 `Microsoft.Extensions.Hosting`，生产代码没有 `IHost/HostBuilder` | 删除直接引用、集中版本和锁文件节点 |
| Host ID 双源 | `DockNameConstant` 与 `HostExtensionIds` 重复 Tool ID | 删除裸字符串常量，强类型单一源贯穿 Welcome/Workspace |
| 字符串 Document 命令 | 插件菜单 XAML 使用 `CreateDocumentEntryCommand`，字符串命令只被测试调用 | 删除字符串命令，测试使用强类型入口 |
| 主窗口测试接缝 | `CreateDocument/OpenDocumentByPath` 被 Host Tests 和 MySmallTools Harness 调用 | 消费者迁移到 `DocumentPersistenceCoordinator`，再删除转发入口 |
| 布局文件错位 | `DockLayoutCoordinator.cs` 包含 Lifecycle；`DockLayoutLifecycle.cs` 包含 Mapper | 成对改名，不能单独改一个文件 |
| 验证职责错位 | Runtime/Contribution 验证实现在 Mapper，Validator 只有转发 | 验证逻辑迁入 Validator，Mapper 只做映射 |
| 回收器全局查找 | `DockDocumentLifetime` 读取 `Application.Current.Resources["ControlRecyclingKey"]` | 建立显式单例所有权并证明 XAML/关闭链同实例 |
| Helpers 杂物箱 | 25 个文件横跨至少五种变化原因 | 按插件、组合、Document 所有权、Docking 和查询职责迁移 |
| 文件树 UNC 缺口 | 所有 UNC 路径均返回 drive=true；子目录测试缺失 | 定义根语义并补充回归测试后修复 |
| 展示模型过度可变 | `CategoryNode` 名称与 Documents 有公开 setter | 改为只读集合/属性，仅展开状态可变 |
| 命名漂移 | Hello/Welcome、ToolManagementViewModel/ToolWorkspaceState 等不一致 | internal 目录、命名空间和文件名统一 |

### 2.4 已排除的误判

以下项目不得作为 V4 “纯删除”直接执行：

- `Models/Plugins` 不为空，包含 `PluginStatusItem.cs`，必须保留；
- `MyAvaloniaManagement.LegacyPluginContracts` 当前只剩被 Git 忽略的本机构建残留；清理它不形成生产提交，
  也不能作为“删除 V2 生产面”的新证据；
- `Models/DocumentCreation`、`Models/ToolCreation` 没有被 Git 跟踪；本机空目录可清理，但不计入 V4 删除面；
- `MainWindowViewModel.CreateDocument` 不只被单元测试调用，MySmallTools 真实窗口 Harness 也依赖它；
- `DockLayoutCoordinator.cs` 不能直接改名为已经存在的 `DockLayoutLifecycle.cs`；必须先成对处理两个错位文件；
- `DockLayoutRuntimeValidator` 不能仅因当前实现薄就删除；文档声明的独立验证职责应由真实代码兑现；
- `WelcomeViewModel` 无参构造仍被 UI/版本测试使用；除非相关测试夹具先完成替换，否则不属于已证死面；
- `HostDiagnostics.cs` 是脱敏边界。没有变化原因证据时不因 752 行或多类型机械拆分；
- `WorkspaceSession` 已由 V3 拆出 Coordinator/Navigator/Builder，不因 572 行再次分裂。

## 3. V4 目标架构

### 3.1 包与依赖方向

```text
PluginSdk (V3 Shipped, BCL-only)
        ↑
PluginSdk.UI (V3 Shipped UI Profile)
        ↑
Host internal
  ├─ Composition
  ├─ Plugins
  ├─ Workspace / Layout / Docking
  ├─ Documents
  ├─ Presentation / Storage / Appearance
  └─ ViewModels / Views
        ↑
Host Tests / UiTests / PluginTests / approved IntegrationHarness
```

- Host internal 可以引用 Core/UI SDK、Avalonia、Dock 与 Microsoft DI；
- SDK 不得反向引用 Host、Dock 或插件实现；
- 插件只引用 SDK 包，不引用 Host 实现；获准 IntegrationHarness 的 Host internal 访问只属于测试装配；
- `Microsoft.Extensions.Hosting` 不进入 Host 顶层依赖；保留实际使用的 `DependencyInjection`；
- 目录重组不新增程序集，不继续拆 Host 项目，也不改变插件 Provider 边界。

### 3.2 领域目录与组合根

V4 目标不是追求最深目录，而是让同一目录只有一种主要变化原因。建议目标形状如下，最终名称可在 G5
实施前按实际依赖微调，但职责不得重新混回 `Helpers`：

```text
Business/
  Composition/
    HostRuntime.cs
    ServiceCollectionExtensions.cs
    HostCompositionException.cs
  Plugins/
    Discovery/
      AssemblyLoaderHelper.cs
      PluginDirectoryLayout.cs
      PluginLoadContext.cs
      PluginManifest.cs
      PluginModuleCatalog.cs
      PluginModulePreflight.cs
      PluginSharedAssemblyPolicy.cs
    Registration/
      PluginRegistryBuilder.cs
      PluginRegistry.cs
      PluginRegistrationContext.cs
      PluginRegistrationDiagnosticReporter.cs
      PluginServiceCommitGuard.cs
      SealableServiceCollection.cs
      PluginProviderOwner.cs
      PluginContributionActivator.cs
  Documents/
    Ownership/
      DocumentLifetime.cs
      DocumentScopeManager.cs
      DocumentScopeRegistry.cs
  Docking/
    DockDocumentLifetime.cs
    DocumentControlRecycling.cs
  Workspace/
    DocumentCreationMenuQuery.cs
```

约束：

- `DockDocumentLifetime` 和 `DocumentControlRecycling` 依赖 Dock/Avalonia，不能仅因名字含 Document
  就与纯保存/序列化逻辑混在一起；
- `PluginMenuService` 若继续保留，应按实际职责改名为 Document 创建菜单查询，而不是泛化为插件管理服务；
- `FileHelper` 不进入 Composition 或 Plugins；其路径语义应靠近 FileSystem 模型/工具；
- 纯文件移动不得夹带业务修改；行为调整应在移动前的独立 G 完成并验证。

### 3.3 Host 稳定 ID 单一源

`HostExtensionIds` 继续拥有所有 Host Document/Tool 规范身份：

- `WelcomeDocument`；
- `FileSystemTree`；
- `PluginMenu`；
- `PluginStatus`；
- `ToolManagement`。

`DockNameConstant` 删除。Welcome 的显示 Tool 动作优先收窄为 `Action<ToolTypeId>`，
`WorkspaceSession.ShowTool`/`ToolDockCoordinator` 在最靠近 Dock 字符串字典的边界才使用 `.Value`。
布局快照仍保存既有字符串值，不改变任何稳定 ID 或 layout 内容。

### 3.4 Layout Lifecycle、Mapper 与 Validator

最终职责固定为：

| 类型 | 唯一职责 | 不负责 |
| --- | --- | --- |
| `DockLayoutLifecycle` | Prepare、ApplyPending、Save 顺序、坏快照回退 | Dock 树遍历、字段格式、贡献验证规则 |
| `DockLayoutSnapshotMapper` | Capture、EnsureSnapshotDocks、ApplySnapshot 和 Dock 树结构转换 | 文件读写、生命周期顺序、插件可用性政策 |
| `DockLayoutRuntimeValidator` | Contribution、Pane、Tool、稳定 ID 和当前运行时完整性验证 | 应用快照、修改 Dock 树、保存文件 |
| `DockLayoutSnapshotV2Json` | 严格 JSON 读取/写出 | 运行时 Dock、插件可用性 |
| `DockLayoutStore` | 路径、原子读写、坏文件隔离和稳定诊断 | 快照映射、运行时修改 |

文件名必须与主职责一致：

- 当前 `DockLayoutCoordinator.cs` → `DockLayoutLifecycle.cs`；
- 当前装有 Mapper 的 `DockLayoutLifecycle.cs` → `DockLayoutSnapshotMapper.cs`；
- Validator 拆入 `DockLayoutRuntimeValidator.cs`；
- 测试直接使用实际被测所有者，不通过 Lifecycle 的 static Capture/Apply 转发 seam。

### 3.5 Document 控件缓存所有权

`DocumentControlRecycling` 必须只有一个运行时实例，同时被以下两条链使用：

1. App/DockControl Style 的 `ControlRecyclingDataTemplate.ControlRecycling`；
2. Document 最终关闭后的单项 `Remove(document)`。

目标时序：

```text
Composition 创建/注册唯一 DocumentControlRecycling
        ├─ App 初始化前后以可验证方式安装到 Application Resources / Style
        └─ 注入 WorkspaceSession 持有的 DockDocumentLifetime
                 ↓
Dock 确认关闭
        ↓
缓存 Remove(document)
        ↓ finally
ManagedDocumentDockable.Dispose()
        ↓
ClosingToken → Document Scope → View/Model 释放
```

G4 必须先用 Headless UI 测试证明 Style 与 Lifetime 观察的是同一引用，再删除字符串资源查找。
不得在 XAML 已经解析 `StaticResource` 后简单替换字典项并假定 Setter 自动改用新实例；若需要更改资源
安装时机或引用方式，必须以真实 App 初始化测试证明。

### 3.6 ViewModel 与测试/Harness 接缝

- `MainWindowViewModel` 只保留真实 XAML 命令、绑定状态、主题、布局和窗口关闭协调；
- `DocumentPersistenceCoordinator` 继续拥有 Create/Open/Restore/Save 用例；
- Host Tests 直接解析 Coordinator，不再通过 ViewModel 测持久化；
- MySmallTools Harness 已持有测试 ServiceProvider，可以解析 Coordinator 并等待创建完成，不得继续调用
  未等待的 `MainWindowViewModel.CreateDocument`；
- `PlugGroupMenuViewModel` 只保留 `DocumentCreationMenuEntry` 强类型创建入口；
- 不为测试新增 public Host API、静态服务定位器或兼容转发方法。

### 3.7 FileSystem 路径语义

V4 必须先固定“驱动器/共享根”的精确定义：

- `C:\`、规范化后的 `C:` 处理策略；
- `\\server\share` 与带末尾分隔符的共享根；
- `\\server\share\folder` 必须作为普通自定义目录，而不是本机驱动器模式；
- 相对路径、空白、非法路径、已删除目录和大小写；
- `SelectFolder` 对普通目录、驱动器根和 UNC 共享根产生可观察且非空的 RootNodes。

实现优先使用 `Path.GetPathRoot`、规范化和精确比较，不通过宽泛 `StartsWith("\\\\")` 把所有 UNC 路径
视为根，也不吞掉所有异常后掩盖可预测输入错误。UI 选择失败仍应安全返回，不泄漏本机路径到诊断。

## 4. V4 版本与磁盘契约

### 4.1 产品与 SDK 版本

V4 是计划代号和 Host internal 收口边界，不自动创建 SDK v4：

- `MyAvaloniaPluginSdkVersion` 默认保持 `3.0.0`；
- API baseline 保持 `v3`，V3 Shipped 文本不得改写；
- 四插件版本和 SDK `[3.0.0, 4.0.0)` 默认保持不变；
- Host 产品版本是否从 3.0.0 提升属于发布决策，不由本任务书名称自动决定；
- 若出现 public 兼容新增，只能进入 v3 Unshipped 并单独评审；本计划当前没有新增需求；
- 若出现 public 破坏，暂停本文并建立真正的 SDK v4 任务书、API baseline 和四插件迁移阶段。

### 4.2 磁盘事实保持不变

| 事实 | V4 值 | 处理 |
| --- | --- | --- |
| manifest schema | 2 | 不修改、不新增 reader |
| Document envelope schema | 2 | 不修改、不迁移 |
| layout schema | 2 | 不修改字段和语义 |
| layout 文件 | `layout-v2.json` | 不改名、不复制 |
| Host 数据根 | `v2` | 不新增 v4 目录 |
| 外观设置 | 既有 schema/路径 | 不修改 |

目录、命名空间和 internal 文件名变化不得写入 manifest、Document、layout 或数据根，也不得触发用户文件迁移。

### 4.3 发布与历史边界

- V1/V2/V3 计划、API 文本、发布脚本和阶段记录保持历史原样；
- V4 新增独立 `docs/plan-history/host-v4/`，不回填 V3 G14 实际结果；
- V4 最终门禁可以复用已签署的 V3 叶子脚本，但必须有独立 V4 编排/摘要或明确记录为何无需新入口；
- 本任务书不授权 tag、上传、NuGet 发布、插件在线分发或任何外部发布动作。

## 5. 删除、改名与保留清单

### 5.1 计划删除

- `ViewModels/IDropTarget.cs`；
- `MainWindowViewModel` 的空 `DragOver/Drop` 及 `Avalonia.Input` 引用；
- `MainView.axaml` 无消费者的 `DragDrop.AllowDrop="True"`；
- `MainWindowViewModel` 未使用的 `PluginMenuService` 字段和构造参数；
- `MenuView.axaml` 文件菜单末尾悬空 `Separator`；
- Host 的 `Microsoft.Extensions.Hosting` PackageReference、集中版本和对应直接锁图；
- `DockNameConstant.cs`；
- `PlugGroupMenuViewModel.CreateDocumentAsync(string)` 及生成命令；
- 完成消费者迁移后的 `MainWindowViewModel.CreateDocument/OpenDocumentByPath`；
- 完成测试迁移后的 `DockLayoutLifecycle.Capture/ApplySnapshot` static 转发 seam；
- `Business/Helpers` 目录本身，在全部文件归位后删除。

### 5.2 计划改名

- `Models/Tools/ToolManagementViewModel.cs` → `Models/Tools/ToolWorkspaceState.cs`；
- `ViewModels/Hello` → `ViewModels/Welcome`；
- `Views/Hello` → `Views/Welcome`；
- 对应 `.Hello` 命名空间 → `.Welcome`；
- 布局两个错位文件按 3.4 成对改名；
- `AssemblyLoadConstant` → 能表达部署目录事实的名称，成员改为普通 .NET 命名风格；值 `Controls` 不变；
- `PluginMenuService` → 能表达 Document 创建菜单查询的名称。

### 5.3 计划移动

- 组合根、插件发现、插件注册、Document Scope、Dock 回收器和菜单查询按 3.2 迁移；
- `FileHelper` 的替代实现靠近 FileSystem 模型/工具；
- 所有架构文档链接、测试 using、XAML `using:` 和 `x:Class` 与最终位置同步。

### 5.4 明确保留

- `Models/Plugins/PluginStatusItem.cs` 及 `Models/Plugins`；
- `WorkspaceSession`、`HostDockFactory`、`DocumentPersistenceCoordinator` 的 V3 职责边界；
- `HostDiagnostics.cs` 脱敏边界，除非出现独立变化原因；
- `WelcomeViewModel` 无参构造，直到其真实 UI/版本测试消费者被明确替换；
- `FileSystemTreeViewModel.initializeTree` 测试参数，除非出现不依赖运行机器的更简单替代；
- `Controls` 插件部署目录值；
- V3 SDK、manifest/envelope/layout/data root 和四插件契约。

## 6. G0–G8 独立整改包

每个 G 必须在实际开始时新建 `docs/plan-history/host-v4/gN-*.md`，记录目标、输入提交、代码变化、
删除面、插件/Harness 影响、测试命令、实际结果、覆盖率、SOLID 取舍、非发布声明和回滚边界。
本文不预建空验收记录，也不提前勾选最终签署项。

### G0：冻结 V3 源码基线

> 实施状态：已完成；证据见 [G0 V3 源码基线](../plan-history/host-v4/g0-v3-source-baseline.md)。

- **目标**：取得包含完整 G14 事实的干净、不可混淆 V3 源码提交，作为 V4 唯一输入。
- **生产变化**：无；不得在此阶段顺手删除死代码或改目录。
- **前置**：由仓库所有者确认当前 G14 未提交内容完整；形成源码提交。tag/外部发布另行授权。
- **验证**：从干净提交执行 V3 正式门禁、API/包、四插件专项、真实资源 Harness、Windows Smoke 和文档门禁。
- **记录**：实际提交、测试数、覆盖率、Core/UI API、ZIP/manifest、数据格式和发布状态。
- **本阶段排除**：SDK v4、产品版本提升、AIFLOW、外部上传、tag 和生产代码重构。
- **回滚**：删除 V4 G0 记录；不得改写 V3 G14 历史证据或把未提交工作树宣称为 V4 基线。

### G1：删除无行为价值的 Host 死面与依赖

> 实施状态：已完成；证据见 [G1 删除 Host 死面与依赖](../plan-history/host-v4/g1-remove-dead-host-surface.md)。

- **目标**：删除已经证明没有生产语义的空协议、无用依赖和 UI 残留。
- **变更**：删除拖放三处残留、MainWindow 未使用菜单服务注入、悬空 Separator、Hosting 直接依赖及集中版本；
  刷新 Host 与所有引用 Host 项目的锁文件。
- **不计入生产变化**：清理被 Git 忽略的 Legacy `bin/obj` 和本机空目录只作为本地卫生动作。
- **验证**：Release `-warnaserror`、锁定还原、Host Unit/UI/Plugin、包依赖图和真实窗口菜单/启动 Smoke。
- **负例**：Host 源码/XAML 不再包含 IDropTarget/AllowDrop/Hosting；DI ValidateOnBuild 继续通过。
- **插件影响**：无 SDK 变化；四插件编译和真实 Host 加载必须保持通过。
- **回滚**：整体回到 G0；不得恢复空接口而只删 XAML，或保留 Hosting 集中版本形成假依赖事实。

### G2：收口强类型身份与测试用例入口

> 实施状态：已完成；证据见 [G2 强类型身份与用例入口](../plan-history/host-v4/g2-strongly-typed-identity-and-use-case-entry.md)。

- **目标**：Host ID 与 Document 创建意图只有一个强类型事实源，生产 ViewModel 不再为测试转发用例。
- **变更**：删除 `DockNameConstant`；Welcome/Workspace 使用 `ToolTypeId`；删除插件菜单字符串创建命令；
  Host Tests 与 MySmallTools Harness 迁移到 `DocumentPersistenceCoordinator`；随后删除 MainWindow 的
  `CreateDocument/OpenDocumentByPath`。
- **Harness 要求**：异步创建必须被 `await`，不能依靠随后遍历 Dock 的竞态时序。
- **验证**：Welcome、ToolViewModel、DocumentPersistence、Workspace、真实播放 Harness 与资源归零测试。
- **负例**：Host 中规范 ID 字面量只存在于 `HostExtensionIds`；MainWindow 绑定端口不出现 Create/OpenPath。
- **不变项**：所有 ID 字符串值、layout 内容、Document 创建/打开错误条语义不变。
- **回滚**：整体回到 G1；不得新增 public Harness API、兼容 overload 或静态服务定位器。

### G3：对齐 Layout 文件与职责

> 实施状态：已完成；证据见 [G3 Layout 职责对齐](../plan-history/host-v4/g3-layout-responsibility-alignment.md)。

- **目标**：Lifecycle、Mapper、Validator 的文件名、代码和架构说明一致。
- **变更**：成对改名两个错位文件；迁移运行时/贡献验证逻辑到 Validator；删除零价值 static 测试转发；
  tests 直接调用 Mapper 或 Validator 的真实入口。
- **验证**：布局捕获/应用、缺插件、不可用 Tool、缺 Pane、坏快照回退、Pinned/Hidden/Active、二次保存和
  `layout-v2.json` 线格式不变。
- **覆盖要求**：Validator 每个稳定错误码、Apply 失败重建 Root、首次 Pending 原子取出均有专项测试。
- **文档**：更新架构图、职责表和全部相对路径；不得保留指向错位文件的链接。
- **回滚**：整体回到 G2；不得只改文件名不迁职责，也不得删除 Validator 后扩大 Mapper。

### G4：建立 Document 控件回收器显式所有权

> 实施状态：已完成；证据见 [G4 Document 控件回收器所有权](../plan-history/host-v4/g4-document-control-recycling-ownership.md)。

- **目标**：XAML Style 与 Document 关闭链使用同一个显式实例，删除 Application.Current 魔法键查找。
- **前置实验**：Headless App 初始化测试证明资源安装时机、Static/Dynamic Resource 选择和 Setter 引用身份。
- **变更**：组合根创建/注册回收器；App 安装同一实例；`DockDocumentLifetime` 构造接收实例；
  `WorkspaceSession` 显式接收 Lifetime 或其精确依赖；删除字符串资源定位。
- **验证**：标签切换复用、单 Document 关闭移除、多个 Document 隔离、回收器 Remove 抛出仍释放 Adapter/Scope、
  App 多实例/Headless 隔离、Runtime 退出和真实播放器 View 可回收。
- **负例**：Host 业务/生命周期代码不读取 `Application.Current.Resources`；Style 与 Lifetime 引用相同实例。
- **插件影响**：无 SDK 变化；MySmallTools 真实窗口 20 轮或 G0 实际轮数资源 Harness 必须保持归零。
- **回滚**：整体回到 G3；不得同时保留 DI 实例与 XAML 新建实例形成两个缓存。

### G5：按领域迁移 Helpers

> 实施状态：已完成；证据见 [G5 领域迁移](../plan-history/host-v4/g5-domain-helper-migration.md)。

- **目标**：删除无语义的 `Business/Helpers` 默认落点，让目录表达真实领域和依赖方向。
- **变更**：按 3.2 移动 Composition、Plugins、Document Ownership、Docking 和菜单查询文件；更新 namespace、
  using、XAML、friend tests 和文档链接。
- **纪律**：本 G 只做机械移动和已批准命名调整；不得修改算法、异常、生命周期或线格式。
- **验证**：全解决方案 Release `-warnaserror`、Host 全测试、四插件全测试、独立包与文档链接门禁。
- **负例**：生产目录不存在 `Business/Helpers`；不存在新建 `Common/Utils/Misc` 杂物箱；SDK 不引用 Host 新目录。
- **回滚**：整体回到 G4；不得保留旧命名空间转发类型或 type-forwarder。

### G6：修复 FileSystem 路径语义并收口展示模型

> 实施状态：已完成；证据见 [G6 路径语义与展示模型](../plan-history/host-v4/g6-file-system-path-and-presentation-model.md)。

- **目标**：解决 UNC 子目录误判，并完成不影响核心契约的低价值风格收口。
- **变更**：以规范路径根比较替代宽泛 UNC 判断；修复 FileSystemTree 注释/格式；`CategoryNode` 的名称和
  Documents 改为只读，仅展开状态可变；`PlugGroupMenuViewModel` 使用非空构造依赖和明确参数防御；
  统一 `AssemblyLoadConstant` 命名但保留 `Controls` 值。
- **验证**：驱动器根、UNC 根、UNC 子目录、普通目录、非法/消失路径、文件树刷新、分类展开和文档创建。
- **实现约束**：不为了一个可变布尔值强制引入新抽象；可复用 CommunityToolkit，也可保留小型 INPC，
  以更少可变面和清晰语义为验收事实。
- **插件影响**：无；部署目录、manifest 和包结构不变。
- **回滚**：整体回到 G5；不得回滚路径行为时保留与旧行为矛盾的测试。

### G7：完成四插件、Harness 与文档回归

> 实施状态：已完成；证据见 [G7 四插件、Harness 与文档回归](../plan-history/host-v4/g7-four-plugins-harness-documentation-regression.md)。

- **目标**：证明 Host internal 收口没有破坏 V3 插件契约、资源所有权、发布包或用户数据。
- **生产变化**：原则上无；只允许修复 G1–G6 暴露的真实回归，不增加新功能。
- **实际例外**：门禁暴露 MySmallTools Surface 分离在 UI 线程同步执行原生 Stop 的回归；修复仅复用
  既有原生调度器并增加一个时序单元测试，没有增加 SDK API、业务功能或磁盘格式。
- **验证**：Host Unit/UI/Plugin、Plugin SDK、MyPlugTest、DaTang、MySmallTools、BiliDownloader 全部测试；
  四插件专项 V3 验收、确定性 ZIP/manifest、真实 Host 加载、诊断脱敏与文档门禁。
- **Harness**：MySmallTools 实时/本地媒体资源循环、全屏、Document 关闭和 Runtime 退出保持资源归零。
- **数据**：现有 V2 线格式 Document/layout 可读取、应用和再次保存；不得产生 v4 数据根或 schema。
- **文档**：更新根 README、docs 导航、Host 架构、测试说明、兼容约束与本任务书实际状态。
- **非发布边界**：G7 不运行 Windows CI、Windows Smoke、ReleaseAcceptance 或 Host Release Gate；
  Windows Smoke 与两轮隔离签署留到 G8/正式发布阶段，不能由真实媒体 Harness 冒充。
- **回滚**：回到最后一个绿色 G；不得通过跳过插件、降低覆盖率或放宽严格 reader 获得通过。

### G8：V4 封板

- **目标**：把 G0–G7 的 Host internal 代码、测试、文档和制品签署为同一 V4 收口基线。
- **前置**：工作树干净；所有 G 有实际记录；Core/UI v3 Shipped 未被改写；Unshipped 状态有明确解释。
- **验证**：从干净提交执行两轮无硬链接隔离、锁定还原、Release `-warnaserror`、全部测试、覆盖率、
  API/包、四插件专项、资源 Harness、诊断、文档和 Windows Smoke；稳定事实两轮一致。
- **记录**：实际测试数、覆盖率、API 条数、删除/改名/移动面、四插件摘要、ZIP/manifest 哈希和机器事实。
- **版本结论**：明确记录 Host 产品版本决策；在没有 SDK public 破坏时继续签署 SDK v3，不伪造 v4 API baseline。
- **发布边界**：默认只建立本地可发布资格，不上传、不打 tag、不发布；外部动作需独立授权。
- **回滚**：以 V4 基线提交为整体回滚单位；不得删除或降级既有用户数据。

## 7. 执行顺序与合并纪律

```text
G0 → G1 → G2 → G3 → G4 → G5 → G6 → G7 → G8
```

- G0 只冻结 V3 输入；没有干净 V3 源提交不得进入 G1；
- G1 只删除已证死面与依赖；G2 才迁移用例接缝和身份；
- G3 单独处理 Layout 职责，避免与大规模 namespace 移动混淆；
- G4 是本轮唯一涉及运行时所有权的高风险阶段，必须先证明同实例和失败释放；
- G5 只做领域目录机械迁移，行为变化不得夹带；
- G6 处理有测试保护的文件系统行为和低价值展示收口；
- G7 只集成、回归和同步文档；若暴露真实回归，只在既有职责中最小修复，不新增架构设计；
- G8 只封板已通过事实，不在发布门禁阶段修改代码、API 或格式；
- 每个 G 必须从前一绿色提交开始，生产构建、专项测试和受影响插件测试绿色后才能提交下一 G；
- 不得使用 `--no-restore` 掩盖锁文件错误，不得降低覆盖率门槛，不得跳过真实包或 Windows Smoke；
- 任一 G 触及 SDK/格式边界时立即暂停并升级评审，不允许以“Host internal 收口”为名扩张范围。

## 8. 最终验收矩阵

### 8.1 基线、构建与依赖

- V4 输入是包含完整 V3 G14 事实的干净、可追溯提交；
- 全解决方案锁定还原和 Release `-warnaserror` 构建通过；
- Host 不再直接依赖 `Microsoft.Extensions.Hosting`，集中版本和 lock 图无死节点；
- Core SDK 保持 BCL-only，UI SDK 不引用 Host/Dock 实现；
- V3 Core/UI Shipped 文本未改写，API 门禁和独立 NuGet 消费通过；
- 四插件仍只依赖 V3 SDK 并生成确定性包。

### 8.2 身份、ViewModel 与 Harness

- Host Document/Tool 稳定 ID 只有 `HostExtensionIds` 一个事实源，字符串值完全不变；
- Welcome 到 Workspace 的 Tool 动作使用强类型 ID；
- MainWindow 构造只保留真实依赖，不实现空拖放协议；
- MainWindow 和插件菜单不存在只为测试保留的字符串 Document 创建入口；
- Host Tests 与 MySmallTools Harness 使用 `DocumentPersistenceCoordinator` 并正确等待异步完成；
- XAML 编译绑定、设计数据、真实菜单和错误条行为保持通过。

### 8.3 Layout 与 Document 所有权

- Layout 文件名与 Lifecycle/Mapper/Validator 主类型一致；
- Mapper 不负责插件可用性政策，Validator 不修改 Dock 树；
- 捕获、验证、应用、回退、Pinned/Hidden/Active 与再次保存行为保持 V3；
- `layout-v2.json` 文件名、schema、字段和值不变；
- XAML Style 与 `DockDocumentLifetime` 使用同一个 `DocumentControlRecycling`；
- 单项回收失败仍继续释放 Adapter、ClosingToken 和 Scope；退出兜底幂等。

### 8.4 领域目录与文件系统

- `Business/Helpers` 消失，类型按 Composition/Plugins/Documents/Docking/Workspace 真实职责归位；
- 不存在旧 namespace 转发、type-forwarder、`Common/Utils/Misc` 替代杂物箱；
- `Models/Plugins/PluginStatusItem` 保留；忽略目录清理不被计作生产删除；
- Hello/Welcome、ToolWorkspaceState、Layout 和常量文件名与概念一致；
- 驱动器根、UNC 根、UNC 子目录和普通目录选择行为有自动化且不会产生空树误判；
- Category 展示模型只暴露必要可变状态。

### 8.5 插件、数据、诊断与发布

- Host Unit/UI/Plugin、SDK 和四插件完整测试全部通过，实际数量记录在 G8；
- 四插件 ZIP/manifest、真实 Host 加载和专项 V3 验收保持通过；
- MySmallTools 真实媒体 Harness、全屏和 Document 关闭后资源归零；
- manifest/envelope/layout/data root 与 V3 完全兼容，不存在 v4 schema 或 v4 数据目录；
- 默认诊断、JSONL、Trace 和 UI 不泄漏路径、异常正文、URL、payload 或凭据；
- 两轮隔离结果在忽略时间、耗时和绝对路径后稳定一致；
- 最终摘要明确区分 `publishable` 与实际上传/tag/发布状态。

## 9. 明确延后

- SDK 4.0.0、public API 破坏或四插件新主版本迁移；
- 运行期热加载、热卸载、可回收 ALC 或插件不停机更新；
- 进程外插件、权限模型、恶意代码沙箱、插件市场和在线安装；
- 跨插件任意事件、服务发现、依赖图、共享数据库或分布式消息；
- 缺失插件时的部分布局恢复、未知 Tool 占位和布局合并策略；
- 通用 Document 内容迁移框架或自动升级插件内容 schema；
- MediatR、CQRS、事件溯源、通用 Repository/Unit of Work；
- 为每个 internal 类型增加接口、工厂、Facade 或 Manager；
- 因文件较大而拆 `HostDiagnostics`、`WorkspaceSession` 或所有多类型文件；
- 合并 Core/UI SDK、拆分 Host 程序集、替换 Microsoft DI/Dock/Avalonia；
- 新拖放功能。V4 只删除空残留；若未来需要文件拖入打开，必须作为独立 UI 功能设计；
- V1/V2 离线数据导入与数据根迁移；出现真实用户需求时另建一次性工具评审。

## 10. 最终签署清单

V4 只有在以下问题全部回答“是”后才算完成：

1. [x] V4 从完整、干净、可追溯的 V3 G14 源码提交开始。
2. [x] 空拖放、未使用注入、悬空菜单项和 Hosting 死依赖已删除且没有兼容残留。
3. [x] Host 稳定 ID 只有强类型单一源，既有字符串值和 layout 身份未变化。
4. [x] MainWindow 与插件菜单不再为测试保留 Document 用例转发，Harness 正确等待真实 Coordinator。
5. [x] Layout Lifecycle、Mapper、Validator 的文件、职责、测试和架构文档一致。
6. [x] DocumentControlRecycling 具有显式唯一实例，Style 和关闭链引用身份已自动化证明。
7. [x] Document 缓存移除失败不会阻断 Adapter、ClosingToken、Scope 和插件资源释放。
8. [x] `Business/Helpers` 已按真实领域消失，没有用新的杂物目录或转发类型替代。
9. [x] Hello/Welcome、ToolWorkspaceState、Layout 和部署目录常量等命名完成概念对齐。
10. [x] UNC 根、UNC 子目录、驱动器根和普通目录行为被定义并通过回归测试。
11. [x] `Models/Plugins`、V3 正确架构和诊断脱敏边界没有被误删或机械拆分。
12. [x] V3 Core/UI Shipped 未改写；manifest/envelope/layout/data root 与四插件 SDK 区间保持兼容。
13. [ ] Host、SDK、四插件、真实包、资源 Harness、诊断、文档和 Windows Smoke 全部通过；G7 已完成除 Windows Smoke 外的开发期部分。
14. [x] G0–G7 覆盖率没有通过降低门槛或跳过高价值测试获得绿色。
15. [x] G0–G7 的根 README、文档导航、Host 架构、兼容约束和测试说明与当前代码一致。
16. [ ] 两轮隔离封板可重复，并明确记录未获授权时没有上传、tag 或外部发布。

任一项未完成时，V4 只能保持候选或开发状态。不得通过改写 V3 历史证据、保留隐藏兼容入口、
降低覆盖率、跳过真实插件/Harness 或无理由提升 SDK/磁盘版本来宣称封板。
