# Managed Plugin V2 G3：manifest v2 与构建协议记录

> 状态：已完成（2026-08-21）
> 适用范围：严格 manifest v2、精确入口发现、单一 SDK 兼容事实、构建期入口探针与确定性插件包
> 前置记录：[G0 绿色基线](./g0-green-baseline.md)、[G1 版本与数据边界](./g1-version-and-data-boundaries.md)、[G2 Plugin SDK 重建](./g2-plugin-sdk-rebuild.md)
> 发布边界：本阶段只执行非发布构建协议门禁，不运行 Windows Smoke、Windows CI、ReleaseAcceptance、G14 发布总门禁或任何发布操作

## 1. 结果摘要

G3 一次性把生产清单切换为严格 manifest v2。Host 不读取 manifest v1，不保留双 reader，也不从
程序集扫描模块。清单以大小写敏感的程序集内完整类型名指定唯一执行入口；程序集中的其他模块即使
构造会抛异常，也不会被发现、构造或配置。

兼容事实从 Host API/Common Contract 两套区间收敛为一个 Plugin SDK 左闭右开区间。Host 只比较实际
Core/UI SDK 版本；二者不一致属于宿主配置错误。对插件可观察的兼容失败统一为
`PLUGIN_SDK_INCOMPATIBLE`，诊断持久化只写 `sdkRange`，独立诊断 schema 提升为 2，并且不读取或迁移
旧诊断日志。

四个业务插件的入口仍实现 Legacy `IPluginModule`。这是 G3 到后续消费者迁移之间的阶段桥，不是最终
SDK 形状；本阶段没有提前迁移插件容器、贡献 API、Dock、Document、layout 或业务实现。四个入口类型
全名已经固定，后续迁移只替换其实现接口和内部注册逻辑，不改 manifest 身份。

## 2. 唯一 manifest v2 格式

根对象固定为五个字段，顺序由构建协议确定：

```json
{
  "schemaVersion": 2,
  "pluginId": "myavalonia.plugin.example",
  "pluginVersion": "2.0.0",
  "entryPoint": {
    "assembly": "Example.Plugin.dll",
    "type": "Example.Plugin.ExamplePluginModule"
  },
  "sdk": {
    "minInclusive": "2.0.0",
    "maxExclusive": "3.0.0"
  }
}
```

Reader 在任何插件代码执行前完成以下验证：

- 文件不超过 64 KiB，JSON 最大深度为 8；不允许注释或尾随逗号；
- 根对象、`entryPoint` 和 `sdk` 都严格拒绝未知、重复、缺失或大小写错误字段；
- `schemaVersion` 只能为 2，v1 明确拒绝；
- Plugin ID 必须是规范的小写 `myavalonia.plugin.*` 稳定身份；
- 插件版本和 SDK 端点只接受 `major.minor.patch` 三段数字；
- SDK 区间必须满足 `minInclusive < maxExclusive`；
- `entryPoint.assembly` 只能是根级 DLL 文件名，不能是绝对路径或包含目录分隔符；
- `entryPoint.type` 必须是非空、无空白、非程序集限定、非泛型、非嵌套的命名空间限定类型全名。

插件版本仍必须与入口程序集 `AssemblyVersion` 精确一致。入口 DLL 必须携带同名 `.deps.json`；重复
PluginId 仍在创建任何插件加载上下文、读取程序集元数据或执行插件代码前阻断组合。

## 3. 精确入口加载顺序

Loader 的顺序验证管线固定为：

1. 扫描插件目录并严格读取全部 manifest；
2. 校验单一 SDK 区间，并在所有目录之间检查 PluginId 唯一性；
3. 校验入口 DLL 与同名 `.deps.json`，建立该插件独占的 `PluginLoadContext`；
4. 加载清单指定程序集并核对 `AssemblyVersion`；
5. 使用 `assembly.GetType(entryPoint.type, throwOnError: false, ignoreCase: false)` 取得精确类型；
6. 在不构造插件对象的情况下验证类型 public、非抽象、非泛型、实现当前 Legacy
   `IPluginModule` 且具有 public 无参构造；
7. 发现快照只保存程序集、已验证 manifest 和对应入口类型；
8. Catalog 只实例化快照中的该类型，再进入既有 `Configure` 阶段。

这条管线删除了 `GetTypes()` 唯一模块扫描、多个模块选择和无清单测试发现入口。任何清单、依赖、
版本或类型失败都发生在入口构造及 `Configure` 前。第二个模块的存在不再是错误，也不能通过顺序、
命名或抛异常劫持入口。

## 4. SOLID 设计思路与朴素模式

本阶段首先满足 SOLID，再选择最小实现：

