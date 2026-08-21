# G13：删除 V1 生产面

> 完成日期：2026-08-22。
>
> 性质：开发期破坏式收口与非发布验证，不是发布验收。摘要固定为 `aiflow=false`、
> `windowsCi=false`、`windowsSmoke=false`、`releaseAcceptance=false`、
> `releaseGate=false`、`publishable=false`。

## 1. 结果

`MyAvaloniaManagement.LegacyPluginContracts` 项目、`MyAvaloniaManagementCommon.dll`、旧 Strategy、
注册上下文、保存快照、生命周期适配和双轨测试夹具已经从活动项目图删除。Host、四个业务插件、统一
MSBuild Target 与独立 ZIP 构建入口现在只认识 `MyAvaloniaManagement.PluginSdk` 和
`MyAvaloniaManagement.PluginSdk.UI`。

V2 public API、manifest/document/layout schema、产品与 SDK 版本及四插件业务行为均未改变。历史
`ApiCompatibility/v1` 文本和 `docs/plan-history/host-v1/` 继续作为已签署事实保留，但不参与编译、加载、
DI、持久化或打包。用户现有 V1 文件和数据目录不读取、不迁移也不删除。

## 2. SOLID 责任划分与设计思路

- `PluginId`、`IHostEventBus` 和 `IPluginLifecycle` 只有 V2 SDK 一个事实源；Host 不再进行类型转换或版本分派。
- manifest reader 只负责严格读取 V2 数据，Provider Owner 只负责私有容器和 V2 生命周期解析，构建 Target
  只负责 V2 入口编译探针。每个组件只有一个变化原因，满足 SRP。
- 插件只依赖 Core/UI 的窄接口，Host 内部状态机、Dock、诊断和存储实现不会反向进入 SDK，保持 ISP 与 DIP。
- Document/Tool 注册仍通过 Descriptor 和泛型约束扩展，删除兼容分支没有增加类型判断框架，保持朴素 OCP。
- 测试组合根直接提交 V2 Descriptor，不再伪造 Strategy 激活器；错误入口夹具只是普通类型，Loader 必须在
  构造前拒绝它。该设计避免为了测试保留第二套生产协议。

本轮没有引入服务定位器、通用适配器、公共基类、反射扫描框架或额外状态机。能直接替换为 V2 类型的地方
直接替换；没有消费者的重载、转发和夹具整体删除。

## 3. 删除与保留边界

| 类别 | G13 结果 |
| --- | --- |
| Legacy 项目与程序集 | 项目、解决方案项、项目引用、包锁依赖和输出闭包全部删除 |
| Host 运行时 | manifest、诊断、Registry、View、生命周期和事件总线直接使用 V2 类型 |
| 构建协议 | 删除入口契约选择开关；入口探针与打包反射预检无条件使用 V2 UI SDK |
| 旧 Host/Common 区间 | 从活动 Target、项目和测试夹具删除，只保留单一 SDK 左闭右开区间 |
| Newtonsoft | Host 与集中包版本删除；SDK、Host 和四插件运行闭包均不引用 |
| 私有依赖 `PluginV1/PluginV2` 夹具 | 保留；名称表示第三方依赖版本，不是 Managed Plugin V1/V2 双轨 |
| v1 API 文本与历史文档 | 保留为审计事实，专项源码扫描显式排除历史区域 |

## 4. 失败矩阵

| 失败点 | 门禁行为 |
| --- | --- |
| 活动源码恢复 Legacy namespace、项目或过渡属性 | G13 源码扫描立即失败 |
| 插件使用旧 Strategy、旧保存接口或独立 `AddView` | 反向消费夹具编译失败才算通过 |
| 清单入口是 public 普通类型但未实现 V2 模块 | Loader 在构造前隔离，不执行抛异常构造函数 |
| Host 或插件依赖闭包出现 Common/Legacy/Newtonsoft | 本次构建的 `.deps.json` 扫描失败 |
| 插件 ZIP 携带 Host/SDK/Legacy 共享程序集 | 包白名单与最终 ZIP 复验失败 |
| 四插件两轮文件、长度、哈希或归档摘要漂移 | 确定性包矩阵失败 |
| 文档重新宣称 Legacy 为活动入口 | 当前事实与链接门禁失败 |

## 5. 非发布专项门禁

统一入口：

```powershell
.\scripts\Test-HostV2ProductionSurface.ps1 -Configuration Release
```

该入口包含 locked restore、全解决方案 Release `-warnaserror`、Host Unit/UI/Plugin 覆盖率、SDK 测试、
DaTang/MySmallTools/BiliDownloader 完整单元测试、SDK 包与真实编译负例、诊断脱敏、文档门禁，以及四插件
各两次隔离测试 ZIP 和真实 Host Loader/Registry/私有 Provider 组合。

2026-08-22 实测结果如下：

- Host Unit **168/168**、Headless UI **52/52**、Plugin **202/202**，合计 **422/422**；
- Host 行覆盖率 **83.19%**、分支覆盖率 **68.81%**，高于既有 **83.15% / 68.74%** 门槛；
- PluginSdk **34/34**、DaTang **62/62**、MySmallTools **184/184**、BiliDownloader **718/718**；
- SDK Core/UI nupkg、两个正向消费者及十个反向消费夹具通过；包协议矩阵的 **25** 个结构负例通过；
- Release `-warnaserror` 全解决方案构建为 **0 警告 / 0 错误**，诊断扫描检查 **102** 个生产 C# 文件；
- 文档门禁检查 **50** 份文档、**290** 个本地链接、**100** 个脚本路径和 **37** 个项目路径。

四插件均以同一源码完成两次隔离构建，逐文件内容、ZIP 长度与 SHA-256 完全一致：

| 插件 | 文件数 | 测试 ZIP SHA-256 |
| --- | ---: | --- |
| BiliDownloader | 14 | `2D291E1A3140C649C992F8EF9795336015B194755FB167553F8D111210DE9A5E` |
| DaTangAccountingHelpPlug | 9 | `899D7D5478863F530E1632FCBF78A8F36A8D1EC0376CAEDBD4D4ED450EAFE7E2` |
| MyPlugTest | 11 | `6339D0BFE11460807C2271D58391A7D3056BAF3A0FB8B8FDFAED2A673211E1C9` |
| MySmallTools | 431 | `90F8D47495F4135B0632C26DF822C37082452FFDDF49225C8406D6BD1EE6D21C` |

机器可读证据位于 `artifacts/test-results/HostV2ProductionSurface/summary.json` 及其
`ManagedPluginPackages/summary.json`。这些摘要是本轮审计证据，不把测试数量或哈希硬编码为未来门槛。

## 6. 明确未执行的流程

本阶段没有使用 AIFLOW，没有运行或修改 Windows CI、Windows Smoke、ReleaseAcceptance、发布脚本、
发布总门禁、签名、上传或标签流程。Release 配置只用于编译器、测试和覆盖率一致性，不代表发布放行；
所有 ZIP 都是可删除的测试制品，`publishable=false`。

G13 如需回滚必须整体回滚，不能选择性恢复单个 Legacy 类型、入口开关或兼容 reader。
