# Managed Plugin V2 G5：声明式贡献目录

> 状态：已完成。
> 日期：2026-08-21。
> 性质：开发阶段非发布重构；未运行 Windows CI、Windows Smoke、发布门禁或发布操作。

## 1. 结果

G5 已把 Host 生产模块预检、插件组合和 UI 消费者切换到最终
`MyAvaloniaManagement.PluginSdk.UI.IPluginModule`。一次注册同时冻结 manifest 所有者、Descriptor、
模型类型、View 类型/无参工厂和可选生命周期实现类型。Host 不再为生产 Registry 保留 Strategy、
`GetMetadata`、Creation Intent Provider 或独立 `AddView` 路径。

Host 的 Welcome、文件树、插件菜单、工具管理和插件状态全部通过同一个 `PluginRegistration` 声明。
Welcome 在 G5 暂时同时实现最终 `IPluginDocument` 和 Dock `Document` 形状；Host Tool 暂时仍继承 Dock
`Tool`。这是明确的阶段边界，不是最终插件模型；G6 会在 Activator 后增加 Dock Adapter。

四个业务插件没有在本阶段迁移。它们的 Legacy 源码继续编译并执行自身单元测试，但 G5
`PluginModulePreflight` 只接受最终 UI SDK 模块，因此旧入口不会加载，也没有双接口回退。MyPlugTest、
DaTangAccountingHelpPlug、MySmallTools 和 BiliDownloader 分别留给 G9–G12。

## 2. SOLID 设计

- 单一职责：`PluginRegistration` 负责翻译一次声明；局部 `PluginRegistryBuilder` 负责候选结构校验；
  全局 Builder 负责冲突过滤；`PluginRegistry` 只保存事实和索引；`PluginContributionActivator` 只路由
  Provider、Document Scope 与模型创建；`ViewLocator` 只按模型查询并按需创建 View。
- 开闭原则：新增 Document/Tool 只增加一条泛型声明，不修改 Registry 查询算法或增加新的发现分支。
- 里氏替换：SDK 泛型约束保证 Document 实现最终文档契约、View 是可无参构造的 `Control`；Host 的
  G5 Dock 形状要求只存在于 internal Activator/Scope 边界，未污染 public SDK。
- 接口隔离：插件只取得自身 `IPluginRegistration` 和私有 `IServiceCollection`；Registry 不暴露
  Provider，Activator 也不成为插件可访问的通用服务定位器。
- 依赖倒置：菜单、工具管理、ViewLocator、状态投影和 ManagementFactory 都依赖同一个不可变 Registry
  事实，不依赖具体插件、Strategy 或程序集扫描。

使用的模式刻意保持朴素：Registration、Builder、不可变 Registry、内部 Activator 和 Provider 租约。
没有规则引擎、动态代理、反射扫描、事件溯源、覆盖优先级链、通用 Unit of Work 或父容器回退。

## 3. 注册封闭与生命周期

`AddDocument`/`AddPersistableDocument` 自动把模型注册为 scoped；`AddTool` 和 `UseLifecycle` 自动注册为
插件 singleton。普通 `Services` 注册仍保留 Microsoft DI 的多实现、keyed 和开放泛型能力。

模块返回后，Host 调用 `Seal()`：贡献专用方法立即拒绝追加，模块之前取得的 `Services` 引用也由
`SealableServiceCollection` 拒绝任何写操作。封闭只影响写入；Host 仍从原集合构建独立 Provider。
该包装器没有服务解析能力，也不是第二个容器。

G5 对生命周期只做三件事：冻结实现类型、注册为 singleton、在候选 Provider 中验证可解析性。不调用
初始化或关闭，不处理依赖图、超时、状态机和贡献可用性；这些职责留给 G8。

## 4. 两阶段提交与冲突算法

每个插件使用自己的临时 Builder：

1. 执行模块配置并封闭注册入口；
2. 校验候选只有一个所有者，且没有重复 Document/Tool ID、重复精确模型映射、同一模型跨
   Document/Tool 或多个生命周期；
3. 构建 `ValidateScopes`/`ValidateOnBuild` 的插件 Provider，并验证生命周期 singleton 可解析；
4. 成功后把纯声明导入全局 Builder，同时把 Provider 作为未提交租约暂存；
5. 全部候选完成后，以简单 `GroupBy` 检测跨所有者 Document ID、Tool ID 和精确模型类型冲突；
6. 释放全部被排除 Provider，只登记已接受插件的 Document Scope，再创建不可变 Registry。

| 冲突组 | 处理结果 |
| --- | --- |
| 纯插件冲突 | 排除组内所有插件；它们的其他贡献也不发布 |
| 插件与 Host 冲突 | 保留 Host，排除全部冲突插件 |
| 无冲突插件 | 继续发布，不受其他插件失败影响 |

算法没有“先注册者获胜”。冲突判断的单位是所有者，发布单位也是所有者，所以不会出现同一插件的菜单
已经可见、Provider 却被释放的半状态。

## 5. 失败原子性与 Provider 所有权

