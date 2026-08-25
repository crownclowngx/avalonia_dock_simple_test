# Workflow Action G2：SDK、Build 与外部模板传播门禁实施记录

> 状态：已完成（2026-08-25；完整非发布门禁通过）
>
> 最终机器摘要时间：`2026-08-25T05:32:22.0101341Z`（北京时间 2026-08-25 13:32:22）
>
> 产品：`3.0.0`；Core/UI SDK 本地候选：`3.1.0`
>
> Templates 本地候选：`1.1.0`；Build 已发布基线：`1.1.2`
>
> manifest schema：`2`；SDK 区间：`[3.1.0, 4.0.0)`
>
> 前置：[G1 Host Workflow Action 内核](./g1-host-workflow-action-kernel.md)

## 1. 结果与边界

G2 已把 G1 的 Workflow Action 能力传播到真实 Core/UI nupkg、通用外部模板和候选 Host 的实体加载链。
模板仍只生成中性的 Document 示例；Provider 和 Consumer 的代码放在生成文档中，避免把 Workflow Studio
业务实现塞进平台模板。同一插件首版不能同时承担 Provider 与 Consumer，两种角色必须分别建包。

公开基线仍是 SDK `3.0.0`、Templates `1.0.4`；G2 的 SDK `3.1.0` 和 Templates `1.1.0` 只在隔离的
本地候选源中验证，**没有上传或发布**。Build 协议没有变化，因此继续从 NuGet.org 精确还原已经发布的
`MyAvaloniaManagement.Plugin.Build 1.1.2`，没有用相同版本重打包制造另一个 Build 制品。

本阶段没有新增、删除或修改生产 public API。v3 Shipped 仍为 Core 127/UI 45，Unshipped 仍为 72/6；
`IPluginRegistration` 成员集合、Build props/targets、确定性 ZIP 协议和 manifest schema 均未改变。

## 2. 设计思路与 SOLID

实现采用四个直接责任，不增加工作流框架、服务定位器、通用模板抽象层、自定义模板引擎扩展或第二套构建协议。

| 原则 | G2 做法 |
| --- | --- |
| SRP | 模板负责创建时快照；Build 负责 manifest/资产/ZIP；外部消费门禁负责 NuGet 与 lock；Host 测试只负责真实加载和调用 |
| OCP | 只把既有 G1 API 传播到候选包和模板，不改写 v3 Shipped 或 `IPluginRegistration` |
| LSP | 公开 1.0.4 + SDK 3.0 仍可还原；使用 3.1 Action API 时以缺失符号失败，候选 3.1 则编译并运行 |
| ISP | Provider 只编译 `IWorkflowActionHandler`/`AddWorkflowAction`；Consumer 只编译 `UseWorkflowActionGateway` |
| DIP | 外部项目只指向 SDK/Build NuGet 契约，Consumer 不引用 Provider，Host 也不引用生成项目源码 |

类型名合法化复用模板引擎内置 `name` 变量和普通正则派生：命名空间保留原始点分名称，Module、Services
等类型只使用合法化后的末段标识符。因此 `-n MyAvalonia.WorkflowStudio` 可直接生成、锁定还原和编译，
不需要自定义模板扩展。

## 3. 包图与所有权

```text
公开 NuGet.org
  ├─ MyAvaloniaManagement.Plugin.Build 1.1.2 ──> 仅构建期
  └─ 其他第三方包

G2 隔离候选 feed
  ├─ MyAvaloniaManagement.PluginSdk 3.1.0
  ├─ MyAvaloniaManagement.PluginSdk.UI 3.1.0 ──> Core 3.1.0
  └─ MyAvaloniaManagement.Plugin.Templates 1.1.0
       ├─ Plugin packages.lock.json
       ├─ Standalone packages.lock.json
       └─ Tests packages.lock.json

Provider ZIP ──独立 ALC──┐
                         ├─ 候选 Host + Default ALC 中共享 SDK
Consumer ZIP ──独立 ALC──┘
```

G2 门禁在系统临时目录创建独立 NuGet 缓存、template hive、候选 feed、生成目录和 Host 装载目录；模板
生成物不得含 Host `ProjectReference`、源码链接、仓库路径或开发机绝对路径。每个生成解决方案的三个
lock file 都必须能在候选源映射下执行 `--locked-mode`。