- 单一职责：Reader 只把不可信 JSON 转成已验证值；目录布局只校验文件边界；Loader 只解析程序集和
  精确类型；Preflight 只判断入口结构；Catalog 只负责构造与配置。
- 开闭原则：manifest 入口全名成为稳定接缝。后续入口改为最终 UI SDK 接口时，不需要恢复扫描或修改
  清单形状，只替换集中预检约束。
- 里氏替换：入口只有完整满足当前可执行 `IPluginModule` 约束才进入 Catalog；错误接口不会拖到构造
  或配置阶段才失败。
- 接口隔离：兼容判断只接收一个不可变 `SdkRange` 和一个实际 SDK 版本，不再暴露 Host/Common 两套
  重复事实。
- 依赖倒置：加载与诊断依赖已验证的清单值对象，而不是从程序集内容、文件夹命名或异常文本反推协议。

使用的模式只有不可变值对象、严格 Reader、顺序验证管线和构建期编译探针。没有增加通用工厂、策略
框架、服务定位器、自定义 MSBuild Task、动态脚本编译框架或反射式扩展机制。

## 5. 构建协议

四个插件项目统一声明以下三个事实：

```xml
<ManagedPluginEntryType>Example.Plugin.ExamplePluginModule</ManagedPluginEntryType>
<ManagedPluginSdkMinInclusive>$(MyAvaloniaPluginSdkVersion)</ManagedPluginSdkMinInclusive>
<ManagedPluginSdkMaxExclusive>$(MyAvaloniaPluginSdkNextMajorVersion)</ManagedPluginSdkMaxExclusive>
```

`schemaVersion=2` 只来自根级 `MyAvaloniaV2ManifestSchemaVersion`，插件不能覆盖为 v1。旧
`ManagedPluginHostApi*` 和 `ManagedPluginCommonContract*` 四属性即使数值正确也不能替代新属性，并且
只要出现就使构建失败。

公共 Target 先校验 ID、版本、目录、入口类型语法、SDK 端点格式和区间方向。主程序集编译完成后，
Target 在 `obj` 中生成一个最小 C# 探针，并使用 MSBuild 内置 `Csc` 作为独立程序集再次编译。探针引用
成品插件程序集和已经解析的引用，直接把 `new <ManagedPluginEntryType>()` 赋给 Legacy
`IPluginModule`。因此不存在、不可访问、抽象、泛型、错误接口或没有 public 无参构造的入口都会在
构建期失败。探针 DLL 不进入插件程序集、部署目录或 ZIP，也不增加任何 public API。

只有探针成功后才生成 UTF-8 manifest、开发部署目录和打包输入。PowerShell 打包入口在创建 ZIP 前
再次严格复核 manifest 字段、入口程序集元数据、精确入口类型、SDK 区间和程序集版本。外置机器清单
使用 schema 2，并镜像 `entryPoint`、`sdk`、ZIP 摘要和逐文件摘要。

确定性包协议继续使用规范路径排序、固定 ZIP 时间戳、逐文件 SHA-256、ZIP SHA-256 和解压后复核。
这是 G3 的非发布构建协议门禁，不是 Windows 发布门禁，也不产生四插件合集。

## 6. 四插件固定入口

| 插件 | `ManagedPluginEntryType` | SDK 区间 |
| --- | --- | --- |
| BiliDownloader | `BiliDownloader.Plugin.BiliDownloaderPluginModule` | `[2.0.0, 3.0.0)` |
| DaTangAccountingHelpPlug | `DaTangAccountingHelpPlug.Plugin.DaTangAccountingHelpPluginModule` | `[2.0.0, 3.0.0)` |
| MyPlugTest | `MyPlugTest.Plugin.MyPlugTestPluginModule` | `[2.0.0, 3.0.0)` |
| MySmallTools | `MySmallTools.Plugin.MySmallToolsPluginModule` | `[2.0.0, 3.0.0)` |

这些类型当前仍实现 Legacy 接口。G3 不修改 Plugin SDK public API，也不把 Legacy DLL 放入插件包。

## 7. 失败语义

| 失败 | 行为 |
| --- | --- |
| manifest 缺失、损坏、超限、非 v2 或字段非法 | `PLUGIN_MANIFEST_*`；隔离当前目录，不加载入口 DLL |
| 实际 SDK 不在单一区间 | `PLUGIN_SDK_INCOMPATIBLE`；不创建模块 |
| Core/UI SDK 版本不一致 | 宿主配置错误；不把两套版本伪装成插件兼容失败 |
| 入口 DLL、deps 或程序集身份无效 | 保留现有稳定加载诊断；不构造模块 |
| 精确类型不存在、大小写不匹配或结构不可执行 | `PLUGIN_ENTRY_INVALID`；不扫描替代模块 |
| 重复 PluginId | 在加载任何插件代码前阻断全局组合 |
| 未声明第二模块存在或构造会抛错 | 忽略；只执行 manifest 精确入口 |

