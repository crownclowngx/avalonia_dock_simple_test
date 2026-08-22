# Managed Plugin v1 G4：Managed-only 插件加载

> **历史说明：本 V1 阶段已由 Managed Plugin V2 G14 取代；以下日期、数量和结论保持原样。**

> 状态：已完成
>
> 完成日期：2026-08-15
>
> 前置基线：G0 绿色基线、G1 支持边界与版本线、G2 Host 实现面收口、G3 Plugin SDK
>
> 所属任务：[Managed Plugin v1 封板评审与整改任务书](../../design/host-v1-sealing-readiness-plan.md#g4删除-legacy-二进制插件路径)

## 1. 结果摘要

G4 删除了无模块二进制插件、策略 public 无参构造激活、无 `.deps.json` 目录索引回退以及
两个历史程序集加载 Facade。宿主现在只有一条插件启动链：

```text
严格 plugin.manifest.json
→ 兼容区间与入口版本预检
→ 必需的入口 .deps.json
→ 每插件独立 PluginLoadContext
→ 完整引用与类型预检
→ 唯一 IPluginModule 结构预检
→ 模块身份核对和服务注册
→ ActivatorUtilities 激活 Document/Tool 策略
```

本次没有提升 manifest schema，也没有修改 Plugin SDK public API。破坏性变化只针对 G1 已明确
排除在 v1 支持面之外的 Legacy 二进制插件。Document/Tool 的 `LegacyIds`、布局 V1、旧浮动状态
归一化和旧数据目录隔离继续保留，它们属于持久化数据兼容。

## 2. 删除内容

| 删除项 | 删除原因 |
| --- | --- |
| 无模块程序集继续参与策略扫描 | 插件没有稳定所有者，无法统一 DI、生命周期和诊断 |
| `PluginStrategyActivator` 的双轨分派 | 同一策略接口存在两套构造语义，新增依赖容易在运行期才失败 |
| `myavalonia.legacy.*` 推断所有者 | 程序集名称不是受版本约束的插件身份事实 |
| 无 deps 的递归 DLL 索引 | 实际依赖闭包不可审阅，并可能与标准 RID 图产生不同结果 |
| `LoadPluginsFromDirectories` | 只返回程序集，丢失清单、预检类型、模块和诊断快照 |
| `LoadAssembliesFromSubdirectory` | 绕过插件清单、兼容检查和目录隔离 |
| Native 目录排除扫描 | G4 后宿主不再递归扫描托管 DLL，排除列表没有生产行为 |

`PLUGIN_ENTRY_AMBIGUOUS` 和 `PLUGIN_PRIVATE_DEPENDENCY_AMBIGUOUS` 同时删除，因为对应的入口猜测和
目录依赖索引已经不存在。入口始终由严格清单唯一声明。

## 3. 保留边界

- `IPluginModule` 仍使用 public 无参构造。它是根容器建立前的最小引导对象，不能依赖尚未建立的 DI；
- Document/Tool 策略统一使用 `ActivatorUtilities`，可以只保留表达真实依赖的构造函数；
- View 仍按命名约定扫描并以 public 无参构造创建，由 G5 改为显式 View 贡献；
- Document、Tool 和 View 的反射发现仍保留，由 G5 建立集中 Plugin Registry；
- `LegacyIds` 和布局迁移继续读取旧稳定 ID，但新保存只写规范 ID；
- ALC 仍不可回收，插件通过退出宿主后替换，不支持热卸载；
- 插件仍是可信进程内代码，不提供沙箱或权限隔离。

## 4. SOLID 与朴素设计

### 4.1 单一职责

- `PluginDirectoryLayout` 只验证入口 DLL 和同名 `.deps.json`；
- `PluginLoadContext` 只执行共享程序集策略和 `AssemblyDependencyResolver` 解析；
- `PluginModulePreflight` 只解释类型结构，不实例化插件代码；
- `PluginModuleCatalog` 只实例化已验证模块、核对身份并编排服务注册；
- `HostExtensionRegistry` 只消费确定所有权并通过 DI 创建扩展策略。

### 4.2 开闭与接口隔离

兼容插件仍可通过 SDK 次版本增加新的服务和策略实现，但不能通过缺少模块、缺少 deps 或宿主内部
Facade 建立旁路。G4 没有为已删除行为创建 `ILegacyLoader`、模式枚举或空兼容接口。

### 4.3 依赖倒置

Document/Tool 策略只声明业务依赖，由 Composition Root 提供。宿主不再根据构造函数形状决定插件
属于哪一种激活模型，也不再用程序集文件名生成所有者。

### 4.4 使用的模式

- **Validator**：`PluginModulePreflight.TryValidate` 返回结果和稳定诊断，不用异常表达预期拒绝；
- **Immutable Snapshot**：程序集、清单、类型和模块类型来自同一次发现事实；
- **Catalog**：`PluginModuleCatalog` 保存唯一模块与 PluginId 所有权；
- **Strategy**：共享程序集策略与 deps 解析策略仍封装在独立 ALC 中；
- **Fail Fast**：未知外部程序集不能生成猜测所有者，模块身份错误在服务注册前终止组合。

这些模式均复用现有启动链，没有增加通用管线框架、服务定位器或新的 manifest 占位字段。

## 5. 包结构与依赖解析

Managed Plugin v1 的最小目录为：

```text
Controls/<PluginDirectory>/
├── plugin.manifest.json
├── <EntryAssembly>.dll
├── <EntryAssembly>.deps.json
├── 私有托管依赖
└── runtimes/<rid>/native/...（存在原生资产时）
```

`PluginDirectoryLayout` 在创建 ALC 前检查 manifest 入口和 deps。`PluginLoadContext` 随后始终创建
非空 `AssemblyDependencyResolver`：SDK 与显式 UI Profile 共享程序集从默认上下文返回，普通托管
依赖和原生资产只接受当前插件 deps/RID 图给出的路径。宿主不会搜索相邻插件或递归猜测 DLL。

## 6. 模块结构与执行时机

`PluginModulePreflight` 对已经完成类型预检的入口程序集要求：

1. 存在一个具体 `IPluginModule` 类型；
2. 不能存在第二个模块候选；
3. 唯一模块具有 public 无参构造。

该阶段不构造模块、不读取 `PluginId`、不调用 `ConfigureServices`，也不激活 Document/Tool 策略。
结构错误因此只隔离当前插件目录。结构通过后，Catalog 才实例化模块，并把模块构造异常、非法
PluginId 或清单身份不一致视为全局组合错误。

严格地说，判断“缺少模块”必须先加载入口程序集并读取类型元数据；因此 G4 能保证的是宿主不会
主动实例化插件对象或调用入口服务，而不是提供恶意代码沙箱。CLR Module Initializer、原生崩溃和
进程全局状态仍属于“可信进程内插件”边界。

## 7. 稳定诊断

| 错误码 | 阶段 | 处理 |
| --- | --- | --- |
| `PLUGIN_MANIFEST_MISSING` | Manifest Preflight | 加载 DLL 前隔离目录 |
| `PLUGIN_DEPENDENCY_MANIFEST_MISSING` | Plugin Root Discovery | 加载 DLL 前隔离目录 |
| `PLUGIN_MODULE_MISSING` | Type Preflight | 不实例化模块或策略，隔离目录 |
| `PLUGIN_MODULE_MULTIPLE` | Type Preflight | 不选择候选，隔离目录 |
| `PLUGIN_MODULE_CONSTRUCTOR_INVALID` | Type Preflight | 不调用非标准构造，隔离目录 |
| `PLUGIN_MANIFEST_DESCRIPTION_MISMATCH` | Module Discovery | 服务注册前中止组合 |

前三类模块结构错误和缺 deps 均进入可恢复插件加载策略；其他有效插件可以继续发现。模块已经执行
构造后产生的身份或注册错误仍保持 Fatal，避免共享容器出现含义不确定的部分组合。

## 8. 测试夹具与证据

`ManagedOnlyPluginLoadingTests` 提供 8 项专项门禁：

- 有效 deps 与唯一模块形成 Catalog；
- 缺清单不加载入口；
- 缺 deps 只隔离当前目录，另一个插件继续加载；
- 只有无参策略、没有模块的程序集在激活前隔离；
- 多模块与错误模块构造分别返回稳定错误码；
- 只有 DI 构造的策略可以创建；
- 四个真实插件都有唯一模块所有者；
- Host 程序集中不存在 `PluginStrategyActivator`。

私有依赖隔离夹具也补充了真实 `IPluginModule`，继续证明同名 1.0/2.0 私有依赖进入不同 ALC，
而 SDK 共享同一默认上下文。历史稳定 ID 与布局迁移测试未删除。

2026-08-15 的最终门禁结果：

| 门禁 | 结果 |
| --- | --- |
| 锁定还原 | 通过 |
| Release 解决方案构建 | 0 警告、0 错误 |
| Managed-only 专项 | 9/9 |
| Host Unit | 113/113 |
| Headless UI | 37/37 |
| Plugin | 127/127 |
| 三层合计 | 277/277，无跳过 |
| Host 覆盖率 | 行 78.70%，分支 64.35% |
| SDK 基础包与 UI Profile 临时消费者 | 通过 |
| Windows 真实窗口 Smoke | 通过 |

测试数量是本次时间点证据，实际门禁仍从 TRX 与 `summary.json` 动态统计。

## 9. 回滚与后续

G4 没有迁移或删除用户数据，代码回滚不会修改 v1 数据根。若回滚加载器、Catalog 和 Registry，必须
连同 Managed-only 夹具、诊断策略和当前事实文档整体回滚；不能只恢复目录索引，否则会重新形成
无模块程序集进入 View/策略扫描的旁路。

G5 将以显式 Document、Tool、View 和生命周期贡献替换剩余反射/命名发现，并让 ViewLocator 直接
消费集中 Plugin Registry。G6 再保护宿主核心 DI 注册。本次不提前实现这些接口。
