# V3 G10：DaTangAccountingHelpPlug 验收

> 完成日期：2026-08-22
>
> 状态：已完成；本记录是开发期非发布证据，不是发布批准。
>
> 前置基线：[G9 MyPlugTest V3 验收](./g9-my-plug-test-v3-acceptance.md)

## 1. 结论

DaTangAccountingHelpPlug 的发票信息导入与银行余额调节继续以两个独立 Document 暴露，Excel 读取、
匹配、报告、DTO 和银行对账 content schema 1 均未改变。专项验收把测试从 Registry 推进到真实
`WorkspaceSession`、Dock Adapter、View 和 `DocumentSaveService`，证明两个 Scope 精确隔离，失败激活
不发布半成品，关闭后的文件选择结果不能迟到回写。

活动 V2 测试类、脚本和环境变量已经删除，当前入口为 `Test-DaTangAccountingHelpPlugV3.ps1` 与
`MYAVALONIA_G10_V3_PACKAGE_ROOT`。Plugin SDK public API、业务 DTO、磁盘信封和默认数据根没有变化。

## 2. 设计思路与时序

职责采用直接组合：插件模块声明两个 Document；每次激活建立独立 Scope；模型只维护业务状态和
Revision；严格 Codec 只解释 schema 1；Host 独占路径、信封、原子写入与 Workspace 发布。

保存竞争的关键时序是：模型捕获 Revision N，Host 开始主文件写入，用户编辑把 Revision 推进到 N+1，
主文件提交后 Host 只调用 `AcceptChanges(N)`。模型保持 Dirty，Host 返回
`Saved + HasPendingChanges`；第二次保存捕获 N+1 后才同时清除模型和标签修改标记。该测试使用受控存储
暂停真实写入，没有为生产模型增加测试接口。

文件选择依赖既有窄窗口端口。选择器返回后模型再次检查关闭令牌；取消返回空值，Document 已关闭时的
迟到结果也不提交。恢复先完整读取严格内容，再一次应用候选状态，避免半恢复对象进入 Workspace。

## 3. SOLID 对照

| 原则 | G10 落点 |
| --- | --- |
| SRP | 模型负责业务状态，Codec 负责内容协议，Host 保存服务负责文件事务，Workspace 负责发布。 |
| OCP | 两个 Document 只消费既有 V3 激活、Revision 和窗口端口，Host 无 DaTang 类型分支。 |
| LSP | 非持久化发票 Document 明确拒绝 Restore；银行 Document 完整遵守可持久化替换契约。 |
| ISP | 插件只依赖 Document 生命周期和窗口交互窄端口，不取得 Window、Dock 或 Host 容器。 |
| DIP | ViewModel 依赖 Excel/文件选择抽象和关闭令牌，不依赖静态窗口、服务定位器或磁盘信封实现。 |

没有引入 Manager、Facade、Repository、事件框架、抽象工厂或仅为测试存在的生产接口。

## 4. 兼容边界与删除面

- 保持插件版本 3.0.0、manifest schema 2、SDK `[3.0.0,4.0.0)`；
- 保持 Document envelope schema 2、银行内容 schema 1、业务 DTO 和 Excel 算法；
- 删除活动 DaTang V2 测试/脚本命名，历史 V2 G10 文档原文保留；
- 结构门禁禁止 Legacy、Dock、旧保存契约、Host EventBus、服务定位器、直接窗口访问和过渡构建开关回流；
- 最终 ZIP 必须通过真实 Loader、Provider 和 Workspace 看到两个 Document，而非只停在 Registry。

## 5. 实际自动化证据

```powershell
.\scripts\Test-DaTangAccountingHelpPlugV3.ps1 -Configuration Release -NoRestore
```

| 套件 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| Plugin SDK | 37 | 0 | 0 |
| Host Unit | 188 | 0 | 0 |
| Headless UI | 62 | 0 | 0 |
| Plugin / Dock | 204 | 0 | 0 |
| DaTang | 62 | 0 | 0 |
| 最终 ZIP → Workspace | 1 | 0 | 0 |
| 合计 | **554** | **0** | **0** |

Host 合并覆盖率为行 **84.39%**、分支 **70.58%**，高于 83.24% / 68.98% 基线。DaTang
整体为 70.09% / 49.31%；银行对账 Document 与严格 Codec 行覆盖率分别为 **97.10%**、**97.14%**。

两次隔离构建均生成 9 文件 ZIP，逐文件事实与归档完全一致；SHA-256 为
`1ADFA975BB9B3A04F58FA0948E05C13178067BD51CF8721B83061435B17465BD`。机器证据位于
`artifacts/test-results/DaTangAccountingHelpPlugV3/summary.json`。

## 6. 非发布声明与回滚

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
```

本阶段未运行 AIFLOW、Windows CI/Smoke、发布验收、签名、上传或标签。测试 ZIP 仅用于确定性和真实
Host 加载，不是发布制品。G10 的回滚单位是 DaTang 活动测试、V3 专项脚本、当前文档及验收暴露的最小
修正；不得回滚 G0–G9，也不得恢复 V2/V3 双入口或宽松内容读取。