诊断记录 schema 2 不兼容旧日志，G3 不提供读取或迁移旧 JSONL 的逻辑，也不修改、删除已有用户文件。

## 8. 测试与非发布门禁证据

Reader、Loader、构建变异和包矩阵覆盖以下关键风险：

- 根对象及两个嵌套对象的缺失、未知、重复字段，注释、尾逗号、大小和深度边界；
- 非法 ID、非三段版本、反向 SDK 区间、v1 拒绝、入口路径和入口类型语法；
- DLL/deps 缺失、类型不存在与大小写不匹配、不可访问、抽象、泛型、错误接口、构造约束、程序集版本；
- SDK 左闭右开边界，以及所有失败均先于模块构造和 `Configure`；
- 双模块夹具只进入清单入口，未声明模块的抛错构造不会执行；
- 新属性缺失/非法、旧四属性不能替代、schema v1 和各种非法入口的 MSBuild 变异；
- 四插件各两轮隔离构建，比较 ZIP、文件清单和 sidecar，并解压后通过真实 Host Loader 验证精确入口。

2026-08-21 串行执行结果：

| 门禁 | 本轮结果 |
| --- | --- |
| 锁定还原 | 更新 Host 新 Core/UI 项目引用对应锁文件后，解决方案 locked-mode 通过 |
| Release `-warnaserror` 全解决方案构建 | 通过，0 warning / 0 error |
| SDK 专用单元测试 | 32/32 |
| 三套 Host 测试 | Unit 172、UI 39、Plugin 161，共 372/372；行 81.12%、分支 66.77% |
| 三个业务插件完整单元测试 | BiliDownloader 720、DaTang 64、MySmallTools 183，共 967/967 |
| G3 构建变异与四插件包矩阵 | 变异负例通过；4 个插件各两轮 ZIP/sidecar/文件摘要一致；真实 Loader 4/4 |
| Core/UI API 兼容与变异 | Core 84、UI 42；无 G3 public API 漂移 |
| Core/UI 真实 nupkg 消费 | DLL/XML/nuspec/依赖白名单、2 个正例和 10 个反例通过 |
| 文档核心/正式门禁 | 通过 |
| `git diff --check` | 通过 |

测试数量只记录本次执行事实，不作为永久阈值。Host 数量与覆盖率来自本轮 TRX/Cobertura；业务插件
数量来自各自 `dotnet test` 输出；插件包摘要写入
`artifacts/test-results/ManagedPluginPackages/summary.json`。SDK 包门禁仅在系统临时目录生成和消费包，
没有发布或上传。

## 9. 明确排除项目

G3 没有实现每插件独立容器、最终 UI SDK 模块迁移、声明式贡献、Host Registry、Dock Adapter、
Document v2、layout v2 或业务插件功能。本轮不使用 AIFLOW，不读取或生成 `.aiflow` 内容；不运行
Windows CI、Windows Smoke、G14 发布总门禁、ReleaseAcceptance、联网/真实媒体、上传、标签或任何
发布操作。

## 10. 回滚边界与完成检查表

回滚单位固定为“Reader、清单模型、Loader、Preflight、Catalog、兼容诊断投影、公共 Props/Targets、
四插件声明、打包脚本、测试和文档”。如需撤销，应以可审阅的新提交整体回到 G2，不得留下 v1/v2
双 reader，不得只恢复模块扫描或 Host/Common 双区间，也不得修改、迁移或删除已有插件包和用户数据。

- [x] 生产 Reader 只接受严格 manifest v2，不保留 v1 分支。
- [x] Loader 与 Catalog 只解析和实例化清单精确入口，不扫描第二模块。
- [x] SDK 兼容事实、错误码和持久字段均收敛为单一区间。
- [x] 四插件声明精确入口与集中 SDK 区间，入口全名保持稳定。
- [x] MSBuild 独立编译探针先于 manifest、部署与打包输入生成。
- [x] 打包入口和 schema 2 sidecar 复核入口、SDK、版本与确定性摘要。
- [x] Reader、Loader、双模块、构建变异和四插件包矩阵测试齐全。
- [x] 根 README、文档导航、任务书、快速开始、排错、兼容约束和测试说明已同步。
- [x] 未使用 AIFLOW，未运行 Windows CI、Smoke、发布验收或发布门禁。