- 模块构造或 `Configure` 失败：没有 Provider、全局声明或 Scope 登记。
- 插件内声明校验失败：局部 Builder 整体丢弃；已经创建的临时 Provider 在异常路径释放。
- Provider 构建/生命周期解析失败：Provider 立即释放，局部声明不导入。
- 全局冲突：冲突插件的未提交租约立即释放，不登记其 `DocumentScopeManager`，Registry 过滤其全部声明。
- Registry 发布：只有不可变 Descriptor、类型、View 工厂、所有者和查询索引；不保存 Provider。

`PluginProviderOwner` 是插件 Provider 的唯一所有者。`PluginContributionActivator` 是唯一读取该所有者并
按 Registry 所有者创建模型的 Host internal 边界：Document 进入所属 Provider 的独立 Scope；Tool 从
所属 Provider 取得 singleton。不同 `HostRuntime` 各自建立 Builder、Registry、ProviderOwner 和事件总线，
不共享可变全局状态。

## 6. 阶段桥

G7/G8 前仍需读取的 Document v1 与 layout v1 历史 ID 只允许在
`LegacyContributionIdMap` 内使用各 ID 的 `Value` 显式转换。Descriptor、Registry 和 public SDK 不保存
Legacy ID，也不提供兼容别名。G5 的 Registry 与组合代码不引用旧 Strategy、`GetMetadata`、Intent
Provider 或独立 `AddView`。

G5 不修改 SDK public API、产品/SDK 版本或活动 API 文本；G2 的 Core/UI API 基线只验证，不重写。

## 7. 测试与非发布门禁

专项入口：

```powershell
.\scripts\Test-DeclarativeContributionCatalog.ps1 -Configuration Release
```

脚本串行执行 G5 Unit、Plugin 和 Headless UI 过滤集，扫描生产目录删除面，并在
`artifacts/test-results/DeclarativeContributionCatalog/summary.json` 写入 `windowsCi=false`、
`releaseGate=false`。覆盖内容包括：

- Descriptor 不可变、防御性复制、元数据读取不构造模型；
- 泛型约束、固定 DI 生命周期、注册/服务集合封闭和未声明类型不可见；
- 插件内重复/所有者混入、多生命周期和模型角色冲突；
- 插件—插件 Document/Tool/模型冲突、插件—Host 冲突、无冲突插件继续发布；
- Configure、Provider 构建、局部校验和全局冲突不产生部分 Registry、Provider 租约或 Scope；
- Registry 发布后不可写、不同 Runtime 隔离；
- View 按需构造、失败脱敏诊断与受控占位；
- Host Welcome/Tool、菜单、工具管理、ViewLocator 和状态投影读取同一 Registry。

本阶段同时顺序验证锁定还原、全解决方案 Release `-warnaserror`、SDK 32 项及 API v2 基线、Host 三套
测试与既有覆盖率阈值、受影响业务插件单元测试、文档核心/完整门禁。验收结果如下；数字只记录本轮
事实，不作为永久硬编码阈值：

| 门禁 | 结果 |
| --- | --- |
| 锁定还原 + 全解决方案 Release `-warnaserror` | 通过，0 警告、0 错误 |
| G5 专项 | 51/51（Unit 22、Plugin 14、UI 15） |
| Plugin SDK 契约 | 32/32；Core/UI API v2 兼容门禁通过 |
| Host 完整回归 | 374/374（Unit 175、UI 39、Plugin 160） |
| Host 覆盖率 | 行 81.36%、分支 66.97%；既有阈值未降低，并新增五个 G5 关键文件阈值 |
| 受影响业务插件 | 967/967（BiliDownloader 720、DaTang 64、MySmallTools 183） |
| 文档门禁 | Core 与完整入口通过；43 份文档、260 个本地链接 |

TRX/Cobertura 与脚本摘要位于 `artifacts/test-results/`；这些运行产物不作为源码提交的一部分。

明确没有运行：Windows CI、Windows Smoke、`Invoke-HostV1ReleaseGate`、ReleaseAcceptance、真实媒体、
联网测试、插件发布包总门禁、上传、打标签或任何发布操作。

## 8. 回滚单位

G5 完整回滚单位是：最终注册实现、可封闭服务集合、插件候选快照、全局冲突过滤、不可变 Registry、
internal Activator、Host 内建声明、阶段 ID 映射、最终 SDK 加载测试夹具、专项脚本和本轮文档。

回到 G4 时不得留下第二套生产 Registry，不得让最终与 Legacy 模块接口并行接受，也不得恢复反射发现、
Strategy 激活或独立 View 注册。用户数据没有在 G5 改写，因此回滚不包含数据迁移或降级写回。

## 9. 后续边界

- G6：以普通模型 + Host internal Dock Adapter 取代 G5 的临时 Dock 形状。
- G7：建立 Document v2 创建、恢复、保存、关闭和 Scope 单路径。
- G8：建立 layout v2、最终生命周期状态机和贡献可用性门控。
- G9–G12：逐个迁移四业务插件，不在 Host 增加 Legacy 回退。
- G13/G14：删除全部 Legacy 与阶段桥并执行最终 V2 封板/发布验收。
