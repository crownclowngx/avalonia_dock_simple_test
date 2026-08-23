# Host V4 G0：冻结 V3 源码基线

> 状态：已完成（2026-08-23）。本阶段只冻结 V4 的输入事实，不修改生产源码，
> 不建立发布资格，也不代表 V4 已经完成或封板。

## 1. 基线结论

V4 的唯一 V3 源码基线是提交 `16ce75e`（`refactor(host): 完成 G14 V3 封板`）。
其后的提交 `6585b9a` 只新增 V4 评审任务书，没有修改生产代码、SDK、插件或磁盘契约。

实施开始时的分支为 `codex/host-v4-g0-g6`，工作树干净；仓库没有指向 V3 G14 或当前
HEAD 的 V3 标签。G0 不创建标签，也不把 V3 G14 已有的本地发布资格改写为本轮重新签署。

## 2. 版本与契约事实

- 产品与 Plugin SDK 版本保持 `3.0.0`，活动 API 基线保持 `v3`。
- Core/UI V3 Shipped 分别为 127/45，Unshipped 均无新增签名。
- manifest、Document envelope 和 layout schema 均保持 2；布局文件仍为 `layout-v2.json`。
- Host 默认数据根仍为 `v2`；四插件 SDK 区间仍为 `[3.0.0, 4.0.0)`。

这些事实是后续 G1–G6 的兼容保护线。任何阶段若必须改变其中一项，都必须暂停 V4 并另行评审，
不能借 Host internal 重构修改 public API 或用户磁盘格式。

## 3. 本地非发布验证

从干净工作树执行了以下验证：

```powershell
dotnet tool restore
dotnet restore MyAvaloniaManagement.sln --locked-mode
dotnet build MyAvaloniaManagement.sln -c Release --no-restore -warnaserror
pwsh -NoProfile -File .\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release -NoRestore
dotnet test <SDK 与四插件测试项目> -c Release --no-restore -p:SkipPluginDeploy=true
```

结果为零失败、零跳过：

| 测试面 | 通过数 |
| --- | ---: |
| Host Unit | 189 |
| Host Headless UI | 62 |
| Host Plugin / Dock | 204 |
| Plugin SDK | 37 |
| MyPlugTest | 11 |
| DaTangAccountingHelpPlug | 62 |
| MySmallTools | 192 |
| BiliDownloader | 728 |
| 合计 | 1485 |

Host 合并行覆盖率为 **84.39%**，分支覆盖率为 **70.58%**。Release 构建零警告、零错误，
锁定还原成功。后续阶段不得通过降低这两个总覆盖率事实或删除高价值测试取得绿色。

## 4. SOLID 与设计取舍

- **SRP**：G0 只记录输入提交和可重复验证事实，不夹带任何生产重构。
- **OCP/LSP**：V3 已签署 SDK、插件与磁盘格式原样作为保护线，不重写历史文本。
- **ISP/DIP**：本阶段不增加接口、容器注册或测试专用生产入口。

没有引入工作流框架、发布编排器或新的抽象。G0 的价值是让 G1 以后每项破坏式 internal 修改都能
追溯到同一个已验证输入，而不是增加实现层次。

## 5. 非发布声明与回滚

本阶段未读取、初始化或修改 AIFLOW；未运行 Windows CI、Windows Smoke、ReleaseAcceptance、
`Invoke-HostV3ReleaseGate.ps1` 或其他发布门禁；未创建标签、上传包或执行外部发布。

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

G0 的回滚单位只有本记录。删除本记录不会改写 `16ce75e`、V3 G14 历史证据或任何用户数据。
