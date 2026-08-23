# Host V4 G3：Layout 文件与职责对齐

> 状态：已完成（2026-08-23）。输入为 G2 提交 `7b7ee50`；本阶段只重排 Host internal Layout 职责，不改变 V2 线格式。

## 1. 交付结果

原先文件名与类职责错位的两个文件已成对拆分：

- `DockLayoutLifecycle.cs` 只编排 `Prepare`、`ApplyPending` 和 `Save`；
- `DockLayoutSnapshotMapper.cs` 只保留 `Capture`、`EnsureSnapshotDocks` 和 `ApplySnapshot`；
- `DockLayoutRuntimeValidator.cs` 负责贡献可用性与运行时 Pane/Tool/Stable ID 校验。

测试已删除对 Lifecycle 静态转发 seam 的依赖，直接调用真实 Mapper 和 Validator。旧
`DockLayoutCoordinator.cs` 不再存在。

## 2. 恢复原子性与错误码

生命周期在调用 `EnsureSnapshotDocks` 之前先执行贡献校验。未注册、不可用或未产生
运行时 Tool 实例时，快照整体隔离，默认 Dock 树的 Pane 比例与结构保持零修改。贡献通过后再
校验 Pane 和稳定 ToolDock；应用阶段抛出可预期结构异常时，整体重建默认布局。

本轮显式断言 `LAYOUT_PLUGIN_MISSING`、`LAYOUT_PLUGIN_UNAVAILABLE`、
`LAYOUT_TOOL_ACTIVATION_MISSING`、`LAYOUT_PANE_MISSING` 和 `LAYOUT_TOOL_DOCK_MISSING`。既有测试继续覆盖
Pending 只消费一次、应用失败重建、Pinned/Hidden/Active 状态往返以及再次保存的 V2 线格式。

## 3. SOLID 与朴素设计

- **SRP**：Lifecycle 管时序，Mapper 管结构映射，Validator 管只读判定，Store 管磁盘与隔离。
- **DIP**：生命周期仅组合现有具体小组件，没有引入容器查找或全局状态。
- **ISP/OCP**：测试面就是真实用例面；不为测试保留转发方法，不扩展 Plugin SDK。

这是三个具体类的朴素职责分离，没有增加 Facade、Manager、事件总线或无生产价值接口。

## 4. 实际验证与回滚边界

```powershell
dotnet test .\Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj -c Release --no-restore --nologo
pwsh -NoProfile -File .\scripts\Test-HostV4DevelopmentGate.ps1 -Stage G3
```

锁定还原、Release `-warnaserror` 构建、三层 Host 测试、覆盖率、结构扫描和文档链接均通过。
Host 为 **458/458**（Unit 191、UI 62、Plugin 205），行/分支覆盖率为 **84.73% / 71.16%**，
构建 **0 警告 / 0 错误**。

本阶段未使用 AIFLOW，未运行 Windows CI/Smoke、ReleaseAcceptance 或 Host 发布门禁，未创建标签、
上传或发布，`publishable=false`。回滚必须整体回到 G2；不能只恢复旧文件名或 Lifecycle 转发 seam。
