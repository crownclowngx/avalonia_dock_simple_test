# Managed Plugin v1 G1：支持边界与版本线冻结记录

> **历史说明：本 V1 阶段已由 Managed Plugin V2 G14 取代；以下日期、数量和结论保持原样。**

> 状态：已完成
> 完成日期：2026-08-15
> 分支：`dev-重构-2026年8月13日`
> 前置基线提交：`75c7d81`
> 所属任务：[Managed Plugin v1 封板评审与整改任务书](../../design/host-v1-sealing-readiness-plan.md#g1冻结-v1-支持边界与版本线)

## 1. 结果摘要

G1 已把 Managed Plugin v1 的支持范围、版本所有权和宿主数据目录从分散约定转成可执行政策。
根级 `Directory.Version.props` 是产品、Host API、Plugin SDK、manifest schema 与数据根代际的
集中事实；四个插件分别拥有唯一 `PluginVersion`。`VersionPolicyTests` 将这些属性与实际程序集、
四份严格清单和欢迎页交叉校验，漂移时报告具体插件与字段。

宿主布局、外观和诊断的生产默认根目录已从预发布目录切换为：

```text
%LOCALAPPDATA%\MyAvaloniaManagement\v1\
```

旧父目录不读取、不移动、不改写、不删除。自动化使用的 `MYAVALONIA_DATA_DIRECTORY` 仍表示完整
数据根，不追加 `v1`。最终锁定还原、Release 构建、三层测试、覆盖率和 Windows Smoke 全部通过。

## 2. 冻结的正式支持边界

Managed Plugin v1 只承诺以下组合：

- Windows x64；
- 同一进程内、由宿主方信任的 Managed Plugin；
- 插件具有严格 `plugin.manifest.json`、明确兼容区间和独立部署目录；
- 更新插件时退出宿主、替换文件并重新启动。

以下能力不属于 v1：运行时热卸载、恶意代码沙箱、权限系统、第三方市场、跨进程 UI、用户动态
启停、插件能力声明和缺失插件占位恢复。代码中当前仍存在的 Legacy 激活属于 G4 删除前的过渡
事实，不是 v1 二进制兼容承诺。G1 只冻结政策，没有提前删除该代码或修改 Plugin SDK public API。

## 3. 六条版本线及其所有权

| 版本线 | 当前值或状态 | 唯一所有者与规则 |
| --- | --- | --- |
| 产品版本 | `1.0.0` | Host 发布；映射 `Version`、`FileVersion`、`InformationalVersion` 和欢迎页 |
| Plugin SDK | `1.0.0`；程序集 `1.0.0.0` | `MyAvaloniaManagementCommon`；兼容新增升次版本，破坏契约升主版本 |
| 插件版本 | 四插件均为 `1.0.0` | 每个插件项目自己的 `PluginVersion`；派生程序集元数据并与清单精确一致 |
| manifest schema | `1` | Host 加载器；结构变化创建新 reader，不预留无语义字段 |
| 宿主持久化 schema | 布局、外观、诊断当前各为 `1` | 每种格式独立拥有整数 schema；不存在全局持久化版本 |
| 插件内容 schema | 由各插件格式拥有 | 不能使用插件发布版本替代；未知未来版本由内容所有者拒绝 |

Host API `AssemblyVersion` 为 `1.0.0.0`，用于当前清单的 Host 兼容检查。Document 宿主信封仍没有
schema，这是 G7 的明确阻断项；G1 没有用占位常量掩盖该缺口。普通进程内强类型消息也没有增加
装饰性的 `Version` 字段，破坏消息语义时应创建新类型或提升 SDK 主版本。

## 4. 实现边界

### 4.1 集中构建属性

- 新增 `Directory.Version.props`，由 `Directory.Build.props` 统一导入；
- Host 的产品版本、文件版本、信息版本和 Host API 程序集版本只引用根级属性；
- Common 的 SDK、包、文件、信息和程序集版本只引用根级属性；
- 四个插件各自只声明 `PluginVersion`，其他版本从它派生；
- 欢迎页固定读取 Host 程序集，而不是测试运行器或 Harness 的入口程序集。

Plugin SDK `AssemblyVersion` 在兼容的 1.x 发布中保持稳定，避免把 SDK 次版本误当成 CLR 主版本
身份；包版本与信息版本仍可按 SemVer 提升。插件入口不是共享契约，因此其程序集版本跟随插件
版本，并继续接受清单的精确一致性检查。

### 4.2 `HostDataRootPolicy`

新增 internal 静态 Policy，唯一职责是无副作用地解析数据根：

1. 非空显式配置直接规范化为绝对路径，不追加任何目录；
2. `null`、空或空白配置使用 `<LocalAppData>\MyAvaloniaManagement\v1`；
3. Policy 不创建目录、不迁移文件、不解释 schema；
4. 布局、外观和诊断只在各自边界拼接文件名或 `Diagnostics` 子目录。

显式路径构造函数继续保留，单元测试和组合根不需要依赖进程环境。插件业务数据库、凭据、缓存
和用户文件不属于宿主路径所有权，G1 没有修改 `BiliDataPaths` 或 MySmallTools 用户数据路径。

## 5. SOLID 与朴素设计取舍

- **SRP**：根级属性负责版本事实，Policy 负责路径选择，Store 负责具体文件读写；
- **OCP**：未来新增 schema reader 或提升版本，不在 v1 对象中预留无语义字段；
- **LSP**：未改变任何 Plugin SDK 接口、插件替换语义或环境变量的完整根含义；
- **ISP**：没有为单一路径政策创建接口、工厂或策略集合；
- **DIP**：Store 仍接受显式路径，生产默认构造才调用内部 Policy。

代码注释集中解释版本所有权、程序集版本为何在 1.x 保持稳定、旧数据为何必须原样保留，以及环境
变量为何不能追加 `v1`。没有用注释逐行复述赋值或路径拼接。

## 6. 自动化门禁

`VersionPolicyTests` 包含四项仓库政策测试：

- 根级产品/SDK 属性与实际 Host/Common 程序集元数据一致；
- 欢迎页显示 Host 产品版本 `1.0.0`；
- manifest schema、数据根代际与代码常量一致；
- 四个插件项目版本、入口程序集、清单版本、入口文件名和兼容区间一致。

`HostDataRootPolicyTests` 覆盖默认、空白、显式覆盖和旧目录保留，Unit 测试总数增加五项。
版本政策增加四项 Plugin 测试。Windows Smoke 继续通过显式完整根运行，
证明没有被额外追加 `v1`。

## 7. 最终验证证据

执行命令：

```powershell
dotnet restore MyAvaloniaManagement.sln --locked-mode --nologo
dotnet build MyAvaloniaManagement.sln `
  -c Release -p:SkipPluginDeploy=true --no-restore --nologo
dotnet test Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj `
  -c Release --no-build --no-restore --filter "FullyQualifiedName~VersionPolicy"
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 `
  -Configuration Release -NoRestore -WindowsSmoke
git diff --check
```

最终结果来自 2026-08-15 生成的 TRX 与 `summary.json`：

| 门禁 | 结果 |
| --- | --- |
| 锁定还原 | 通过，锁文件没有变化 |
| 解决方案 Release 构建 | 0 警告、0 错误 |
| VersionPolicy 定向测试 | 4/4 通过 |
| `MyAvaloniaManagement.Tests` | 110/110 通过 |
| `MyAvaloniaManagement.UiTests` | 32/32 通过 |
| `MyAvaloniaManagement.PluginTests` | 116/116 通过 |
| 测试合计 | 258/258 通过，无跳过 |
| Host 覆盖率 | 行 77.01%，分支 63.79% |
| Windows Smoke | 通过 |

G0 的 249 项是独立历史基线，仍保留在 G0 文档中；258 是完成 G1 后的时间点证据，不是永久固定
门槛。后续继续从 `artifacts/test-results/MyAvaloniaManagement/summary.json` 动态读取。

## 8. 回滚与后续

G1 可以作为独立变更回滚。回滚版本属性不会修改已生成的程序集；回滚数据根政策也不会删除
`v1` 目录。旧版与新版分别读取自己的默认目录，显式环境变量目录始终由调用方负责。

完成 G1 不代表宿主已经封板。G2 仍需收口 Host public 面，G3 形成正式 Plugin SDK，G4 删除
Legacy，G7/G8 建立 Document 信封与保存边界；其余任务继续按主整改计划执行。

## 9. 完成检查表

- [x] 根级版本事实建立并由所有相关项目引用；
- [x] 四插件各自只维护一个插件版本源；
- [x] 版本、清单和实际程序集具有可读差异门禁；
- [x] v1 支持边界与 Legacy 过渡事实明确区分；
- [x] 默认宿主数据根切换到 `v1`；
- [x] 显式数据根不追加 `v1`；
- [x] 旧预发布目录保持原样；
- [x] 未增加 public API、空接口或消息版本占位；
- [x] Release 构建、三层测试、覆盖率和 Windows Smoke 全部通过；
- [x] 主计划、兼容契约、Quick Start、测试说明和文档索引同步更新。
