# Workbench Command G7：外部 WorkflowStudio 三条真实命令

> 状态：已完成（2026-08-28）；本地非发布门禁通过。
>
> Host 输入提交：`97732d21ad16676a38a298d6a8fda3140d467759`
>
> WorkflowStudio 输入提交：`0b3a3f55f43e66a914099f011dd344e7f556b56e`
>
> 前置：[G6 SDK 3.3、模板与独立消费](./g6-sdk-candidate-template-independent-consumption.md)
>
> 总设计：[Workbench Command 引入任务书](../../design/workbench-command-introduction-plan.md#g7迁移外部-workflowstudio-三条真实命令)

## 1. 目标与版本

G7 在独立 WorkflowStudio 仓库迁移 Validate、Run、Cancel 三条真实命令，重点验证异步运行状态、取消、关闭
协作和同类型多实例。Studio 插件从 `1.1.0` 提升到 `1.2.0`，精确引用 Core/UI `3.3.0`，manifest SDK
区间为 `[3.3.0, 4.0.0)`；Workflow SDK `1.0.0`、Build `1.1.2`、manifest schema 2、Definition v2
均不改变。

Host 不引用 Studio 项目，Studio 也不引用 Host/SDK 源项目。Studio 从 NuGet.org locked restore，Host 只消费
Studio 门禁生成的真实 ZIP 解压目录。

## 2. 执行与所有权图

```text
外部 WorkflowStudio 1.2.0 ZIP
        │ 生产 Loader / 独立 ALC
        ▼
不可变 Command + Menu + KeyBinding Registry
        │
        ├─ Tools / workflow：Validate(0) → Run(10) → Cancel(20)，非 Studio 时 Hide
        └─ F6 / F5 / Shift+F5
                    │ Host Context + Executor 执行前重查
                    ▼
当前 MainDocument : IWorkbenchDocumentCommandTarget
        ├─ ValidateDefinition
        ├─ WorkflowRunSession.RunAsync → caller-bound Gateway → 第二 ALC Action
        └─ WorkflowRunSession.Cancel + ClosingToken
```

Host 创建并释放 `MenuItem`、`KeyBinding` 和执行适配器；插件只声明稳定身份与不可变 Descriptor。Target 属于
Document Scope，运行门闩、状态、Secret 和 RunSession 都是实例字段。关闭后 Target fail closed，运行命令返回
真实可等待任务，状态事件按具体 CommandId 发布。

## 3. SOLID 与朴素设计

| 原则 | G7 落地 |
| --- | --- |
| SRP | 身份、声明、实例用例、Host 投影/路由和跨仓库验收分别拥有单一职责 |
| OCP | 三条命令通过公开 Descriptor/Target 扩展，不修改 Host Executor 或 Workflow Runner |
| LSP | Host 对真实 Studio 只使用 SDK 接口；默认 ALC SDK 与独立 ALC 插件类型不混用 |
| ISP | Target 不取得 Registry、Provider、Dock、Control 或通用服务定位能力 |
| DIP | Studio 依赖公开 NuGet，Host 依赖 ZIP 和 SDK 抽象，没有双向源码引用 |

使用的模式只有稳定身份常量、不可变描述符、窄 Adapter 和实例状态通知。没有引入 Mediator、事件总线、反射
命令发现、字符串 `when`、服务定位器、第二套 Runner 或 Workflow Action 治理副本。中文 XML/设计注释重点说明
状态所有权、执行前防御、取消与 UI 对象边界。

## 4. 三层测试证据

### 4.1 WorkflowStudio 仓库

- 单元测试 **54/54**，无失败、无跳过；
- 总行/分支覆盖率 **89.78% / 83.95%**，`MainDocument.cs` 行覆盖率 **91.39%**；
- 覆盖 idle/running/closing 状态、未知 ID、预取消、真实 Run/Cancel、Secret 正负例和两个实例隔离；
- Standalone Fake 自检 4 次 Action 调用、1 个 Run 释放；
- 两轮 4 文件 ZIP 哈希一致：`27C0C59BD7CC08AB7035AF777BB9C1A3D397258B37D363EDF2EC7CD88F2F2E6D`。

### 4.2 Host 真实包与业务 Action

PluginTests **2/2**：第一个测试经生产 Loader/Provider/Registry/Scope 验证三条命令、菜单、快捷键、默认 ALC
共享 SDK 和两个 Studio Document；第二个测试把 Studio 与 WorkflowActionG1 Provider 两份真实包放入两个独立
ALC，实际运行 echo Action 并得到“工作流执行成功”。Host 只反射 Studio 的公开绑定面来编辑测试定义，没有
引用外部具体类型。

### 4.3 Host Headless UI

Headless UI **1/1**：无活动 Studio 时菜单隐藏、快捷键禁用；切换到两个真实 Studio Document 后 `F6` 仅更新
当前实例，菜单与快捷键共享同一命令对象；移除活动目标后恢复隐藏/禁用，关闭窗口后 KeyBinding 集合归零。

Host V4 G7 当前基础开发门禁为 **573/573**，行/分支覆盖率 **86.98% / 72.39%**，没有低于 G6 基线。

## 5. 门禁

```powershell
pwsh -NoProfile -File .\scripts\Test-WorkbenchCommandG7.ps1 -Configuration Release
```

入口组合 Host 本地开发门禁、Studio 独立门禁、真实包 PluginTests、Headless UI 和文档门禁。机器摘要位于
`artifacts/test-results/WorkbenchCommandG7/summary.json`；Studio 的独立摘要位于其
`artifacts/test-results/WorkflowStudioG7/summary.json`。

## 6. 非发布与回滚

本阶段不使用 AIFLOW，不调用 Windows CI、Windows Smoke、Release Acceptance 或发布门禁；不上传 Studio
`1.2.0`，不签名、不打 tag，也不产生发布资格。Release 仅是本地编译配置。

回滚必须整体移除 Studio Command/Placement 身份、Module 声明、Target 适配、G7 脚本/测试/文档，并恢复
`1.1.0` 与 SDK `3.2.0` lock file。原编辑器按钮、定义、Validator、Runner、Secret 和 Workflow Action
Gateway 必须保留，不能通过删除现有业务路径换取表面上的“统一”。Host 的 G1–G6 Command 内核可独立保留。

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
published=false
uploaded=false
signed=false
tagCreated=false
```