.NET 10 生成 nupkg 时会写入非确定的 OPC 元数据。该差异不属于包内容契约，却会破坏可提交 lock file。
G2 只对本地候选 Core/UI/Template nupkg 规范化 OPC 关系 ID、core-properties 路径、条目顺序和 ZIP 时间；
DLL、nuspec、依赖和 API 字节保持原样。该步骤不是新的发布打包协议，也不触碰 Build `1.1.2`。

## 4. 模板内容

- Core/UI 精确锁定 `[3.1.0]`，Build 精确锁定 `[1.1.2]`；
- `ManagedPluginSdkMinInclusive=3.1.0`，最大版本保持 `4.0.0`，schema 保持 2；
- Plugin、Standalone、Tests 都提交 `packages.lock.json`；
- 默认实现仍只有 `MainDocument`/`MainView`；
- `docs/workflow-actions.md` 分别给出 Provider 和 Consumer 示例，并明确首版角色互斥；
- 模板是创建时快照，不提供覆盖更新既有项目的机制。

## 5. 门禁与实测证据

统一入口：

```powershell
pwsh -NoProfile -File .\scripts\Test-WorkflowActionG2.ps1 -Configuration Release
```

2026-08-25 的 Release 非发布实测结果：

| 项目 | 结果 |
| --- | ---: |
| Host Unit / Headless UI / Plugin | 226 + 65 + 207 = **498/498** |
| Host 行 / 分支覆盖率 | **85.7% / 71.76%** |
| SDK / G1 双插件复核 | **3/3 + 1/1** |
| 四业务插件单元回归 | **11 + 62 + 197 + 729 = 999/999** |
| Build 协议负例 / 既有真实插件包 | **25 / 4** |
| 生成解决方案 / 每方案 lock file | **4 / 3** |
| G2 外部 Host 实调 / 公开旧模板负例 | **1/1 / 1/1** |

外部 Host 专项证明 Provider/Consumer 位于两个独立 ALC，SDK 来自 Default ALC，Consumer 不引用
Provider；caller-bound Gateway 完成一次结构化回显，Handler 活动数归零且创建数等于释放数，敏感探针
没有进入诊断。Standalone 只做三秒有界启动检查并关闭临时进程，不计为 Windows Smoke。

| 制品 | SHA-256 |
| --- | --- |
| Core SDK 3.1.0 候选 nupkg | `1D9C08DE3B8805EAA3D1CB1C7C0290D4A4050C42C388C33D102B16CD05EB86AA` |
| UI SDK 3.1.0 候选 nupkg | `75EB8264B369A04DEA892DA173206401424345A6A309DA3B9536FAD1A4A12AE5` |
| Templates 1.1.0 候选 nupkg | `CCC3FB160C12BB9D3F4918DCC280CBB059C7CC9C3CC35BC54F61846983F4F355` |
| Provider ZIP / manifest | `9BBA4D9E4A03D573D68D15AEB08019A43EC747070AD3DC4F9369EC50842CDB64` / `5642C2C65F95C1D022F1A9171833804DFE2DA0B3B3A3790CC1A101BD16839E4B` |
| Consumer ZIP / manifest | `F8EFFDE4B1C0F1750DBDE8E75492FD072646F7C9D1CB60240E82030375ABA62C` / `592137823EBD3915B236BCC1D24E3AEF838CD70C92DBA88056F66ADA9C78A4E1` |

两轮 Provider、Consumer ZIP 和外置 manifest 分别字节一致。权威机器摘要位于 Git 忽略的
`artifacts/test-results/WorkflowActionG2/summary.json`；门禁只在全部阶段成功后写入该文件。

## 6. 非发布声明与回滚

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
published=false
uploaded=false
tagCreated=false
```

G2 没有使用上传密钥，没有执行 Windows CI、Windows Smoke、ReleaseAcceptance 或发布门禁。Release
只表示本地编译配置。回滚单位是 Templates `1.1.0`、G2 外部包测试、聚合门禁和本文档整体回到 G1；
Core/UI 的 G1 内核和已经发布的 Build `1.1.2` 不回滚、不改写。
