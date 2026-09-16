# Plugin SDK API 基线维护

> 用途：评审 SDK public 表面及兼容性。状态：当前；核对日期：2026-09-16。事实源：各 SDK 的 ApiCompatibility 文本、项目文件、[集中版本](../../Directory.Version.props)和 [Gate 契约检查](../../tools/MyAvaloniaManagement.Gate/GateApplication.cs)。

## 活动基线

| 契约 | 活动目录 | Shipped / Unshipped 条目 |
| --- | --- | --- |
| Core | [PluginSdk v3](../../Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v3) | 127 / 91 |
| UI | [PluginSdk.UI v3](../../Host/MyAvaloniaManagement.PluginSdk.UI/ApiCompatibility/v3) | 45 / 79 |
| Workflow | [PluginSdk.Workflow v1](../../Host/MyAvaloniaManagement.PluginSdk.Workflow/ApiCompatibility/v1) | 68 / 0 |

数量是本次对文本的核对快照，签名内容才是权威源。Core v1/v2、UI v2 是受保护的历史基线，不能为了整理文档删除或改写。Host 可执行程序集不属于插件 public API。

Core/UI 活动基线由 `MyAvaloniaPluginSdkApiBaseline` 指向 v3，包及程序集版本相同。Workflow 包号与六包统一，但保留 v1 API 和 1.0.0.0 二进制身份，不能用 NuGet 包主版本推断其 API 目录。

## 分类与发布的实际边界

Shipped 记录已经正式冻结的契约；Unshipped 是当前仍登记在增量文件中的表面。**在本仓，Unshipped 不能简单解释为“从未对外发布”**：Workflow Action、Workbench Command、图标新增已随公开包交付，历史发布保留了既有文本分类。

`verify` 允许这些条目存在；当前 `seal` 显式要求 Core/UI Unshipped 为零，因此当前基线尚不满足该项正式封板条件。NuGet 上传成功、开发 verify 成功和 Host seal 成功不是同一事实。

后续分类政策需要单独评审，已列入[待办](../roadmap/README.md)。不得为通过 seal 删除条目、直接清空增量文件、倒写历史 Shipped，或降低门禁。

## 日常变更

V10 建立了[3.4.1 发布输入与文本映射](../../build/ApiBaseline/3.4.1.json)，包含来自 NuGet 官方平面源的三个包及程序集 SHA-256、当前 Shipped/Unshipped 原文与摘要。严格二进制表面及参数名称比较用于核对发布归属；这份映射不声称恢复了发布源码的可空注解，也不改变文本分类。

开发 `verify` 在契约阶段调用 `tools/Verify-PluginApi.ps1`，以已发布程序集为左侧、当前程序集为右侧，使用固定的 `Microsoft.DotNet.ApiCompat.Tool 10.0.302`。兼容新增允许，删除和破坏性签名失败；严格比较额外记录表面是否相同。首次下载的包须匹配记录摘要，后续使用经过摘要检查的本地缓存。工具说明见[微软文档](https://learn.microsoft.com/dotnet/fundamentals/apicompat/global-tool)。

需要检验比较工具本身时，在构建和 `dotnet tool restore` 后运行：

```powershell
pwsh -NoProfile -File tools/Verify-PluginApi.ps1 -RunSelfTests
```

自测实际构建“兼容新增 / 删除 / 改签名”微型程序集，不生成忽略规则。输出包含摘要和每次比较日志；`seal` 的历史条件没有随本轮修改或解除。

仅调整 internal/private 实现时不修改 API 文本。新增 public API 时：

1. 用真实插件需求确定最小契约，说明线程、取消、异常和所有权，补齐 XML 注释。
2. 核对 Core 只依赖 BCL；UI 不泄漏 Dock、Host 或 Newtonsoft；Workflow 只公开共享协议。
3. 审阅分析器的新增、删除和破坏性变化诊断，按相应活动基线登记兼容新增，保持排序与唯一性。
4. 执行 SDK 与 Host 回归；涉及包依赖和二进制边界时另验证真实包、新旧消费者和独立加载上下文。
5. 同步当前契约、模板示例与有日期的验证记录。

删除、改可见性、改参数或返回类型均需按兼容影响评审；确需破坏时建立新主版本迁移方案，不能重写旧承诺。禁止通过 NoWarn、移除分析器、删除基线或 `*REMOVED*` 隐藏破坏。

## 当前验证入口

在主仓根目录执行：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

只定位 SDK 测试时可运行：

```powershell
dotnet test Host/MyAvaloniaManagement.PluginSdk.Tests -c Release -m:1
```

专项测试不替代完整 Gate；现有 Gate 不自动重放过去所有跨仓消费与发布实验。真实包验证、当前 seal 前提及 NuGet 发布流程分别见[主仓验证](../maintenance/verification.md)、[发布维护](../maintenance/nuget-release.md)。退役脚本和历史签署仅从[归档](../archive/README.md)查阅。
