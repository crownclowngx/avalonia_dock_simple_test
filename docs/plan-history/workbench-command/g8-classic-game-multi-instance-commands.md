# Workbench Command G8：ClassicGame 全游戏多实例命令

> 状态：已完成（2026-08-28；双仓本地非发布门禁通过）。
>
> Host 输入：Workbench Command G7 绿色实现及当前 G0–G7 工作树。
>
> ClassicGame 输入提交：`1d11f1689433caf242365480233dd76ff5c8836b`
>
> ClassicGame 输入 tree：`1f3e826735c6ea191bbf68cb658557260a734fc2`

## 1. 交付事实

ClassicGame 从 `1.0.0` / SDK `3.2.0` 升级为 `1.1.0` / Core+UI SDK `3.3.0`，manifest 区间为
`[3.3.0, 4.0.0)`。原 13 个 Document、schema 2、布局/Document 格式和数据根均未改变。外部工作树的 304 项
状态是 LF→CRLF；忽略行尾后没有语义差异，实施未 reset、清理或全仓格式化。

初始 locked restore 因历史 3.2 lock hash 与公开源不一致产生 `NU1403`，按真实失败记录。最终门禁只使用
NuGet.org、隔离缓存和同步后的 locked restore，不使用 Host/SDK 源码 ProjectReference。

## 2. 架构与 SOLID

```text
ClassicGame 22 个不可变 Descriptor
  ├─ 13 × Restart → Tools/Hide
  ├─  9 × Undo    → Tools/Hide
  └─ Gomoku       → Ctrl+Shift+R / Ctrl+Z
                         │
                         ▼
Host Catalog → Context → State/Executor（执行前重查）
                         │ 活动 Document Scope
                         ▼
IWorkbenchDocumentCommandTarget
                         │
内部 WorkbenchDocumentCommandAdapter → 既有 RelayCommand
```

`PluginIds` 只保存稳定身份；Module 只创建 Descriptor；每个 Document 只选择已有业务命令；内部 Adapter 只处理
协议分派、状态事件和释放。领域规则、AI、历史、计时和 View 命令不迁移。事件 sender 保持为 Document Target，
满足 Host 对当前订阅引用的防迟到校验；Dispose 先 fail closed/退订，再释放 ViewModel。

2048、扫雷、消消乐、俄罗斯方块没有 Undo，因此没有占位命令。蜘蛛纸牌和空当接龙的 Restart 复用重开同局。
Host 3.3 每条 Command 只能绑定一个 DocumentTypeId，并拒绝同 owner 重复 Gesture；G8 因而只保留五子棋
快捷键样本，不修改 SDK/Host 生产内核。22 条 Catalog 命令已可被 G9 Palette 投影，G8 没有实现 Palette UI。

## 3. 验收链

ClassicGame 门禁执行 locked restore、Release 零警告构建、格式、全量单测与 Cobertura、Standalone、两轮
确定性四文件 ZIP、manifest/SDK 排除和文档链接检查。

最终声明矩阵为 13 条 Restart、9 条 Undo，共 22 条命令与 22 条 Tools 菜单。

Host 专项把同一真实 ZIP 交给生产 Loader、独立 ALC、Plugin Provider、Registry 和 Document Scope：

- 构造全部 13 个真实游戏，逐一执行 Restart 并核对只有 9 个 Undo；
- 两个真实五子棋实例形成 A 可撤销、B 不可撤销，切换后状态与执行只作用当前实例；
- Headless MainWindow 逐个验证 13 个游戏 Tools 菜单 Hide/Enabled；
- 菜单与五子棋快捷键共享同一个 Host Adapter，关闭窗口后 KeyBinding/订阅归零。

最终实测为：

- ClassicGame **526/526**，失败 0、跳过 0，覆盖率 **71.75% / 58.36%**；
- `GomokuDocument` 和 `WorkbenchDocumentCommandAdapter` 行覆盖率均为 **100%**；
- Host 基础门禁 **575/575**，覆盖率 **86.98% / 72.42%**；
- 真实包 PluginTests **1/1**，Headless UI **1/1**；
- 两轮确定性 ZIP 各 4 个文件，SHA-256：
  `4A1C7358BEEC84361C123E1B60ABEE2F372190DAD930FE0FE10F4CFE31F77EB9`。

机器摘要位于：

```text
artifacts/test-results/WorkbenchCommandG8/summary.json
```

## 4. 非发布与回滚

```powershell
pwsh -NoProfile -File .\scripts\Test-WorkbenchCommandG8.ps1 -Configuration Release
```

该入口只复用 Host 本地开发门禁，不运行 Windows CI、Windows Smoke、Release Acceptance、Host Release Gate
或其他发布门禁。Release 仅是本地编译配置；不上传、不签名、不打 tag、不形成发布资格。

回滚只移除 ClassicGame 22 条 Command/菜单、2 条快捷键 Placement、13 个 Target、内部 Adapter 和 G8 专项
测试/脚本/文档，并恢复插件/SDK/lock file；Host 3.3 生产代码、13 个游戏原 View 行为和数据格式保持不变。

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
