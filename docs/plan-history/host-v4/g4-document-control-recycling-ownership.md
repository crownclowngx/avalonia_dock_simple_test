# Host V4 G4：Document 控件回收器显式所有权

> 状态：已完成（2026-08-23）。输入为 G3 提交 `79033e2`；本阶段只收口 Host internal UI 资源所有权。

## 1. 唯一实例与两条使用链

组合根为每个 Host 容器注册唯一 `DocumentControlRecycling`，并用构造注入创建
`DockDocumentLifetime`。`WorkspaceSession` 必须显式接收 Lifetime，不再自行 `new`。

`App.axaml` 删除了回收器实例，DockControl Style 改为 `DynamicResource ControlRecyclingKey`。
`App.Initialize` 在生产 XAML 加载后安装同一 DI 实例。两条链因此共享一个可审阅所有者：

1. 组合根 → App Resource → DockControl Style → 标签切换复用；
2. 组合根 → DockDocumentLifetime → WorkspaceSession 关闭提交 → 单项移除。

`DockDocumentLifetime` 已彻底删除 `Application.Current.Resources` 查找，也没有改用服务定位器。

## 2. 异常与隔离语义

回收器先移除当前 Document 的强引用、解除视觉父级与 DataContext，然后让 Adapter 的 View 租约
执行幂等释放。即使 View 的 `Dispose` 抛出，Lifetime 的 `finally` 仍会释放 Adapter，进而取消
ClosingToken 并释放 Document Scope。重复关闭和 Runtime 退出兜底保持幂等。

Headless UI 测试用生产 App XAML 证明 Resource、DockControl 附加属性与 App 构造依赖是同一
实例。单元测试证明同容器多 App 共享实例，不同容器完全隔离；另覆盖标签复用、
单项移除、多 Document 隔离和 View 释放失败。

## 3. SOLID 与设计思路

- **SRP**：回收器管控件缓存，Lifetime 管关闭释放顺序，Session 管 Document 所有权提交。
- **DIP**：Lifetime 依赖构造期确定的对象，不依赖 Avalonia 全局应用状态。
- **ISP**：没有为注入新增无行为价值接口；现有两个具体小类就是完整边界。
- **OCP/LSP**：标签切换复用、Dock 关闭和 Plugin SDK 契约均不变。

本阶段只使用构造注入和明确所有者，没有新增 Manager、Facade、事件总线或服务定位器。

## 4. 实际验证、资源结果与回滚

```powershell
pwsh -NoProfile -File .\scripts\Test-HostV4DevelopmentGate.ps1 -Stage G4
pwsh -NoProfile -File .\scripts\Test-MySmallToolsV3.ps1 -Configuration Release -NoRestore -HarnessCycles 20
```

G4 开发门禁完成锁定还原、Release `-warnaserror` 构建、三层 Host 测试、覆盖率、结构与文档扫描。
Host 为 **461/461**（Unit 193、UI 63、Plugin 205），行/分支覆盖率为 **84.80% / 71.18%**，
构建 **0 警告 / 0 错误**。

MySmallTools 专项为 **691/691**。本地真实媒体 Harness 完成 **20 轮**，关闭后 Document、View、
加密流弱引用和最终原生资源全部归零；测试 ZIP 为 431 个文件。这是用户指定保留的本地
资源所有权验证，不是 Windows CI 或发布门禁。

本阶段未使用 AIFLOW，未运行 Windows CI/Smoke、ReleaseAcceptance 或 Host 发布门禁，未创建标签、
上传或发布，`publishable=false`。回滚必须整体回到 G3，不能恢复全局资源查找或 XAML 自行构造。
